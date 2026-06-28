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

namespace Configurator.Application.Services.Dialogs;

/// <summary>
/// Provides user dialog interactions required by application use-cases.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows a confirmation dialog and waits for user decision.
    /// </summary>
    /// <param name="message">Text message displayed to the user.</param>
    /// <param name="ct">Cancellation token. If canceled before completion, the operation is canceled.</param>
    /// <returns>
    /// <see langword="true"/> when the user confirms the action; otherwise <see langword="false"/>,
    /// including explicit rejection and dialog close/cancel scenarios.
    /// </returns>
    Task<bool> ConfirmAsync(string message, CancellationToken ct = default);

    /// <summary>
    /// Shows a secure input dialog and requests a secret value from user.
    /// </summary>
    /// <param name="message">Text message displayed to the user.</param>
    /// <param name="ct">Cancellation token. If canceled before completion, the operation is canceled.</param>
    /// <returns>
    /// Secret string provided by user, or <see langword="null"/> when dialog was canceled/closed
    /// without confirmed input.
    /// </returns>
    Task<string?> RequestSecretAsync(string message, CancellationToken ct = default);

    /// <summary>
    /// Shows an error dialog and waits until the user closes it.
    /// </summary>
    Task ShowErrorAsync(
        string title,
        string message,
        string? details = null,
        CancellationToken ct = default);

    Task<bool> ShowAlarmNotificationAsync(
        ModbusAlarmKind kind,
        string message,
        CancellationToken ct = default);

    Task<ModbusOptions?> EditModbusSettingsAsync(
        string title,
        string sectionName,
        ModbusOptions options,
        CancellationToken ct = default);

    Task<OpcUaConfiguredTag?> EditOpcUaTagAsync(
        string title,
        OpcUaConfiguredTag? tag,
        OpcUaImportTarget target,
        CancellationToken ct = default);

    Task<OpcUaTagImportResult?> ImportOpcUaTagsAsync(
        OpcUaBrowseRequest request,
        CancellationToken ct = default);
}
