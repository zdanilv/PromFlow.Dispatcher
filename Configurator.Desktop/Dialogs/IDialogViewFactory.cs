using Configurator.Application.Services.Dialogs;
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

namespace Configurator.Desktop.Dialogs;

public interface IDialogViewFactory
{
    /// <summary>
    /// Creates a confirm dialog view and view-model pair through DI for desktop-layer presentation.
    /// </summary>
    /// <param name="message">Dialog text that describes the confirmation action to the user.</param>
    /// <returns>Prepared view with bound view-model and its result stream.</returns>
    DialogViewContext<bool> CreateConfirm(string message);

    /// <summary>
    /// Creates a secret input dialog view and view-model pair through DI for desktop-layer presentation.
    /// </summary>
    /// <param name="message">Dialog text that describes the requested secret input to the user.</param>
    /// <returns>Prepared view with bound view-model and its result stream.</returns>
    DialogViewContext<string?> CreateSecretInput(string message);

    DialogViewContext<bool> CreateAlarmNotification(
        ModbusAlarmKind kind,
        string message);

    DialogViewContext<bool> CreateAlarmNotification(AlarmNotificationContent notification)
        => CreateAlarmNotification(notification.Kind, notification.Message);

    DialogViewContext<ModbusOptions?> CreateModbusSettings(
        string title,
        string sectionName,
        ModbusOptions options);

    DialogViewContext<OpcUaConfiguredTag?> CreateOpcUaTagEditor(
        string title,
        OpcUaConfiguredTag? tag,
        OpcUaImportTarget target);

    DialogViewContext<OpcUaTagImportResult?> CreateOpcUaTagImport(
        OpcUaBrowseRequest request);
}
