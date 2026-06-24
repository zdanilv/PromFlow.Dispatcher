using Configurator.Application.Services.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Configurator.Infrastructure.Persistence.Security;

public sealed class PasswordHashService : IPasswordHashService
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    public string HashPassword(AppUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        return _hasher.HashPassword(user, password);
    }

    public PasswordHashVerificationResult VerifyPassword(AppUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        return _hasher.VerifyHashedPassword(user, user.PasswordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordHashVerificationResult.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordHashVerificationResult.SuccessRehashNeeded,
            _ => PasswordHashVerificationResult.Failed
        };
    }
}
