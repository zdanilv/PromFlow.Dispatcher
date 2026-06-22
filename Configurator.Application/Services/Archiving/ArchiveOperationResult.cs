namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Result of an archive operation without a payload.
/// </summary>
public sealed record ArchiveOperationResult(
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorDetails)
{
    public static ArchiveOperationResult Success()
        => new(true, null, null, null);

    public static ArchiveOperationResult Failure(string code, string message, string? details = null)
        => new(false, code, message, details);
}

/// <summary>
/// Result of an archive operation with a payload.
/// </summary>
public sealed record ArchiveOperationResult<T>(
    bool Succeeded,
    T? Value,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorDetails)
{
    public static ArchiveOperationResult<T> Success(T value)
        => new(true, value, null, null, null);

    public static ArchiveOperationResult<T> Failure(string code, string message, string? details = null)
        => new(false, default, code, message, details);
}
