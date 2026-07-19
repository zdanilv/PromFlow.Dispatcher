using Configurator.Desktop.Dialogs.ConfirmDialog;
using Configurator.Desktop.Dialogs.InputDialog;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Configurator.Desktop.Dialogs.AlarmNotificationDialog;
using Configurator.Desktop.Dialogs.ModbusSettingsDialog;
using Configurator.Desktop.Dialogs.OpcUaTagEditorDialog;
using Configurator.Desktop.Dialogs.OpcUaTagImportDialog;
using Microsoft.Extensions.DependencyInjection;

namespace Configurator.Desktop.Dialogs;

public sealed class DialogViewFactory(IServiceProvider serviceProvider) : IDialogViewFactory
{
    public DialogViewContext<bool> CreateConfirm(string message)
    {
        var vm = ActivatorUtilities.CreateInstance<ConfirmDialogViewModel>(serviceProvider, message);
        var view = serviceProvider.GetRequiredService<ConfirmDialogView>();
        view.DataContext = vm;

        return new DialogViewContext<bool>(view, vm.Result);
    }

    public DialogViewContext<string?> CreateSecretInput(string message)
    {
        var vm = ActivatorUtilities.CreateInstance<InputDialogViewModel>(serviceProvider, message);
        var view = serviceProvider.GetRequiredService<InputDialogView>();
        view.DataContext = vm;

        return new DialogViewContext<string?>(view, vm.Result);
    }

    public DialogViewContext<bool> CreateAlarmNotification(
        ModbusAlarmKind kind,
        string message)
    {
        var vm = ActivatorUtilities.CreateInstance<AlarmNotificationDialogViewModel>(
            serviceProvider,
            kind,
            message);
        var view = serviceProvider.GetRequiredService<AlarmNotificationDialogView>();
        view.DataContext = vm;

        return new DialogViewContext<bool>(view, vm.Result, DialogHostIds.AlarmNotification);
    }

    public DialogViewContext<ModbusOptions?> CreateModbusSettings(
        string title,
        string sectionName,
        ModbusOptions options)
    {
        var vm = ActivatorUtilities.CreateInstance<ModbusSettingsDialogViewModel>(
            serviceProvider,
            title,
            sectionName,
            options);
        var view = serviceProvider.GetRequiredService<ModbusSettingsDialogView>();
        view.DataContext = vm;

        return new DialogViewContext<ModbusOptions?>(view, vm.Result);
    }

    public DialogViewContext<OpcUaConfiguredTag?> CreateOpcUaTagEditor(
        string title,
        OpcUaConfiguredTag? tag,
        OpcUaImportTarget target)
    {
        var vm = ActivatorUtilities.CreateInstance<OpcUaTagEditorDialogViewModel>(
            serviceProvider,
            title,
            tag,
            target);
        var view = serviceProvider.GetRequiredService<OpcUaTagEditorDialogView>();
        view.DataContext = vm;

        return new DialogViewContext<OpcUaConfiguredTag?>(view, vm.Result);
    }

    public DialogViewContext<OpcUaTagImportResult?> CreateOpcUaTagImport(
        OpcUaBrowseRequest request)
    {
        var vm = ActivatorUtilities.CreateInstance<OpcUaTagImportDialogViewModel>(serviceProvider, request);
        var view = serviceProvider.GetRequiredService<OpcUaTagImportDialogView>();
        view.DataContext = vm;

        return new DialogViewContext<OpcUaTagImportResult?>(view, vm.Result);
    }
}
