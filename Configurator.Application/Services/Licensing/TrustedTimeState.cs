namespace Configurator.Application.Services.Licensing;

public sealed record TrustedTimeState(
    DateTimeOffset MaxObservedUtc,
    DateTimeOffset UpdatedAtUtc);
