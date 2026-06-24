namespace Configurator.Application.Services.Authorization;

public sealed record PasswordPolicy(
    int MinimumLength,
    bool RequireDigit,
    bool RequireUppercase,
    bool RequireLowercase)
{
    public static PasswordPolicy Default { get; } = new(12, true, true, true);
}
