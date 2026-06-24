namespace Configurator.Application.Services.Authorization;

public interface IUserRepository
{
    Task<long> CountAsync(CancellationToken cancellationToken = default);

    Task<AppUser?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AppUser?> FindByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken = default);

    Task<UserRepositoryResult<AppUser>> CreateAsync(
        AppUser user,
        CancellationToken cancellationToken = default);

    Task<UserRepositoryResult<AppUser>> BootstrapAdministratorAsync(
        AppUser user,
        CancellationToken cancellationToken = default);

    Task<UserRepositoryResult<AppUser>> UpdateAsync(
        AppUser user,
        long expectedRowVersion,
        CancellationToken cancellationToken = default);
}

public sealed record UserRepositoryResult<T>(
    bool Succeeded,
    T? Value,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static UserRepositoryResult<T> Success(T value)
        => new(true, value, null, null);

    public static UserRepositoryResult<T> Failure(string errorCode, string errorMessage)
        => new(false, default, errorCode, errorMessage);
}
