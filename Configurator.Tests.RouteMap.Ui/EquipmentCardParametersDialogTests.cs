using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Dialogs;
using Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Panels;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using DialogHostAvalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed class EquipmentCardParametersDialogTests
{
    private const string BucketId = "equip.bucket";
    private const string OtherCardId = "equip.other";
    private const string UserSignalId = "test.parameter.user.1";
    private const string AdminSignalId = "equip.bucket.parameter2";
    private const string ReadSignalId = "test.parameter.user.2";
    private const string NewSignalId = "test.parameter.new";
    private const string NewTitle = "Новая настройка";
    private const string EmptyMessage = "Для текущего режима нет доступных настроек оборудования.";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    // UI-07, UI-08
    [AvaloniaTheory]
    [InlineData(ApplicationOptions.AdminWorkMode, true)]
    [InlineData(ApplicationOptions.UserWorkMode, false)]
    public async Task Work_mode_controls_rows_and_technical_captions(string workMode, bool isAdminMode)
    {
        using var fixture = new DialogFixture(workMode);
        var definition = fixture.Manager.CurrentDefinition;
        var document = fixture.Manager.CurrentDocument;
        var originalJson = File.ReadAllText(fixture.Storage.ActiveFilePath);
        var dialog = fixture.Open();

        var texts = VisibleTexts(dialog);
        Assert.Contains("Кюбель Л.К.", texts);
        Assert.Contains("Настройки оборудования", texts);
        Assert.Contains("Скорость", texts);
        Assert.Equal(isAdminMode, texts.Contains("Фильтер"));
        Assert.Equal(isAdminMode, texts.Contains($"{UserSignalId} • UInt16"));
        Assert.Equal(isAdminMode, texts.Contains($"{AdminSignalId} • UInt16"));
        Assert.Equal(isAdminMode, FindButton(dialog, "Добавить").IsEffectivelyVisible);
        Assert.False(dialog.FindControl<Border>("NewParameterForm")!.IsEffectivelyVisible);
        Assert.DoesNotContain("Новый параметр", texts);
        Assert.Equal(isAdminMode ? 3 : 2, ParameterList(dialog).Items.Count);
        Assert.True(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        fixture.CaptureFrame($"equipment-settings-{workMode}");

        await fixture.CloseAsync(dialog);
        Assert.Same(definition, fixture.Manager.CurrentDefinition);
        Assert.Same(document, fixture.Manager.CurrentDocument);
        Assert.Equal(originalJson, File.ReadAllText(fixture.Storage.ActiveFilePath));
        Assert.Empty(fixture.Dispatcher.Requests);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    public async Task Admin_can_open_and_cancel_the_form_with_keyboard_in_a_scrollable_dialog(int parameterCount)
    {
        using var fixture = new DialogFixture(ApplicationOptions.AdminWorkMode, parameterCount: parameterCount);
        var originalJson = fixture.ReadJson();
        var dialog = fixture.Open();
        var vm = ViewModel(dialog);
        var add = FindButton(dialog, "Добавить");
        var title = dialog.FindControl<TextBox>("NewParameterTitleBox")!;
        var form = dialog.FindControl<Border>("NewParameterForm")!;
        var scroll = dialog.FindControl<ScrollViewer>("ParametersScrollViewer")!;
        Assert.Equal(parameterCount == 0, VisibleTexts(dialog).Contains(EmptyMessage));
        Assert.Equal(parameterCount > 0, VisibleTexts(dialog).Contains("Скорость"));
        Assert.True(add.IsEffectivelyVisible);
        Assert.True(add.IsEffectivelyEnabled);
        Assert.False(form.IsEffectivelyVisible);
        Assert.Equal(parameterCount > 0, FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        add.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Assert.True(add.Focus());
        fixture.PressSpace();

        Assert.True(form.IsEffectivelyVisible);
        Assert.True(title.IsFocused);
        Assert.False(add.IsEffectivelyEnabled);
        Assert.False(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        Assert.All(form.GetVisualDescendants().OfType<InputElement>()
            .Where(field => field is TextBox or ComboBox), field => Assert.True(field.IsEffectivelyEnabled));
        SetDraft(dialog, NewTitle, NewSignalId);
        await vm.AddCommand.Execute().ToTask().WaitAsync(TestTimeout);
        Assert.Equal(NewSignalId, vm.NewParameterSignalId);
        var labels = VisibleTexts(form);
        Assert.All(new[] { "Новый параметр", "Название", "SignalId", "Направление", "Тип значения", "Отображать для" },
            label => Assert.Contains(label, labels));
        if (parameterCount != 1)
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        Assert.Equal(scroll.Extent.Width, scroll.Viewport.Width, precision: 1);
        fixture.CaptureFrame($"equipment-settings-add-{parameterCount}-rows");
        var cancel = FindButton(dialog, "Отмена");
        cancel.BringIntoView();
        Assert.True(cancel.Focus());
        if (parameterCount > 1)
            fixture.CaptureFrame("equipment-settings-add-actions");
        fixture.PressSpace();
        Assert.False(form.IsEffectivelyVisible);
        Assert.True(add.IsEffectivelyEnabled);
        Assert.Equal(parameterCount > 0, FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        Assert.Null(vm.NewParameterTitle);
        Assert.Null(vm.NewParameterSignalId);
        Assert.Equal(parameterCount, vm.Parameters.Count);
        Assert.Equal(parameterCount, fixture.Manager.CurrentDocument.Cards.Single().Parameters.Count);
        Assert.Equal(originalJson, fixture.ReadJson());
        Assert.Empty(fixture.Store.Additions);
        Assert.Empty(fixture.Dispatcher.Requests);
        await fixture.CloseAsync(dialog);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admin_creates_bool_inline_and_can_save_explicit_false_after_reopening(bool offline)
    {
        using var fixture = new DialogFixture(ApplicationOptions.AdminWorkMode, offline: offline, parameterCount: 0);
        var dialog = fixture.Open();
        var viewModel = ViewModel(dialog);
        Assert.True(viewModel.HasNoParameters);
        Assert.Contains(EmptyMessage, VisibleTexts(dialog));
        Assert.False(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        fixture.Click(FindButton(dialog, "Добавить"));
        var form = dialog.FindControl<Border>("NewParameterForm")!;
        var fields = form.GetVisualDescendants().OfType<ComboBox>().ToArray();
        Assert.Equal(SignalBindingDirection.ReadWrite, fields[0].SelectedItem);
        Assert.Equal(SignalValueType.Word, fields[1].SelectedItem);
        Assert.Equal(EquipmentParameterVisibility.User, fields[2].SelectedValue);
        fields[0].SetCurrentValue(SelectingItemsControl.SelectedItemProperty, SignalBindingDirection.Write);
        fields[1].SetCurrentValue(SelectingItemsControl.SelectedItemProperty, SignalValueType.Bool);
        fields[2].SetCurrentValue(SelectingItemsControl.SelectedIndexProperty, 1);
        Assert.False(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);

        var create = FindButton(dialog, "Создать");
        fixture.Click(create);
        Assert.Contains("Название настройки оборудования обязательно.", VisibleTexts(dialog));
        var error = dialog.FindControl<TextBlock>("CreateErrorText")!;
        var scroll = dialog.FindControl<ScrollViewer>("ParametersScrollViewer")!;
        Assert.True(error.TranslatePoint(default, dialog)!.Value.Y >=
            scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), dialog)!.Value.Y);
        Assert.True(form.IsEffectivelyVisible);
        dialog.FindControl<TextBox>("NewParameterTitleBox")!.SetCurrentValue(TextBox.TextProperty, "Разрешение");
        dialog.FindControl<TextBox>("NewParameterSignalIdBox")!.SetCurrentValue(TextBox.TextProperty, "test.parameter.bool");
        var created = viewModel.CreateCommand.FirstAsync().ToTask();
        fixture.Click(create);
        await created.WaitAsync(TestTimeout);
        Dispatcher.UIThread.RunJobs();
        Assert.False(form.IsEffectivelyVisible);
        Assert.Contains("Настройка добавлена", VisibleTexts(dialog));
        Assert.Contains("Разрешение", VisibleTexts(dialog));
        Assert.DoesNotContain(EmptyMessage, VisibleTexts(dialog));
        Assert.False(viewModel.HasNoParameters);
        Assert.Equal("test.parameter.bool", Assert.Single(viewModel.Parameters).SignalId);
        Assert.True(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        var persisted = Assert.Single(fixture.Storage.LoadActive()!.Cards.Single().Parameters);
        Assert.Equal((SignalBindingRole.EquipmentParameter, SignalBindingDirection.Write, SignalValueType.Bool,
            EquipmentParameterVisibility.Admin, null, false, null),
            (persisted.Role, persisted.Direction, persisted.ValueType, persisted.Visibility,
                persisted.SavedValue, persisted.PendingAutoDispatch, persisted.LastDispatchError));
        Assert.Empty(fixture.Store.Saves);
        Assert.Empty(fixture.Store.GetPending());
        Assert.Empty(fixture.Dispatcher.Requests);
        await SaveOperatorDialogAsync(dialog);
        Assert.Empty(fixture.Store.Saves);
        Assert.Empty(fixture.Dispatcher.Requests);
        await fixture.CloseAsync(dialog);

        dialog = fixture.Open(ApplicationOptions.UserWorkMode);
        Assert.Empty(Assert.IsType<EquipmentCardParametersDialogViewModel>(dialog.DataContext).Parameters);
        Assert.False(FindButton(dialog, "Добавить").IsEffectivelyVisible);
        await fixture.CloseAsync(dialog);

        dialog = fixture.Open();
        var boolChoice = Assert.Single(dialog.GetVisualDescendants().OfType<ComboBox>(),
            box => box.DataContext is EquipmentCardParameterRow && box.IsEffectivelyVisible);
        Assert.Equal(-1, boolChoice.SelectedIndex);
        await SaveOperatorDialogAsync(dialog);
        Assert.Empty(fixture.Store.Saves);
        boolChoice.SetCurrentValue(SelectingItemsControl.SelectedIndexProperty, 0);
        await SaveOperatorDialogAsync(dialog);
        var save = Assert.Single(fixture.Store.Saves);
        Assert.Equal(offline, save.Pending);
        Assert.Equal(new EquipmentParameterSetpoint("test.parameter.bool", "false"), Assert.Single(save.Values));
        var saved = fixture.Parameter("test.parameter.bool");
        Assert.Equal(("false", offline, null), (saved.SavedValue, saved.PendingAutoDispatch, saved.LastDispatchError));
        if (offline)
        {
            Assert.Empty(fixture.Dispatcher.Requests);
            Assert.Equal("test.parameter.bool", Assert.Single(fixture.Store.GetPending()).Parameter.Binding.SignalId);
        }
        else
        {
            var request = Assert.Single(fixture.Dispatcher.Requests);
            Assert.Equal(("test.parameter.bool", false, SignalValueType.Bool),
                (request.SignalId, Assert.IsType<bool>(request.Value), request.ValueType));
            Assert.Empty(fixture.Store.GetPending());
        }
        await fixture.CloseAsync(dialog);
    }

    [AvaloniaTheory]
    [InlineData(SignalBindingDirection.Read, SignalValueType.UInt16, false)]
    [InlineData(SignalBindingDirection.ReadWrite, SignalValueType.Word, true)]
    [InlineData(SignalBindingDirection.ReadWrite, SignalValueType.Bool, false)]
    [InlineData(SignalBindingDirection.ReadWrite, SignalValueType.String, false)]
    [InlineData(SignalBindingDirection.ReadWrite, SignalValueType.Float32, false)]
    [InlineData(SignalBindingDirection.ReadWrite, SignalValueType.Int16, false)]
    [InlineData(SignalBindingDirection.ReadWrite, SignalValueType.Int32, false)]
    [InlineData(SignalBindingDirection.ReadWrite, SignalValueType.Dword, false)]
    [InlineData(SignalBindingDirection.ReadWrite, SignalValueType.Date, false)]
    public async Task Creation_keeps_existing_parameters_and_updates_json_runtime_and_open_dialog(
        SignalBindingDirection direction, SignalValueType type, bool offline)
    {
        using var fixture = new DialogFixture(ApplicationOptions.AdminWorkMode, offline: offline, pendingError: null);
        var originalJson = JsonNode.Parse(fixture.ReadJson())!;
        var originalParameters = ParametersInJson(originalJson);
        var originalRuntime = fixture.Manager.CurrentDefinition.MapEquipment.Single().Parameters;
        var pending = Assert.Single(fixture.Store.GetPending());
        Assert.Equal((AdminSignalId, SignalValueType.UInt16, "5000", true, null),
            (pending.Parameter.Binding.SignalId, pending.Parameter.Binding.ValueType, pending.Parameter.SavedValue,
                pending.Parameter.PendingAutoDispatch, pending.Parameter.LastDispatchError));
        var publications = new List<(RouteMapDefinition Definition, RouteMapConfigurationDocument Document, string Json)>();
        using var subscription = fixture.Manager.DefinitionChanges.Skip(1).Subscribe(definition =>
            publications.Add((definition, fixture.Manager.CurrentDocument, fixture.ReadJson())));
        var dialog = fixture.Open();
        fixture.Click(FindButton(dialog, "Добавить"));
        SetDraft(dialog, $" {NewTitle} ", $" {NewSignalId} ", direction, type);
        Assert.Empty(fixture.Store.Additions);
        Assert.Empty(fixture.Dispatcher.Requests);
        Assert.True(JsonNode.DeepEquals(originalJson, JsonNode.Parse(fixture.ReadJson())));

        await ClickCreateAsync(fixture, dialog);

        Assert.Equal((BucketId, new NewEquipmentCardParameterDefinition(
            NewTitle, NewSignalId, direction, type, EquipmentParameterVisibility.User)), Assert.Single(fixture.Store.Additions));
        Assert.Null(ViewModel(dialog).CreateErrorMessage);
        Assert.False(dialog.FindControl<Border>("NewParameterForm")!.IsEffectivelyVisible);
        Assert.Contains(NewTitle, VisibleTexts(dialog));
        Assert.True(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        var publication = Assert.Single(publications);
        Assert.Same(fixture.Manager.CurrentDefinition, publication.Definition);
        Assert.Same(fixture.Manager.CurrentDocument, publication.Document);
        Assert.Equal(fixture.ReadJson(), publication.Json);
        var expected = new EquipmentCardParameter(NewTitle,
            new(SignalBindingRole.EquipmentParameter, NewSignalId, direction, type)) { RequiresExplicitSetpoint = true };
        Assert.Equal(originalRuntime.Append(expected), publication.Definition.MapEquipment.Single().Parameters);
        Assert.Equal(originalRuntime.Select(parameter => parameter.Binding.SignalId).Append(NewSignalId),
            ViewModel(dialog).Parameters.Select(row => row.SignalId));
        Assert.Equal(pending, Assert.Single(fixture.Store.GetPending()));
        Assert.Empty(fixture.Dispatcher.Requests);
        Assert.Empty(fixture.Store.Saves);

        var persistedJson = JsonNode.Parse(publication.Json)!;
        Assert.Equal((int?)originalJson["schemaVersion"], (int?)persistedJson["schemaVersion"]);
        var persistedParameters = ParametersInJson(persistedJson);
        var addedJson = persistedParameters.Last()!.AsObject();
        Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(new
        {
            title = NewTitle,
            signalId = NewSignalId,
            role = "equipmentParameter",
            direction = JsonNamingPolicy.CamelCase.ConvertName(direction.ToString()),
            valueType = JsonNamingPolicy.CamelCase.ConvertName(type.ToString()),
            visibility = "user",
            savedValue = (string?)null,
            pendingAutoDispatch = false,
            lastDispatchError = (string?)null,
            requiresExplicitSetpoint = true,
        }), addedJson));
        persistedParameters.RemoveAt(persistedParameters.Count - 1);
        Assert.Equal(3, originalParameters.Count);
        Assert.True(JsonNode.DeepEquals(originalJson, persistedJson));
        foreach (var document in new[] { publication.Document, fixture.Storage.LoadActive()! })
        {
            var parameter = Assert.Single(document.Cards.Single().Parameters, item => item.SignalId == NewSignalId);
            Assert.Equal((expected.Title, expected.Binding.Role, direction, type, expected.Visibility, null, false, null),
                (parameter.Title, parameter.Role, parameter.Direction, parameter.ValueType, parameter.Visibility,
                    parameter.SavedValue, parameter.PendingAutoDispatch, parameter.LastDispatchError));
        }

        await fixture.CloseAsync(dialog);
        using var reloaded = fixture.CreateManager();
        Assert.Null(reloaded.LastLoadError);
        dialog = fixture.Open(manager: reloaded);
        Assert.Contains(NewTitle, VisibleTexts(dialog));
        Assert.Equal(expected, reloaded.CurrentDefinition.MapEquipment.Single().Parameters[^1]);
        await fixture.CloseAsync(dialog);
    }

    [AvaloniaFact]
    public async Task Closing_with_a_draft_preserves_only_the_created_parameter()
    {
        using var fixture = new DialogFixture(ApplicationOptions.AdminWorkMode, includeOtherCard: true);
        var originalJson = JsonNode.Parse(fixture.ReadJson())!;
        var dialog = fixture.Open(cardId: OtherCardId);
        fixture.Click(FindButton(dialog, "Добавить"));
        SetDraft(dialog, NewTitle, NewSignalId);
        await ClickCreateAsync(fixture, dialog);
        Assert.Equal(OtherCardId, Assert.Single(fixture.Store.Additions).CardId);
        Assert.Equal(NewSignalId, Assert.Single(ViewModel(dialog).Parameters).SignalId);
        var committedJson = fixture.ReadJson();
        var committedDocument = JsonNode.Parse(committedJson)!;
        Assert.True(JsonNode.DeepEquals(ParametersInJson(originalJson), ParametersInJson(committedDocument)));
        Assert.Equal(NewSignalId, (string?)Assert.Single(ParametersInJson(committedDocument, OtherCardId))!["signalId"]);

        fixture.Click(FindButton(dialog, "Добавить"));
        SetDraft(dialog, "Незавершённая настройка", "test.parameter.discard");
        var vm = ViewModel(dialog);
        await fixture.CloseAsync(dialog);
        Assert.Null(vm.NewParameterTitle);
        Assert.Null(vm.NewParameterSignalId);
        Assert.Equal(committedJson, fixture.ReadJson());
        Assert.Empty(fixture.Dispatcher.Requests);

        dialog = fixture.Open(cardId: OtherCardId);
        Assert.Equal(NewSignalId, Assert.Single(ViewModel(dialog).Parameters).SignalId);
        Assert.Contains(NewTitle, VisibleTexts(dialog));
        await fixture.CloseAsync(dialog);
    }

    [AvaloniaTheory]
    [InlineData("", NewSignalId, "Название", 0)]
    [InlineData("   ", NewSignalId, "Название", 0)]
    [InlineData(NewTitle, "", "SignalId", 0)]
    [InlineData(NewTitle, "   ", "SignalId", 0)]
    [InlineData(NewTitle, AdminSignalId, "уже существует", 1)]
    [InlineData(NewTitle, "EQUIP.BUCKET.PARAMETER2", "уже существует", 1)]
    public async Task Invalid_or_duplicate_input_keeps_the_draft_and_can_be_corrected(
        string title, string signalId, string message, int storeCalls)
    {
        using var fixture = new DialogFixture(ApplicationOptions.AdminWorkMode);
        var dialog = fixture.Open();
        var originalJson = fixture.ReadJson();
        var originalDefinition = fixture.Manager.CurrentDefinition;
        var originalDocument = fixture.Manager.CurrentDocument;
        var vm = ViewModel(dialog);
        fixture.Click(FindButton(dialog, "Добавить"));
        SetDraft(dialog, title, signalId);
        await ClickCreateAsync(fixture, dialog);
        Assert.Equal(storeCalls, fixture.Store.Additions.Count);
        AssertDraftError(dialog, title, signalId, message, expectedRows: 3);
        Assert.Equal(originalJson, fixture.ReadJson());
        Assert.Same(originalDefinition, fixture.Manager.CurrentDefinition);
        Assert.Same(originalDocument, fixture.Manager.CurrentDocument);
        if (signalId == "EQUIP.BUCKET.PARAMETER2")
            fixture.CaptureFrame("equipment-settings-duplicate-error");

        SetDraft(dialog, NewTitle, NewSignalId);
        await ClickCreateAsync(fixture, dialog);
        Assert.Null(vm.CreateErrorMessage);
        Assert.Equal(NewSignalId, vm.Parameters[^1].SignalId);
        Assert.Empty(fixture.Dispatcher.Requests);
        await fixture.CloseAsync(dialog);
    }

    [AvaloniaFact]
    public async Task File_write_failure_keeps_the_active_profile_and_form_until_retry()
    {
        using var fixture = new DialogFixture(ApplicationOptions.AdminWorkMode);
        var dialog = fixture.Open();
        var originalJson = fixture.ReadJson();
        var originalDefinition = fixture.Manager.CurrentDefinition;
        var originalDocument = fixture.Manager.CurrentDocument;
        var publications = new List<RouteMapDefinition>();
        using var subscription = fixture.Manager.DefinitionChanges.Skip(1).Subscribe(publications.Add);
        fixture.Click(FindButton(dialog, "Добавить"));
        SetDraft(dialog, NewTitle, NewSignalId);

        // A directory at the temporary file path prevents the real storage write.
        var blockedPath = fixture.Storage.ActiveFilePath + ".tmp";
        Directory.CreateDirectory(blockedPath);
        try
        {
            await ClickCreateAsync(fixture, dialog);
            AssertDraftError(dialog, NewTitle, NewSignalId, "Не удалось сохранить RouteMap", expectedRows: 3);
            Assert.Equal(originalJson, fixture.ReadJson());
            Assert.Equal(3, fixture.Storage.LoadActive()!.Cards.Single().Parameters.Count);
            Assert.Same(originalDefinition, fixture.Manager.CurrentDefinition);
            Assert.Same(originalDocument, fixture.Manager.CurrentDocument);
            Assert.Empty(publications);
        }
        finally
        {
            Directory.Delete(blockedPath);
        }

        await ClickCreateAsync(fixture, dialog);
        Assert.Null(ViewModel(dialog).CreateErrorMessage);
        Assert.Equal(NewSignalId, ViewModel(dialog).Parameters[^1].SignalId);
        Assert.Equal(NewTitle, fixture.Parameter(NewSignalId).Title);
        Assert.Same(fixture.Manager.CurrentDefinition, Assert.Single(publications));
        Assert.Empty(fixture.Dispatcher.Requests);
        await fixture.CloseAsync(dialog);
    }

    [AvaloniaFact]
    public async Task User_cannot_create_through_the_store_with_an_active_profile()
    {
        using var fixture = new DialogFixture(ApplicationOptions.UserWorkMode);
        var originalJson = fixture.ReadJson();
        var originalDocument = fixture.Manager.CurrentDocument;
        var originalDefinition = fixture.Manager.CurrentDefinition;
        var dialog = fixture.Open();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.AddAsync(BucketId, new(NewTitle, NewSignalId, SignalBindingDirection.ReadWrite,
                SignalValueType.Word, EquipmentParameterVisibility.User)));

        Assert.Contains("администратор", error.Message);
        Assert.Equal(originalJson, fixture.ReadJson());
        Assert.Same(originalDocument, fixture.Manager.CurrentDocument);
        Assert.Same(originalDefinition, fixture.Manager.CurrentDefinition);
        Assert.Equal(new[] { UserSignalId, ReadSignalId }, ViewModel(dialog).Parameters.Select(row => row.SignalId));
        Assert.Empty(fixture.Dispatcher.Requests);
        await fixture.CloseAsync(dialog);
    }

    [AvaloniaFact]
    public async Task Unmapped_definition_can_be_created_but_its_value_requires_modbus_mapping()
    {
        var modbus = new ModbusOptions();
        using var fixture = new DialogFixture(ApplicationOptions.AdminWorkMode, parameterCount: 0, modbus: modbus);
        var dialog = fixture.Open();
        fixture.Click(FindButton(dialog, "Добавить"));
        SetDraft(dialog, NewTitle, NewSignalId);
        await ClickCreateAsync(fixture, dialog);
        Assert.Null(ViewModel(dialog).CreateErrorMessage);
        Assert.Empty(modbus.DataMap);
        Assert.Empty(fixture.Dispatcher.Requests);

        var beforeSave = fixture.ReadJson();
        var row = Assert.Single(ViewModel(dialog).Parameters);
        Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(), box => ReferenceEquals(box.DataContext, row))
            .SetCurrentValue(TextBox.TextProperty, "42");
        var save = ViewModel(dialog).SaveCommand.FirstAsync().ToTask();
        InvokeButton(dialog, "Сохранить");
        await save.WaitAsync(TestTimeout);
        Assert.Contains($"SignalId {NewSignalId} не настроен", row.ValidationMessage);
        Assert.Contains(row.ValidationMessage, VisibleTexts(dialog));
        Assert.Equal(beforeSave, fixture.ReadJson());
        Assert.Empty(fixture.Store.Saves);
        Assert.Empty(fixture.Dispatcher.Requests);
        Assert.Empty(modbus.DataMap);
        await fixture.CloseAsync(dialog);
    }

    [AvaloniaFact]
    public async Task Double_create_while_a_setpoint_save_holds_the_gate_preserves_both_changes()
    {
        using var fixture = new DialogFixture(ApplicationOptions.AdminWorkMode, offline: true, pendingError: null);
        var dialog = fixture.Open();
        var vm = ViewModel(dialog);
        fixture.Click(FindButton(dialog, "Добавить"));
        SetDraft(dialog, NewTitle, NewSignalId);
        var saveReachedPublication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseSave = new ManualResetEventSlim();
        using var subscription = fixture.Manager.DefinitionChanges.Skip(1).Take(1).Subscribe(_ =>
        {
            // The manager still holds its write gate while publishing the saved setpoint.
            saveReachedPublication.SetResult();
            if (!releaseSave.Wait(TestTimeout))
                throw new TimeoutException("Setpoint publication did not resume.");
        });
        using var values = new RouteMapEquipmentParameterValueStore(fixture.Manager);
        var save = Task.Run(() => values.SaveAsync(BucketId, [new(AdminSignalId, "6000")], pendingAutoDispatch: true));
        Task? created = null;
        try
        {
            await saveReachedPublication.Task.WaitAsync(TestTimeout);
            created = vm.CreateCommand.FirstAsync().ToTask();
            var create = FindButton(dialog, "Создать");
            fixture.Click(create);
            Assert.True(vm.IsCreatingParameter);
            Assert.False(create.IsEffectivelyEnabled);
            Assert.False(FindButton(dialog, "Закрыть").IsEffectivelyEnabled);
            Assert.False(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
            Assert.Equal(3, vm.Parameters.Count);
            Assert.DoesNotContain(ParametersInJson(JsonNode.Parse(fixture.ReadJson())!),
                parameter => (string?)parameter!["signalId"] == NewSignalId);
            fixture.Click(create);
            Assert.Single(fixture.Store.Additions);
        }
        finally
        {
            releaseSave.Set();
            await save.WaitAsync(TestTimeout);
            if (fixture.Store.Additions.Count != 0 && created is not null)
                await created.WaitAsync(TestTimeout);
        }

        Assert.False(vm.IsAddingParameter);
        Assert.False(vm.IsCreatingParameter);
        Assert.Null(vm.CreateErrorMessage);
        Assert.Single(vm.Parameters, row => row.SignalId == NewSignalId);
        var existing = fixture.Parameter(AdminSignalId);
        var added = fixture.Parameter(NewSignalId);
        Assert.Equal(("6000", true, null), (existing.SavedValue, existing.PendingAutoDispatch, existing.LastDispatchError));
        Assert.Equal((null, false), (added.SavedValue, added.PendingAutoDispatch));
        Assert.Equal(new[] { UserSignalId, AdminSignalId, ReadSignalId, NewSignalId },
            ParametersInJson(JsonNode.Parse(fixture.ReadJson())!).Select(parameter => (string?)parameter!["signalId"]));
        Assert.Equal("6000", Assert.Single(fixture.Store.GetPending()).Parameter.SavedValue);
        Assert.Empty(fixture.Dispatcher.Requests);
        await fixture.CloseAsync(dialog);
    }

    [AvaloniaFact]
    public async Task Creation_from_an_open_dialog_preserves_newer_changes_from_the_editor()
    {
        using var fixture = new DialogFixture(ApplicationOptions.AdminWorkMode, pendingError: null);
        var dialog = fixture.Open();
        fixture.Click(FindButton(dialog, "Добавить"));
        SetDraft(dialog, NewTitle, NewSignalId);

        using var editor = new RouteMapSettingsViewModel(fixture.Manager, fixture.Storage, new NullFilePicker());
        editor.Draft.Cards.Single().Title = "Имя карточки из редактора";
        editor.Draft.Cards.Single().Parameters.Single(item => item.SignalId == ReadSignalId).Title = "Имя параметра из редактора";
        await editor.SaveCommand.Execute().ToTask().WaitAsync(TestTimeout);
        Assert.Null(editor.ErrorMessage);
        var latestJson = JsonNode.Parse(fixture.ReadJson());

        await ClickCreateAsync(fixture, dialog);
        Assert.Null(ViewModel(dialog).CreateErrorMessage);
        var jsonAfterCreation = JsonNode.Parse(fixture.ReadJson())!;
        var rows = ParametersInJson(jsonAfterCreation);
        Assert.Equal(NewSignalId, (string?)rows.Last()!["signalId"]);
        rows.RemoveAt(rows.Count - 1);
        Assert.True(JsonNode.DeepEquals(latestJson, jsonAfterCreation));
        Assert.Contains(NewTitle, VisibleTexts(dialog));
        Assert.Empty(fixture.Dispatcher.Requests);
        await fixture.CloseAsync(dialog);

        dialog = fixture.Open();
        Assert.Contains("Имя карточки из редактора", VisibleTexts(dialog));
        Assert.Contains("Имя параметра из редактора", VisibleTexts(dialog));
        Assert.Contains(NewTitle, VisibleTexts(dialog));
        await fixture.CloseAsync(dialog);
    }

    // UI-09
    [AvaloniaFact]
    public async Task All_admin_parameters_leave_an_empty_dialog_that_opens_and_closes_offline()
    {
        using var fixture = new DialogFixture(ApplicationOptions.UserWorkMode, allAdmin: true, offline: true);
        var originalJson = File.ReadAllText(fixture.Storage.ActiveFilePath);
        var dialog = fixture.Open();

        var texts = VisibleTexts(dialog);
        Assert.Contains("Кюбель Л.К.", texts);
        Assert.Contains("Настройки оборудования", texts);
        Assert.Contains("Для текущего режима нет доступных настроек оборудования.", texts);
        Assert.Empty(ParameterList(dialog).Items);
        Assert.False(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        Assert.True(FindButton(dialog, "Закрыть").IsEffectivelyEnabled);

        await fixture.CloseAsync(dialog);
        var adminDialog = fixture.Open(ApplicationOptions.AdminWorkMode);
        Assert.Equal(new[] { UserSignalId, AdminSignalId, ReadSignalId },
            Assert.IsType<EquipmentCardParametersDialogViewModel>(adminDialog.DataContext).Parameters.Select(row => row.SignalId));
        await fixture.CloseAsync(adminDialog);
        Assert.Equal(originalJson, File.ReadAllText(fixture.Storage.ActiveFilePath));
        Assert.Empty(fixture.Dispatcher.Requests);
        Assert.Empty(fixture.Store.Saves);
    }

    // UI-10
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editor_and_operator_dialogs_preserve_hidden_setpoints_online_and_offline(bool offline)
    {
        using var fixture = new DialogFixture(ApplicationOptions.UserWorkMode, offline: offline, adminInitiallyVisible: true);
        await SetAdminVisibilityInEditorAsync(fixture);
        Assert.Empty(fixture.Dispatcher.Requests);
        Assert.Empty(fixture.Store.Saves);

        var originalParameters = fixture.Manager.CurrentDefinition.MapEquipment.Single().Parameters;
        var originalInventory = RouteMapSignalInventory.Build(fixture.Manager.CurrentDefinition);
        Assert.All(new[] { UserSignalId, AdminSignalId, ReadSignalId },
            id => Assert.Contains(originalInventory, item => item.SignalId == id));
        var adminDialog = fixture.Open(ApplicationOptions.AdminWorkMode);
        var adminViewModel = Assert.IsType<EquipmentCardParametersDialogViewModel>(adminDialog.DataContext);
        Assert.Equal(new[] { UserSignalId, AdminSignalId, ReadSignalId }, adminViewModel.Parameters.Select(row => row.SignalId));
        Assert.True(Assert.Single(adminDialog.GetVisualDescendants().OfType<TextBox>(),
            box => box.DataContext is EquipmentCardParameterRow row && row.SignalId == ReadSignalId).IsReadOnly);
        await fixture.CloseAsync(adminDialog);

        var expectedJson = JsonNode.Parse(File.ReadAllText(fixture.Storage.ActiveFilePath))!;
        var userParameterJson = expectedJson["cards"]!.AsArray().Single()!["parameters"]!.AsArray()
            .Single(parameter => (string?)parameter!["signalId"] == UserSignalId)!;
        userParameterJson["savedValue"] = "42";
        userParameterJson["pendingAutoDispatch"] = offline;
        var dialog = fixture.Open();
        var input = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(),
            box => box.IsEffectivelyVisible && !box.IsReadOnly);
        var viewModel = Assert.IsType<EquipmentCardParametersDialogViewModel>(dialog.DataContext);
        Assert.Equal(new[] { UserSignalId, ReadSignalId }, viewModel.Parameters.Select(row => row.SignalId));
        Assert.Equal("100", input.Text);
        input.SetCurrentValue(TextBox.TextProperty, "42");
        Assert.Equal("42", viewModel.Parameters[0].ValueText);

        await SaveOperatorDialogAsync(dialog);
        var save = Assert.Single(fixture.Store.Saves);
        Assert.Equal(offline, save.Pending);
        Assert.Equal(new EquipmentParameterSetpoint(UserSignalId, "42"), Assert.Single(save.Values));
        if (offline)
            Assert.Empty(fixture.Dispatcher.Requests);
        else
        {
            var request = Assert.Single(fixture.Dispatcher.Requests);
            Assert.Equal((UserSignalId, (ushort)42, SignalValueType.UInt16), (request.SignalId, request.Value, request.ValueType));
        }

        Assert.Equal(new[] { originalParameters[0] with { SavedValue = "42", PendingAutoDispatch = offline },
                originalParameters[1], originalParameters[2] }, fixture.Manager.CurrentDefinition.MapEquipment.Single().Parameters);
        var hidden = Assert.Single(fixture.Manager.CurrentDocument.Cards.Single().Parameters,
            parameter => parameter.SignalId == AdminSignalId);
        Assert.Equal((EquipmentParameterVisibility.Admin, "5000", true, "Предыдущая ошибка"),
            (hidden.Visibility, hidden.SavedValue, hidden.PendingAutoDispatch, hidden.LastDispatchError));
        Assert.Equal(originalParameters[1], Assert.Single(fixture.Store.GetPending(),
            item => item.Parameter.Binding.SignalId == AdminSignalId).Parameter);
        Assert.Equal(originalInventory, RouteMapSignalInventory.Build(fixture.Manager.CurrentDefinition));
        Assert.True(JsonNode.DeepEquals(expectedJson, JsonNode.Parse(File.ReadAllText(fixture.Storage.ActiveFilePath))));

        await fixture.CloseAsync(dialog);
        adminDialog = fixture.Open(ApplicationOptions.AdminWorkMode);
        adminViewModel = Assert.IsType<EquipmentCardParametersDialogViewModel>(adminDialog.DataContext);
        Assert.Equal(new[] { UserSignalId, AdminSignalId, ReadSignalId }, adminViewModel.Parameters.Select(row => row.SignalId));
        Assert.Equal("5000", adminViewModel.Parameters[1].ValueText);
        Assert.Equal("Предыдущая ошибка", adminViewModel.Parameters[1].DispatchError);
        Assert.Equal(offline ? 0 : 1, fixture.Dispatcher.Requests.Count);
        Assert.Single(fixture.Store.Saves);

        adminViewModel.Parameters[1].ValueText = "5001";
        await SaveOperatorDialogAsync(adminDialog);
        Assert.Equal(new[] { new EquipmentParameterSetpoint(UserSignalId, "42"), new EquipmentParameterSetpoint(AdminSignalId, "5001") },
            fixture.Store.Saves[1].Values);
        Assert.Equal(offline ? [] : new[] { UserSignalId, UserSignalId, AdminSignalId },
            fixture.Dispatcher.Requests.Select(request => request.SignalId));
        await fixture.CloseAsync(adminDialog);
    }

    private static async Task SetAdminVisibilityInEditorAsync(DialogFixture fixture)
    {
        using var editor = new RouteMapSettingsViewModel(fixture.Manager, fixture.Storage, new NullFilePicker());
        var view = new RouteMapSettingsDialog { DataContext = editor };
        var window = new Window { Width = 1320, Height = 780, Content = view };
        try
        {
            window.Show();
            Assert.Single(view.GetVisualDescendants().OfType<TabControl>()).SelectedIndex = 5;
            Dispatcher.UIThread.RunJobs();
            var parameters = editor.SelectedCard!.Parameters;
            var choices = view.GetVisualDescendants().OfType<ComboBox>()
                .Where(box => box.DataContext is EquipmentCardParameterConfiguration && Grid.GetColumn(box) == 6)
                .ToArray();
            Assert.Equal(3, choices.Length);
            Assert.All(choices, box => Assert.Equal(EquipmentParameterVisibility.User, box.SelectedValue));
            Assert.Single(choices, box => ReferenceEquals(box.DataContext, parameters[1]))
                .SetCurrentValue(SelectingItemsControl.SelectedIndexProperty, 1);
            editor.ApplyCommand.Execute().Subscribe();
            Assert.Null(editor.ErrorMessage);
            Assert.Equal(EquipmentParameterVisibility.Admin,
                fixture.Manager.CurrentDefinition.MapEquipment.Single().Parameters[1].Visibility);
            Assert.Equal(EquipmentParameterVisibility.User, fixture.Storage.LoadActive()!.Cards.Single().Parameters[1].Visibility);
            await editor.SaveCommand.Execute().ToTask().WaitAsync(TestTimeout);
            Assert.Null(editor.ErrorMessage);
            var reloaded = await fixture.Manager.LoadActiveDraftAsync();
            Assert.Equal(new[] { EquipmentParameterVisibility.User, EquipmentParameterVisibility.Admin, EquipmentParameterVisibility.User },
                reloaded.Cards.Single().Parameters.Select(parameter => parameter.Visibility));
            Assert.True(fixture.Manager.Apply(reloaded).IsSuccess);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task SaveOperatorDialogAsync(EquipmentCardParametersDialogView dialog)
    {
        var viewModel = Assert.IsType<EquipmentCardParametersDialogViewModel>(dialog.DataContext);
        var saveCompleted = viewModel.SaveCommand.FirstAsync().ToTask();
        InvokeButton(dialog, "Сохранить");
        await saveCompleted.WaitAsync(TestTimeout);
        Assert.Null(viewModel.ErrorMessage);
    }

    private static EquipmentCardParametersDialogViewModel ViewModel(Control dialog) =>
        Assert.IsType<EquipmentCardParametersDialogViewModel>(dialog.DataContext);

    private static JsonArray ParametersInJson(JsonNode document, string cardId = BucketId) =>
        document["cards"]!.AsArray().Single(card => (string?)card!["id"] == cardId)!["parameters"]!.AsArray();

    private static void SetDraft(EquipmentCardParametersDialogView dialog, string title, string signalId,
        SignalBindingDirection direction = SignalBindingDirection.ReadWrite, SignalValueType type = SignalValueType.Word)
    {
        dialog.FindControl<TextBox>("NewParameterTitleBox")!.SetCurrentValue(TextBox.TextProperty, title);
        dialog.FindControl<TextBox>("NewParameterSignalIdBox")!.SetCurrentValue(TextBox.TextProperty, signalId);
        var choices = dialog.FindControl<Border>("NewParameterForm")!.GetVisualDescendants().OfType<ComboBox>().ToArray();
        choices[0].SetCurrentValue(SelectingItemsControl.SelectedItemProperty, direction);
        choices[1].SetCurrentValue(SelectingItemsControl.SelectedItemProperty, type);
    }

    private static async Task ClickCreateAsync(DialogFixture fixture, EquipmentCardParametersDialogView dialog)
    {
        var created = ViewModel(dialog).CreateCommand.FirstAsync().ToTask();
        fixture.Click(FindButton(dialog, "Создать"));
        await created.WaitAsync(TestTimeout);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertDraftError(EquipmentCardParametersDialogView dialog,
        string title, string signalId, string message, int expectedRows)
    {
        var vm = ViewModel(dialog);
        Assert.True(dialog.FindControl<Border>("NewParameterForm")!.IsEffectivelyVisible);
        Assert.Equal(title, dialog.FindControl<TextBox>("NewParameterTitleBox")!.Text ?? "");
        Assert.Equal(signalId, dialog.FindControl<TextBox>("NewParameterSignalIdBox")!.Text ?? "");
        Assert.Contains(message, vm.CreateErrorMessage);
        Assert.Contains(vm.CreateErrorMessage, VisibleTexts(dialog));
        Assert.Equal(expectedRows, vm.Parameters.Count);
        Assert.Null(vm.StatusMessage);
        Assert.True(FindButton(dialog, "Создать").IsEffectivelyEnabled);
        Assert.False(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
    }

    private static string?[] VisibleTexts(Control dialog) => dialog.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible)
        .Select(text => text.Text)
        .ToArray();

    private static Button FindButton(Control dialog, string content) =>
        Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, content));

    private static void InvokeButton(Control dialog, string content)
    {
        var button = FindButton(dialog, content);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(button)).Invoke();
    }

    private static ItemsControl ParameterList(Control dialog) =>
        Assert.Single(dialog.GetVisualDescendants().OfType<ItemsControl>(), items => items.GetType() == typeof(ItemsControl));

    private sealed class DialogFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"equipment-dialog-{Guid.NewGuid():N}");
        private readonly DialogHostStyles _hostStyles = new();
        private readonly ServiceProvider _services;
        private readonly DialogHost _host;
        private readonly Window _window;
        private readonly string _workMode;
        private readonly bool _offline;
        private readonly RouteMapSignalRuntime? _signalRuntime;

        public DialogFixture(string workMode, bool allAdmin = false, bool offline = false,
            bool adminInitiallyVisible = false, int parameterCount = 3, string? pendingError = "Предыдущая ошибка",
            bool includeOtherCard = false, ModbusOptions? modbus = null)
        {
            _workMode = workMode;
            _offline = offline;
            Storage = new RouteMapConfigurationStorage(Path.Combine(_directory, "route-map.json"));
            var mapper = new RouteMapConfigurationMapper(RouteMapSeed.Create());
            var document = mapper.CreateSeedDocument();
            var bucket = Assert.Single(document.Cards, card => card.Id == BucketId);
            if (includeOtherCard)
            {
                var other = Storage.Clone(document).Cards.Single();
                other.Id = OtherCardId;
                other.Title = "Другая карточка";
                other.AttachedChainId = null;
                document.Cards.Add(other);
            }
            bucket.Parameters.Add(new EquipmentCardParameterConfiguration
            {
                Title = "Скорость",
                SignalId = UserSignalId,
                ValueType = SignalValueType.UInt16,
                Visibility = allAdmin ? EquipmentParameterVisibility.Admin : EquipmentParameterVisibility.User,
                SavedValue = "100",
            });
            bucket.Parameters.Add(new EquipmentCardParameterConfiguration
            {
                Title = "Фильтер",
                SignalId = AdminSignalId,
                ValueType = SignalValueType.UInt16,
                Visibility = adminInitiallyVisible ? EquipmentParameterVisibility.User : EquipmentParameterVisibility.Admin,
                SavedValue = "5000",
                PendingAutoDispatch = true,
                LastDispatchError = pendingError,
            });
            bucket.Parameters.Add(new EquipmentCardParameterConfiguration
            {
                Title = "Текущий вес",
                SignalId = ReadSignalId,
                Direction = SignalBindingDirection.Read,
                ValueType = SignalValueType.UInt16,
                Visibility = allAdmin ? EquipmentParameterVisibility.Admin : EquipmentParameterVisibility.User,
            });
            while (bucket.Parameters.Count > parameterCount)
                bucket.Parameters.RemoveAt(bucket.Parameters.Count - 1);
            for (var index = bucket.Parameters.Count; index < parameterCount; index++)
            {
                bucket.Parameters.Add(new EquipmentCardParameterConfiguration
                {
                    Title = $"Параметр {index}",
                    SignalId = $"test.parameter.extra.{index}",
                    Direction = SignalBindingDirection.Read,
                });
            }
            Storage.SaveActive(document);
            Manager = CreateManager();
            Assert.Null(Manager.LastLoadError);
            Store = new RecordingValueStore(Manager, Options.Create(new ApplicationOptions { WorkMode = workMode }));
            IEquipmentParameterWriteValidator validator = NoopEquipmentParameterWriteValidator.Instance;
            if (modbus is not null)
            {
                var signals = new EmptySignalProvider();
                _signalRuntime = new RouteMapSignalRuntime(signals, Dispatcher, signals, Dispatcher, RouteMapSignalSource.Modbus);
                validator = new EquipmentParameterWriteValidator(_signalRuntime, new StaticModbusOptions(modbus));
            }
            _services = new ServiceCollection()
                .AddSingleton<IEquipmentCommandDispatcher>(Dispatcher)
                .AddSingleton<IEquipmentParameterWriteValidator>(validator)
                .AddSingleton<IEquipmentParameterValueStore>(Store)
                .AddSingleton<IEquipmentCardParameterDefinitionStore>(Store)
                .AddTransient<EquipmentCardParametersDialogView>()
                .BuildServiceProvider();
            Avalonia.Application.Current!.Styles.Add(_hostStyles);
            _host = new DialogHost
            {
                Identifier = DialogHostIds.Root,
                IsMultipleDialogsEnabled = true,
                DisableOpeningAnimation = true,
            };
            _window = new Window { Width = 700, Height = 480, Content = _host };
            _window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        public RouteMapConfigurationStorage Storage { get; }
        public RouteMapConfigurationManager Manager { get; }
        public RecordingValueStore Store { get; }
        public RecordingDispatcher Dispatcher { get; } = new();

        public RouteMapConfigurationManager CreateManager() => new(Storage,
            new RouteMapConfigurationMapper(RouteMapSeed.Create()), new RouteMapConfigurationValidator(),
            new RouteMapConfigurationMigrator());

        public string ReadJson() => File.ReadAllText(Storage.ActiveFilePath);

        public EquipmentCardParameterConfiguration Parameter(string signalId) =>
            Assert.Single(Storage.LoadActive()!.Cards.Single(card => card.Id == BucketId).Parameters,
                parameter => parameter.SignalId == signalId);

        public EquipmentCardParametersDialogView Open(string? workMode = null, string cardId = BucketId,
            RouteMapConfigurationManager? manager = null)
        {
            var dialogService = new EquipmentCardParametersDialogService(
                _services, new DialogCoordinator(), Options.Create(new ApplicationOptions { WorkMode = workMode ?? _workMode }));
            var card = (manager ?? Manager).CurrentDefinition.MapEquipment.Single(card => card.Id == cardId);
            var cardViewModel = new EquipmentCardViewModel(card, Dispatcher, parametersDialogService: dialogService);
            cardViewModel.ApplyRuntime(new RouteObjectRuntimeState(
                card.Id, _offline ? RouteObjectState.Offline : RouteObjectState.Idle, card.StatusText, ValueText: null,
                IsVisible: true, CanStart: false, CanStop: false, IsEnabled: !_offline), isConnectionAvailable: !_offline);
            var cardView = new EquipmentCardView { DataContext = cardViewModel };
            _host.Content = cardView;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var button = cardView.FindControl<Button>("ParametersButton")!;
            Assert.True(button.IsEffectivelyEnabled);
            Click(button);
            Assert.True(_host.IsOpen);
            return Assert.Single(_host.GetVisualDescendants().OfType<EquipmentCardParametersDialogView>());
        }

        public async Task CloseAsync(EquipmentCardParametersDialogView dialog)
        {
            var viewModel = ViewModel(dialog);
            var closed = viewModel.Result.FirstAsync().ToTask();
            InvokeButton(dialog, "Закрыть");
            Assert.False(await closed.WaitAsync(TestTimeout));
            Assert.False(_host.IsOpen);
        }

        public void Click(Button button)
        {
            button.BringIntoView();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var center = new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
            var point = button.TranslatePoint(center, _window)!.Value;
            _window.MouseMove(point, RawInputModifiers.None);
            _window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            _window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        public void PressSpace()
        {
            _window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            _window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        public void CaptureFrame(string name)
        {
            if (HeadlessTestApplication.ScreenshotDirectory is not { Length: > 0 } directory)
                return;

            Directory.CreateDirectory(directory);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = Assert.IsType<Avalonia.Media.Imaging.WriteableBitmap>(_window.CaptureRenderedFrame());
            frame.Save(Path.Combine(directory, name + ".png"));
        }

        public void Dispose()
        {
            if (_host.IsOpen)
                DialogHost.Close(DialogHostIds.Root, false);
            _window.Close();
            Avalonia.Application.Current!.Styles.Remove(_hostStyles);
            _services.Dispose();
            _signalRuntime?.Dispose();
            Store.Dispose();
            Manager.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingValueStore(RouteMapConfigurationManager manager, IOptions<ApplicationOptions> options)
        : IEquipmentParameterValueStore, IEquipmentCardParameterDefinitionStore, IDisposable
    {
        private readonly RouteMapEquipmentParameterValueStore _store = new(manager, options);
        public List<(EquipmentParameterSetpoint[] Values, bool Pending)> Saves { get; } = [];
        public List<(string CardId, NewEquipmentCardParameterDefinition Definition)> Additions { get; } = [];

        public Task SaveAsync(string cardId, IReadOnlyCollection<EquipmentParameterSetpoint> values,
            bool pendingAutoDispatch, CancellationToken cancellationToken = default)
        {
            Saves.Add((values.ToArray(), pendingAutoDispatch));
            return _store.SaveAsync(cardId, values, pendingAutoDispatch, cancellationToken);
        }

        public IReadOnlyList<PendingEquipmentParameter> GetPending() => _store.GetPending();

        public Task<EquipmentCardParameter> AddAsync(string cardId, NewEquipmentCardParameterDefinition definition,
            CancellationToken cancellationToken = default)
        {
            Additions.Add((cardId, definition));
            return _store.AddAsync(cardId, definition, cancellationToken);
        }

        public Task CompleteDispatchAsync(string cardId, string signalId, string expectedSavedValue,
            string? errorMessage, CancellationToken cancellationToken = default) =>
            _store.CompleteDispatchAsync(cardId, signalId, expectedSavedValue, errorMessage, cancellationToken);

        public void Dispose() => _store.Dispose();
    }

    private sealed class StaticModbusOptions(ModbusOptions options) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue => options;
        public ModbusOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<ModbusOptions, string?> listener) => null;
    }

    private sealed class EmptySignalProvider : ISignalValueProvider
    {
        public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() =>
            Observable.Never<IReadOnlyDictionary<string, SignalValue>>();
    }

    private sealed class NullFilePicker : IRouteMapSettingsFilePicker
    {
        public Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class RecordingDispatcher : IEquipmentCommandDispatcher
    {
        public List<SignalWriteRequest> Requests { get; } = [];

        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }
}
