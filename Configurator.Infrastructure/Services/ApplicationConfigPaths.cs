namespace Configurator.Infrastructure.Services;

public static class ApplicationConfigPaths
{
    public const string AppSettingsFileName = "appsettings.json";
    public const string ApplicationDirectoryName = "Configurator";

    public static string SharedConfigDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationDirectoryName);

    public static string SharedAppSettingsPath =>
        Path.Combine(SharedConfigDirectory, AppSettingsFileName);
}
