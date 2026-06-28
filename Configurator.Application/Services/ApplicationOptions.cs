namespace Configurator.Application.Services;

public sealed class ApplicationOptions
{
    public const string SectionName = "Application";
    public const string AdminWorkMode = "admin";
    public const string UserWorkMode = "user";

    public string WorkMode { get; set; } = AdminWorkMode;

    public string NormalizedWorkMode => IsUserMode ? UserWorkMode : AdminWorkMode;

    public bool IsUserMode => IsUserModeValue(WorkMode);

    public bool IsAdminMode => !IsUserMode;

    private static bool IsUserModeValue(string? value) =>
        string.Equals(value?.Trim(), UserWorkMode, StringComparison.OrdinalIgnoreCase);
}
