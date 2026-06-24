namespace Configurator.Application.Services.Authorization;

public interface IPasswordHashService
{
    string HashPassword(AppUser user, string password);

    PasswordHashVerificationResult VerifyPassword(AppUser user, string password);
}

public enum PasswordHashVerificationResult
{
    Failed,
    Success,
    SuccessRehashNeeded
}
