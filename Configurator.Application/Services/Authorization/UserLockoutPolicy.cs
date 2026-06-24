namespace Configurator.Application.Services.Authorization;

public sealed record UserLockoutPolicy(int MaxFailedAttempts, TimeSpan Duration)
{
    public static UserLockoutPolicy Default { get; } = new(5, TimeSpan.FromMinutes(15));
}
