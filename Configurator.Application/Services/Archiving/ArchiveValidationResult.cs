namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Validation result for archive configuration contracts.
/// </summary>
public sealed record ArchiveValidationResult
{
    public ArchiveValidationResult(IEnumerable<ArchiveValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = Array.AsReadOnly(errors.ToArray());
    }

    public bool Succeeded => Errors.Count == 0;

    public IReadOnlyList<ArchiveValidationError> Errors { get; }

    public static ArchiveValidationResult Success()
        => new(Array.Empty<ArchiveValidationError>());

    public static ArchiveValidationResult Failure(
        string code,
        string message,
        string? propertyName = null)
        => new([new ArchiveValidationError(code, message, propertyName)]);
}

/// <summary>
/// Single archive validation error.
/// </summary>
public sealed record ArchiveValidationError(
    string Code,
    string Message,
    string? PropertyName);
