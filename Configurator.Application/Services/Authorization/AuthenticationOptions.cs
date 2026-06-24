namespace Configurator.Application.Services.Authorization;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    public int MinimumPasswordLength { get; set; } = 12;

    public bool RequireDigit { get; set; } = true;

    public bool RequireUppercase { get; set; } = true;

    public bool RequireLowercase { get; set; } = true;

    public string SecurityDatabasePath { get; set; } = string.Empty;

    public PasswordPolicy ToPasswordPolicy()
        => new(MinimumPasswordLength, RequireDigit, RequireUppercase, RequireLowercase);

    public UserLockoutPolicy ToLockoutPolicy()
        => new(MaxFailedAttempts, TimeSpan.FromMinutes(LockoutMinutes));

    public AuthenticationOptions Clone()
        => new()
        {
            MaxFailedAttempts = MaxFailedAttempts,
            LockoutMinutes = LockoutMinutes,
            MinimumPasswordLength = MinimumPasswordLength,
            RequireDigit = RequireDigit,
            RequireUppercase = RequireUppercase,
            RequireLowercase = RequireLowercase,
            SecurityDatabasePath = SecurityDatabasePath
        };
}
