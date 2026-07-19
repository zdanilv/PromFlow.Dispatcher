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

/// <summary>
/// Dialog service implementation based on DialogHost.
/// </summary>
/// <remarks>
/// Uses <c>RootDialogHost</c> host identifier and maps UI outcomes to application-level results.
/// </remarks>
public sealed class DialogHostDialogService(
    IDialogViewFactory dialogViewFactory,
    DialogCoordinator dialogCoordinator) : IDialogService
{
    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
    {
        var context = dialogViewFactory.CreateConfirm(message);
        var result = await ShowWithFallbackAsync(context, fallback: false, ct);
        return result is true;
    }

    /// <inheritdoc />
    public async Task<string?> RequestSecretAsync(string message, CancellationToken ct = default)
    {
        var context = dialogViewFactory.CreateSecretInput(message);
        return await ShowWithFallbackAsync<string?>(context, fallback: default, ct);
    }

    /// <inheritdoc />
    public async Task ShowErrorAsync(
        string title,
        string message,
        string? details = null,
        CancellationToken ct = default)
    {
        var text = string.IsNullOrWhiteSpace(details)
            ? $"{title}{Environment.NewLine}{Environment.NewLine}{message}"
            : $"{title}{Environment.NewLine}{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}{details}";
        var context = dialogViewFactory.CreateConfirm(text);
        await ShowWithFallbackAsync(context, fallback: false, ct);
    }

    public async Task<bool> ShowAlarmNotificationAsync(
        ModbusAlarmKind kind,
        string message,
        CancellationToken ct = default)
        => await ShowAlarmNotificationAsync(new AlarmNotificationContent(kind, message), ct);

    public async Task<bool> ShowAlarmNotificationAsync(
        AlarmNotificationContent notification,
        CancellationToken ct = default)
    {
        var context = dialogViewFactory.CreateAlarmNotification(notification);
        var result = await ShowWithFallbackAsync(context, fallback: false, ct);
        return result is true;
    }

    public async Task<ModbusOptions?> EditModbusSettingsAsync(
        string title,
        string sectionName,
        ModbusOptions options,
        CancellationToken ct = default)
    {
        var context = dialogViewFactory.CreateModbusSettings(title, sectionName, options);
        return await ShowWithFallbackAsync<ModbusOptions?>(context, fallback: default, ct);
    }

    public async Task<OpcUaConfiguredTag?> EditOpcUaTagAsync(
        string title,
        OpcUaConfiguredTag? tag,
        OpcUaImportTarget target,
        CancellationToken ct = default)
    {
        var context = dialogViewFactory.CreateOpcUaTagEditor(title, tag, target);
        return await ShowWithFallbackAsync<OpcUaConfiguredTag?>(context, fallback: default, ct);
    }

    public async Task<OpcUaTagImportResult?> ImportOpcUaTagsAsync(
        OpcUaBrowseRequest request,
        CancellationToken ct = default)
    {
        var context = dialogViewFactory.CreateOpcUaTagImport(request);
        return await ShowWithFallbackAsync<OpcUaTagImportResult?>(context, fallback: default, ct);
    }

    private async Task<TResult?> ShowWithFallbackAsync<TResult>(
        DialogViewContext<TResult> context,
        TResult? fallback,
        CancellationToken ct)
    {
        try
        {
            return await dialogCoordinator.ShowAsync(context, ct);
        }
        catch (InvalidOperationException ex) when (IsMissingHostException(ex))
        {
            throw new InvalidOperationException(
                $"Dialog host '{context.HostIdentifier}' was not found. Ensure it is present in MainWindow.",
                ex);
        }
        return fallback;
    }

    private static bool IsMissingHostException(InvalidOperationException ex)
    {
        var message = ex.Message;
        return message.Contains("not found", StringComparison.OrdinalIgnoreCase)
               || message.Contains("no loaded", StringComparison.OrdinalIgnoreCase);
    }

}
