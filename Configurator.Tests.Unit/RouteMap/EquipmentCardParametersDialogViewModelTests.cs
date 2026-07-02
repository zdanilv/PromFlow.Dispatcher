using System.Reactive.Linq;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class EquipmentCardParametersDialogViewModelTests
{
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
        var viewModel = new EquipmentCardParametersDialogViewModel(card, null, dispatcher);
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
        var viewModel = new EquipmentCardParametersDialogViewModel(card, null, dispatcher);
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
        SignalValueType valueType) =>
        new(
            title,
            new SignalBinding(SignalBindingRole.EquipmentParameter, signalId, direction, valueType));

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

    private sealed class CapturingDispatcher : IEquipmentCommandDispatcher
    {
        public List<SignalWriteRequest> Requests { get; } = [];

        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticWriteValidator(string? message) : IEquipmentParameterWriteValidator
    {
        public string? Validate(SignalWriteRequest request) => message;
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
