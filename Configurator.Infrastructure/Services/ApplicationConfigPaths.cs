namespace Configurator.Infrastructure.Services;

public static class ApplicationConfigPaths
{
    public const string AppSettingsFileName = "appsettings.json";
    public const string UserSettingsFileName = "user_settings.json";
    public const string ApplicationDirectoryName = "Configurator";
    public const string LogsDirectoryName = "logs";

    public static string SharedConfigDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationDirectoryName);

    public static string SharedAppSettingsPath =>
        Path.Combine(SharedConfigDirectory, AppSettingsFileName);

    public static string UserSettingsPath =>
        Path.Combine(SharedConfigDirectory, UserSettingsFileName);

    public static string LogsDirectory =>
        Path.Combine(SharedConfigDirectory, LogsDirectoryName);

    public static string LogFilePattern =>
        Path.Combine(LogsDirectory, "app-.log");
}
