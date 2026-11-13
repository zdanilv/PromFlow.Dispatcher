using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class EquipmentCardParametersDialogViewModelTests
{
    // DLG-01, DLG-02
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Visibility_filters_dialog_rows_in_card_order(bool isAdminMode)
    {
        var card = MixedVisibilityCard();
        var original = card.Parameters.ToArray();
        using var viewModel = new EquipmentCardParametersDialogViewModel(
            card, null, new CapturingDispatcher(), isAdminMode: isAdminMode);

        Assert.Equal(ExpectedVisibleIds(isAdminMode), viewModel.Parameters.Select(row => row.SignalId));
        Assert.Equal(original, card.Parameters);
    }

    // DLG-03, DLG-05
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parameters_without_explicit_visibility_are_user_parameters_in_both_modes(bool isAdminMode)
    {
        var card = Card(
            Parameter("Первый", "card.first", SignalBindingDirection.Read, SignalValueType.UInt16),
            Parameter("Второй", "card.second", SignalBindingDirection.Write, SignalValueType.Bool));
        using var viewModel = new EquipmentCardParametersDialogViewModel(
            card, null, new CapturingDispatcher(), isAdminMode: isAdminMode);

        Assert.All(card.Parameters, parameter => Assert.Equal(EquipmentParameterVisibility.User, parameter.Visibility));
        Assert.Equal(new[] { "card.first", "card.second" }, viewModel.Parameters.Select(row => row.SignalId));
    }

    // DLG-04
    [Fact]
    public async Task All_admin_parameters_in_user_mode_leave_an_empty_dialog_that_can_close()
    {
        var dispatcher = new CapturingDispatcher();
        var store = new RecordingValueStore();
        var card = Card(MixedVisibilityCard().Parameters[1]);
        using var viewModel = new EquipmentCardParametersDialogViewModel(
            card, null, dispatcher, valueStore: store, isAdminMode: false);
        var result = viewModel.Result.FirstAsync().ToTask();

        Assert.Empty(viewModel.Parameters);
        Assert.True(viewModel.HasNoParameters);
        Assert.Equal("Для текущего режима нет доступных настроек оборудования.", viewModel.EmptyStateMessage);
        Assert.False(await viewModel.SaveCommand.CanExecute.FirstAsync());
        Assert.True(await viewModel.CloseCommand.CanExecute.FirstAsync());

        await viewModel.CloseCommand.Execute().FirstAsync();
        Assert.False(await result);
        Assert.Empty(store.Saved);
        Assert.Empty(dispatcher.Requests);
    }

    // DLG-06
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Direction_controls_editing_independently_of_visibility(bool isAdminMode)
    {
        var card = Card(
            Parameter("Read", "card.read", SignalBindingDirection.Read, SignalValueType.Bool),
            Parameter("Write", "card.write", SignalBindingDirection.Write, SignalValueType.String),
            Parameter("ReadWrite", "card.readwrite", SignalBindingDirection.ReadWrite, SignalValueType.UInt16),
            Parameter("Admin Read", "card.admin.read", SignalBindingDirection.Read, SignalValueType.Date)
                with { Visibility = EquipmentParameterVisibility.Admin },
            MixedVisibilityCard().Parameters[1]);
        using var viewModel = new EquipmentCardParametersDialogViewModel(
            card, null, new CapturingDispatcher(), isAdminMode: isAdminMode);

        (string Id, bool CanEdit)[] expected =
            [("card.read", false), ("card.write", true), ("card.readwrite", true)];
        if (isAdminMode)
            expected = [.. expected, ("card.admin.read", false), ("parameter.admin.1", true)];
        Assert.Equal(expected, viewModel.Parameters.Select(row => (row.SignalId, row.CanEdit)));
    }

    // DLG-07, DLG-08, DLG-09, DLG-10
    [Theory]
    [InlineData(false, true, "20")]
    [InlineData(true, true, "20")]
    [InlineData(false, true, "некорректное число")]
    [InlineData(false, false, "20")]
    [InlineData(true, false, "20")]
    public async Task Save_only_processes_visible_editable_parameters(
        bool isAdminMode, bool isConnectionAvailable, string adminSavedValue)
    {
        var dispatcher = new CapturingDispatcher();
        var store = new RecordingValueStore();
        var validator = new RecordingWriteValidator();
        var card = MixedVisibilityCard(adminSavedValue);
        var original = card.Parameters.ToArray();
        using var viewModel = new EquipmentCardParametersDialogViewModel(
            card, null, dispatcher, validator, isConnectionAvailable: isConnectionAvailable,
            valueStore: store, isAdminMode: isAdminMode);
        var visibleIds = ExpectedVisibleIds(isAdminMode);
        Assert.Equal(visibleIds, viewModel.Parameters.Select(row => row.SignalId));
        Assert.True(await viewModel.SaveCommand.CanExecute.FirstAsync());
        foreach (var row in viewModel.Parameters)
            row.ValueText = row.SignalId switch
            {
                "parameter.user.1" => "11",
                "parameter.admin.1" => "21",
                "parameter.user.2" => "31",
                _ => throw new InvalidOperationException(row.SignalId)
            };

        await viewModel.SaveCommand.Execute().FirstAsync();

        var expectedOnlineIds = isConnectionAvailable ? visibleIds : [];
        EquipmentParameterSetpoint[] expectedSetpoints = isAdminMode
            ? [new("parameter.user.1", "11"), new("parameter.admin.1", "21"), new("parameter.user.2", "31")]
            : [new("parameter.user.1", "11"), new("parameter.user.2", "31")];
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(expectedSetpoints, store.Saved);
        Assert.Equal(!isConnectionAvailable, store.LastPendingAutoDispatch);
        Assert.Equal(expectedOnlineIds, validator.SignalIds);
        Assert.Equal(expectedOnlineIds, dispatcher.Requests.Select(request => request.SignalId));
        Assert.Equal(expectedOnlineIds, store.Completions.Select(completion => completion.SignalId));
        Assert.Equal(original, card.Parameters);
    }

    // DLG-11
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Disabled_card_opens_filtered_settings_with_or_without_connection(bool isConnectionAvailable)
    {
        var dispatcher = new CapturingDispatcher();
        var dialog = new UserParametersDialogStub(dispatcher);
        var card = MixedVisibilityCard();
        var cardViewModel = new EquipmentCardViewModel(card, dispatcher, parametersDialogService: dialog);
        cardViewModel.ApplyRuntime(new RouteObjectRuntimeState(
            card.Id, isConnectionAvailable ? RouteObjectState.Disabled : RouteObjectState.Offline,
            card.StatusText, ValueText: null, IsVisible: true, CanStart: false, CanStop: false, IsEnabled: false),
            isConnectionAvailable: isConnectionAvailable);

        Assert.False(cardViewModel.IsEnabled);
        Assert.True(cardViewModel.IsParametersButtonEnabled);
        Assert.True(await cardViewModel.OpenParametersCommand.CanExecute.FirstAsync());
        await cardViewModel.OpenParametersCommand.Execute().FirstAsync();

        Assert.Equal((card, isConnectionAvailable), Assert.Single(dialog.Opened));
        Assert.Equal(ExpectedVisibleIds(isAdminMode: false), dialog.VisibleIds);
        Assert.Empty(dispatcher.Requests);
    }

    // DLG-13
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void Technical_caption_flag_is_independent_of_visibility(bool isAdminMode, bool showTechnicalDetails)
    {
        using var viewModel = new EquipmentCardParametersDialogViewModel(
            MixedVisibilityCard(), null, new CapturingDispatcher(),
            showTechnicalDetails: showTechnicalDetails, isAdminMode: isAdminMode);

        Assert.Equal(ExpectedVisibleIds(isAdminMode), viewModel.Parameters.Select(row => row.SignalId));
        Assert.All(viewModel.Parameters, row =>
        {
            Assert.Equal(showTechnicalDetails, row.ShowTechnicalDetails);
            Assert.Equal($"{row.SignalId} • UInt16", row.SignalCaption);
        });
    }

    [Fact]
    public void Constructor_uses_latest_good_values_for_readable_parameters()
    {
        var now = DateTimeOffset.Now;
        var card = Card(
            Parameter("Текущий вес", "card.weight", SignalBindingDirection.Read, SignalValueType.UInt16),
            Parameter("Команда", "card.command", SignalBindingDirection.Write, SignalValueType.Bool));
        var signals = new Dictionary<string, SignalValue>
        {
            ["card.weight"] = new("card.weight", (ushort)42, SignalValueType.UInt16, now, IsQualityGood: true, IsStale: false),
            ["card.command"] = new("card.command", true, SignalValueType.Bool, now, IsQualityGood: true, IsStale: false),
        };

        var viewModel = new EquipmentCardParametersDialogViewModel(card, signals, new CapturingDispatcher());

        Assert.Equal("42", viewModel.Parameters[0].ValueText);
        Assert.True(viewModel.Parameters[0].IsReadOnly);
        Assert.Equal(string.Empty, viewModel.Parameters[1].ValueText);
        Assert.True(viewModel.Parameters[1].IsBool);
        Assert.False(viewModel.Parameters[1].BoolValue);
        Assert.False(viewModel.Parameters[1].IsReadOnly);
    }

    // DLG-12
    [Fact]
    public async Task Save_sends_only_editable_parameters_and_does_not_close_dialog()
    {
        var dispatcher = new CapturingDispatcher();
        var card = Card(
            Parameter("Read", "card.read", SignalBindingDirection.Read, SignalValueType.UInt16),
            Parameter("Bool", "card.bool", SignalBindingDirection.Write, SignalValueType.Bool),
            Parameter("Int16", "card.int16", SignalBindingDirection.Write, SignalValueType.Int16),
            Parameter("UInt16", "card.uint16", SignalBindingDirection.Write, SignalValueType.UInt16),
            Parameter("Word", "card.word", SignalBindingDirection.Write, SignalValueType.Word),
            Parameter("Int32", "card.int32", SignalBindingDirection.Write, SignalValueType.Int32),
            Parameter("Dword", "card.dword", SignalBindingDirection.Write, SignalValueType.Dword),
            Parameter("Float32", "card.float32", SignalBindingDirection.Write, SignalValueType.Float32),
            Parameter("String", "card.string", SignalBindingDirection.ReadWrite, SignalValueType.String),
            Parameter("Date", "card.date", SignalBindingDirection.Write, SignalValueType.Date));
        using var viewModel = new EquipmentCardParametersDialogViewModel(card, null, dispatcher, isAdminMode: false);
        var wasClosed = false;
        using var subscription = viewModel.Result.Subscribe(_ => wasClosed = true);

        viewModel.Parameters.Single(x => x.SignalId == "card.bool").BoolValue = true;
        viewModel.Parameters.Single(x => x.SignalId == "card.int16").ValueText = "-12";
        viewModel.Parameters.Single(x => x.SignalId == "card.uint16").ValueText = "12";
        viewModel.Parameters.Single(x => x.SignalId == "card.word").ValueText = "65535";
        viewModel.Parameters.Single(x => x.SignalId == "card.int32").ValueText = "123456";
        viewModel.Parameters.Single(x => x.SignalId == "card.dword").ValueText = "4294967295";
        viewModel.Parameters.Single(x => x.SignalId == "card.float32").ValueText = "1.5";
        viewModel.Parameters.Single(x => x.SignalId == "card.string").ValueText = "текст";
        viewModel.Parameters.Single(x => x.SignalId == "card.date").ValueText = "2026-06-30 12:34:56";

        Assert.True(viewModel.Parameters.Single(row => row.SignalId == "card.bool").IsBool);
        Assert.All(viewModel.Parameters.Where(row => row.SignalId != "card.bool"), row => Assert.True(row.UsesTextInput));
        await viewModel.SaveCommand.Execute().FirstAsync();

        Assert.False(wasClosed);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("card.bool", true, SignalValueType.Bool), (request.SignalId, request.Value, request.ValueType)),
            request => Assert.Equal(("card.int16", (short)-12, SignalValueType.Int16), (request.SignalId, request.Value, request.ValueType)),
            request => Assert.Equal(("card.uint16", (ushort)12, SignalValueType.UInt16), (request.SignalId, request.Value, request.ValueType)),
            request => Assert.Equal(("card.word", (ushort)65535, SignalValueType.Word), (request.SignalId, request.Value, request.ValueType)),
            request => Assert.Equal(("card.int32", 123456, SignalValueType.Int32), (request.SignalId, request.Value, request.ValueType)),
            request => Assert.Equal(("card.dword", uint.MaxValue, SignalValueType.Dword), (request.SignalId, request.Value, request.ValueType)),
            request => Assert.Equal(("card.float32", 1.5f, SignalValueType.Float32), (request.SignalId, request.Value, request.ValueType)),
            request => Assert.Equal(("card.string", "текст", SignalValueType.String), (request.SignalId, request.Value, request.ValueType)),
            request =>
            {
                Assert.Equal("card.date", request.SignalId);
                Assert.Equal(SignalValueType.Date, request.ValueType);
                Assert.IsType<DateTime>(request.Value);
            });
    }

    // DLG-12
    [Fact]
    public async Task Save_marks_invalid_numeric_values_without_dispatching()
    {
        var dispatcher = new CapturingDispatcher();
        var card = Card(
            Parameter("Int16", "card.int16", SignalBindingDirection.Write, SignalValueType.Int16),
            Parameter("UInt16", "card.uint16", SignalBindingDirection.Write, SignalValueType.UInt16),
            Parameter("Int32", "card.int32", SignalBindingDirection.Write, SignalValueType.Int32),
            Parameter("Dword", "card.dword", SignalBindingDirection.Write, SignalValueType.Dword),
            Parameter("Float32", "card.float32", SignalBindingDirection.Write, SignalValueType.Float32),
            Parameter("Date", "card.date", SignalBindingDirection.Write, SignalValueType.Date));
        using var viewModel = new EquipmentCardParametersDialogViewModel(card, null, dispatcher, isAdminMode: false);
        viewModel.Parameters.Single(x => x.SignalId == "card.int16").ValueText = "40000";
        viewModel.Parameters.Single(x => x.SignalId == "card.uint16").ValueText = "-1";
        viewModel.Parameters.Single(x => x.SignalId == "card.int32").ValueText = "hello";
        viewModel.Parameters.Single(x => x.SignalId == "card.dword").ValueText = "-1";
        viewModel.Parameters.Single(x => x.SignalId == "card.float32").ValueText = "NaN";
        viewModel.Parameters.Single(x => x.SignalId == "card.date").ValueText = "not-a-date";

        await viewModel.SaveCommand.Execute().FirstAsync();

        Assert.True(viewModel.HasError);
        Assert.All(viewModel.Parameters, parameter => Assert.True(parameter.HasValidationError));
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public async Task Save_allows_empty_string_value()
    {
        var dispatcher = new CapturingDispatcher();
        var card = Card(Parameter("String", "card.string", SignalBindingDirection.Write, SignalValueType.String));
        var viewModel = new EquipmentCardParametersDialogViewModel(card, null, dispatcher);
        viewModel.Parameters[0].ValueText = string.Empty;

        await viewModel.SaveCommand.Execute().FirstAsync();

        Assert.Null(viewModel.ErrorMessage);
        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(("card.string", string.Empty, SignalValueType.String), (request.SignalId, request.Value, request.ValueType));
    }

    [Fact]
    public async Task Save_uses_preflight_validation_before_dispatching()
    {
        var dispatcher = new CapturingDispatcher();
        var card = Card(Parameter("Bool", "card.bool", SignalBindingDirection.Write, SignalValueType.Bool));
        var validator = new StaticWriteValidator("SignalId card.bool не настроен во вкладке SignalId ↔ Modbus.");
        var viewModel = new EquipmentCardParametersDialogViewModel(card, null, dispatcher, validator);
        viewModel.Parameters[0].BoolValue = true;

        await viewModel.SaveCommand.Execute().FirstAsync();

        Assert.Equal("Исправьте значения параметров.", viewModel.ErrorMessage);
        Assert.Equal("SignalId card.bool не настроен во вкладке SignalId ↔ Modbus.", viewModel.Parameters[0].ValidationMessage);
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public async Task Online_save_records_write_error_only_for_the_visible_parameter()
    {
        const string error = "Тайм-аут записи";
        var dispatcher = new CapturingDispatcher(new TimeoutException(error));
        var store = new RecordingValueStore();
        var mixed = MixedVisibilityCard();
        var card = Card(mixed.Parameters[0], mixed.Parameters[1]);
        using var viewModel = new EquipmentCardParametersDialogViewModel(
            card, null, dispatcher, valueStore: store, isAdminMode: false);
        var row = Assert.Single(viewModel.Parameters);
        row.ValueText = "11";

        await viewModel.SaveCommand.Execute().FirstAsync();

        Assert.Equal(row.SignalId, Assert.Single(dispatcher.Requests).SignalId);
        Assert.Equal(new EquipmentParameterSetpoint(row.SignalId, "11"), Assert.Single(store.Saved));
        Assert.Equal((card.Id, row.SignalId, error), Assert.Single(store.Completions));
        Assert.Contains(error, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Save_offline_persists_editable_values_without_mapping_or_dispatching()
    {
        var dispatcher = new CapturingDispatcher();
        var store = new RecordingValueStore();
        var card = Card(
            Parameter("Read", "card.read", SignalBindingDirection.Read, SignalValueType.Word),
            Parameter("Speed", "card.speed", SignalBindingDirection.ReadWrite, SignalValueType.Float32),
            Parameter("Enabled", "card.enabled", SignalBindingDirection.Write, SignalValueType.Bool));
        var viewModel = new EquipmentCardParametersDialogViewModel(
            card,
            null,
            dispatcher,
            new StaticWriteValidator("Нет Modbus DataMap"),
            isConnectionAvailable: false,
            valueStore: store);
        viewModel.Parameters.Single(parameter => parameter.SignalId == "card.speed").ValueText = "1.5";
        viewModel.Parameters.Single(parameter => parameter.SignalId == "card.enabled").BoolValue = true;

        await viewModel.SaveCommand.Execute().FirstAsync();

        Assert.Empty(dispatcher.Requests);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal("Сохранено локально. Отправка будет выполнена после подключения.", viewModel.StatusMessage);
        Assert.True(store.LastPendingAutoDispatch);
        Assert.Collection(store.Saved,
            value => Assert.Equal(("card.speed", "1.5"), (value.SignalId, value.Value)),
            value => Assert.Equal(("card.enabled", "true"), (value.SignalId, value.Value)));
    }

    [Fact]
    public async Task Save_invalid_offline_batch_does_not_persist_anything()
    {
        var dispatcher = new CapturingDispatcher();
        var store = new RecordingValueStore();
        var card = Card(
            Parameter("Correct", "card.correct", SignalBindingDirection.Write, SignalValueType.Word),
            Parameter("Invalid", "card.invalid", SignalBindingDirection.Write, SignalValueType.Int16));
        var viewModel = new EquipmentCardParametersDialogViewModel(
            card,
            null,
            dispatcher,
            isConnectionAvailable: false,
            valueStore: store);
        viewModel.Parameters[0].ValueText = "15";
        viewModel.Parameters[1].ValueText = "40000";

        await viewModel.SaveCommand.Execute().FirstAsync();

        Assert.True(viewModel.HasError);
        Assert.Empty(store.Saved);
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public void Constructor_prefers_saved_editable_setpoints_and_keeps_read_runtime_value()
    {
        var now = DateTimeOffset.Now;
        var card = Card(
            Parameter("Read", "card.read", SignalBindingDirection.Read, SignalValueType.Word, savedValue: "999"),
            Parameter("Bool", "card.bool", SignalBindingDirection.Write, SignalValueType.Bool, savedValue: "true"),
            Parameter("String", "card.string", SignalBindingDirection.ReadWrite, SignalValueType.String, savedValue: "локально"),
            Parameter("Date", "card.date", SignalBindingDirection.Write, SignalValueType.Date, savedValue: "2026-06-30T12:34:56.0000000+03:00", error: "Не удалось отправить"));
        var signals = new Dictionary<string, SignalValue>
        {
            ["card.read"] = new("card.read", (ushort)42, SignalValueType.Word, now, true, false),
        };

        var viewModel = new EquipmentCardParametersDialogViewModel(card, signals, new CapturingDispatcher());

        Assert.Equal("42", viewModel.Parameters.Single(parameter => parameter.SignalId == "card.read").ValueText);
        Assert.True(viewModel.Parameters.Single(parameter => parameter.SignalId == "card.bool").BoolValue);
        Assert.Equal("локально", viewModel.Parameters.Single(parameter => parameter.SignalId == "card.string").ValueText);
        var date = viewModel.Parameters.Single(parameter => parameter.SignalId == "card.date");
        Assert.Equal("2026-06-30T12:34:56.0000000+03:00", date.ValueText);
        Assert.True(date.HasDispatchError);
        Assert.Equal("Не удалось отправить", date.DispatchError);
    }

    [Fact]
    public async Task Auto_dispatch_sends_pending_admin_value_once_and_records_dispatch_result()
    {
        var runtime = new RuntimeSource(RouteMapSignalSource.Modbus);
        var dispatcher = new CapturingDispatcher();
        var store = new RecordingValueStore();
        var parameter = Parameter(
            "Speed",
            "card.speed",
            SignalBindingDirection.Write,
            SignalValueType.Float32,
            savedValue: "1.5",
            pending: true) with { Visibility = EquipmentParameterVisibility.Admin };
        store.Pending.Add(new PendingEquipmentParameter("card", parameter));
        var autoDispatcher = new EquipmentParameterAutoDispatcher(store, dispatcher, new StaticWriteValidator(null), runtime);

        await autoDispatcher.DispatchPendingAsync();
        await autoDispatcher.DispatchPendingAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(("card.speed", 1.5f, SignalValueType.Float32), (request.SignalId, request.Value, request.ValueType));
        var completion = Assert.Single(store.Completions);
        Assert.Null(completion.ErrorMessage);
        Assert.Empty(store.GetPending());
    }

    [Fact]
    public async Task Auto_dispatch_error_is_saved_and_not_retried()
    {
        var runtime = new RuntimeSource(RouteMapSignalSource.Modbus);
        var dispatcher = new CapturingDispatcher(new InvalidOperationException("PLC недоступен"));
        var store = new RecordingValueStore();
        var parameter = Parameter(
            "Enabled",
            "card.enabled",
            SignalBindingDirection.Write,
            SignalValueType.Bool,
            savedValue: "true",
            pending: true);
        store.Pending.Add(new PendingEquipmentParameter("card", parameter));
        var autoDispatcher = new EquipmentParameterAutoDispatcher(store, dispatcher, new StaticWriteValidator(null), runtime);

        await autoDispatcher.DispatchPendingAsync();
        await autoDispatcher.DispatchPendingAsync();

        Assert.Single(dispatcher.Requests);
        var completion = Assert.Single(store.Completions);
        Assert.Equal("PLC недоступен", completion.ErrorMessage);
        Assert.Empty(store.GetPending());
    }

    [Fact]
    public void WriteValidator_skips_datamap_for_mock_source()
    {
        var validator = new EquipmentParameterWriteValidator(
            new RuntimeSource(RouteMapSignalSource.Mock),
            new StaticOptionsMonitor<ModbusOptions>(new ModbusOptions()));

        var result = validator.Validate(new SignalWriteRequest("missing.bool", true, SignalValueType.Bool));

        Assert.Null(result);
    }

    [Fact]
    public void WriteValidator_reports_modbus_mapping_errors()
    {
        var options = new ModbusOptions
        {
            DataMap =
            [
                Point("read.only", ModbusDataAccess.Read, ModbusValueType.Bool),
                Point("wrong.type", ModbusDataAccess.ReadWrite, ModbusValueType.UInt16),
                Point("ok.bool", ModbusDataAccess.ReadWrite, ModbusValueType.Bool),
                Point("ok.word", ModbusDataAccess.ReadWrite, ModbusValueType.Word),
                Point("ok.dword", ModbusDataAccess.ReadWrite, ModbusValueType.Dword),
                Point("ok.date", ModbusDataAccess.ReadWrite, ModbusValueType.Date),
                Point("ok.string", ModbusDataAccess.ReadWrite, ModbusValueType.String, length: 3),
                Point("short.string", ModbusDataAccess.ReadWrite, ModbusValueType.String, length: 1)
            ]
        };
        var validator = new EquipmentParameterWriteValidator(
            new RuntimeSource(RouteMapSignalSource.Modbus),
            new StaticOptionsMonitor<ModbusOptions>(options));

        Assert.Contains("не настроен", validator.Validate(new SignalWriteRequest("missing.bool", true, SignalValueType.Bool)));
        Assert.Contains("только для чтения", validator.Validate(new SignalWriteRequest("read.only", true, SignalValueType.Bool)));
        Assert.Contains("не совместим", validator.Validate(new SignalWriteRequest("wrong.type", true, SignalValueType.Bool)));
        Assert.Null(validator.Validate(new SignalWriteRequest("ok.bool", true, SignalValueType.Bool)));
        Assert.Null(validator.Validate(new SignalWriteRequest("ok.word", (ushort)1, SignalValueType.Word)));
        Assert.Null(validator.Validate(new SignalWriteRequest("ok.dword", 1u, SignalValueType.Dword)));
        Assert.Null(validator.Validate(new SignalWriteRequest("ok.date", DateTime.Now, SignalValueType.Date)));
        Assert.Null(validator.Validate(new SignalWriteRequest("ok.string", "hello", SignalValueType.String)));
        Assert.Contains("Length", validator.Validate(new SignalWriteRequest("short.string", "hello", SignalValueType.String)));
    }

    private static EquipmentCommandCard MixedVisibilityCard(string adminSavedValue = "20") =>
        Card(
            Parameter("Первый", "parameter.user.1", SignalBindingDirection.ReadWrite, SignalValueType.UInt16, savedValue: "10"),
            Parameter("Скрытый", "parameter.admin.1", SignalBindingDirection.ReadWrite, SignalValueType.UInt16,
                savedValue: adminSavedValue, pending: true, error: "Предыдущая ошибка отправки")
                with { Visibility = EquipmentParameterVisibility.Admin },
            Parameter("Третий", "parameter.user.2", SignalBindingDirection.ReadWrite, SignalValueType.UInt16, savedValue: "30"));

    private static string[] ExpectedVisibleIds(bool isAdminMode) => isAdminMode
        ? ["parameter.user.1", "parameter.admin.1", "parameter.user.2"]
        : ["parameter.user.1", "parameter.user.2"];

    private static EquipmentCommandCard Card(params EquipmentCardParameter[] parameters) =>
        new(
            "card",
            "Карточка",
            "Выключено",
            RouteObjectState.Idle,
            CanStart: true,
            CanStop: true,
            Bindings: [])
        {
            Parameters = parameters,
        };

    private static EquipmentCardParameter Parameter(
        string title,
        string signalId,
        SignalBindingDirection direction,
        SignalValueType valueType,
        string? savedValue = null,
        bool pending = false,
        string? error = null) =>
        new(
            title,
            new SignalBinding(SignalBindingRole.EquipmentParameter, signalId, direction, valueType),
            savedValue,
            pending,
            error);

    private static ModbusDataPointOptions Point(
        string name,
        ModbusDataAccess access,
        ModbusValueType type,
        int length = 1) =>
        new()
        {
            Name = name,
            Access = access,
            Type = type,
            Length = length
        };

    private sealed class CapturingDispatcher(Exception? exception = null) : IEquipmentCommandDispatcher
    {
        public List<SignalWriteRequest> Requests { get; } = [];

        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (exception is not null)
                throw exception;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingValueStore : IEquipmentParameterValueStore
    {
        public List<EquipmentParameterSetpoint> Saved { get; } = [];
        public List<PendingEquipmentParameter> Pending { get; } = [];
        public List<(string CardId, string SignalId, string? ErrorMessage)> Completions { get; } = [];
        public bool LastPendingAutoDispatch { get; private set; }

        public Task SaveAsync(
            string cardId,
            IReadOnlyCollection<EquipmentParameterSetpoint> values,
            bool pendingAutoDispatch,
            CancellationToken cancellationToken = default)
        {
            Saved.AddRange(values);
            LastPendingAutoDispatch = pendingAutoDispatch;
            return Task.CompletedTask;
        }

        public IReadOnlyList<PendingEquipmentParameter> GetPending() => Pending.ToArray();

        public Task CompleteDispatchAsync(
            string cardId,
            string signalId,
            string expectedSavedValue,
            string? errorMessage,
            CancellationToken cancellationToken = default)
        {
            Completions.Add((cardId, signalId, errorMessage));
            Pending.RemoveAll(item =>
                item.CardId == cardId &&
                item.Parameter.Binding.SignalId == signalId &&
                item.Parameter.SavedValue == expectedSavedValue);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticWriteValidator(string? message) : IEquipmentParameterWriteValidator
    {
        public string? Validate(SignalWriteRequest request) => message;
    }

    private sealed class RecordingWriteValidator : IEquipmentParameterWriteValidator
    {
        public List<string> SignalIds { get; } = [];

        public string? Validate(SignalWriteRequest request)
        {
            SignalIds.Add(request.SignalId);
            return null;
        }
    }

    private sealed class UserParametersDialogStub(IEquipmentCommandDispatcher dispatcher) : IEquipmentCardParametersDialogService
    {
        public List<(EquipmentCommandCard Card, bool IsConnectionAvailable)> Opened { get; } = [];
        public string[] VisibleIds { get; private set; } = [];

        public Task ShowAsync(
            EquipmentCommandCard card,
            IReadOnlyDictionary<string, SignalValue>? signals,
            bool isConnectionAvailable = true,
            CancellationToken cancellationToken = default)
        {
            Opened.Add((card, isConnectionAvailable));
            using var viewModel = new EquipmentCardParametersDialogViewModel(
                card, signals, dispatcher, isConnectionAvailable: isConnectionAvailable, isAdminMode: false);
            VisibleIds = viewModel.Parameters.Select(row => row.SignalId).ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class RuntimeSource(RouteMapSignalSource source) : IRouteMapSignalRuntime
    {
        public RouteMapSignalSource CurrentSource { get; private set; } = source;

        public event EventHandler<RouteMapSignalSourceChangedEventArgs>? SourceChanged;

        public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() =>
            Observable.Empty<IReadOnlyDictionary<string, SignalValue>>();

        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void SwitchSource(RouteMapSignalSource source)
        {
            var previous = CurrentSource;
            CurrentSource = source;
            SourceChanged?.Invoke(this, new RouteMapSignalSourceChangedEventArgs(previous, source));
        }

        public void Dispose()
        {
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
