namespace Configurator.Desktop.Dialogs;

/// <summary>
/// Known dialog host identifiers used by desktop UI.
/// </summary>
public static class DialogHostIds
{
    /// <summary>
    /// Main/root host for global modal dialogs.
    /// </summary>
    public const string Root = "RootDialogHost";

    /// <summary>
    /// Host for alarm notifications without the default DialogHost popup frame.
    /// </summary>
    public const string AlarmNotification = "AlarmNotificationDialogHost";
}
