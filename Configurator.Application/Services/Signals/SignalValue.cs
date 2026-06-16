namespace Configurator.Application.Services.Signals;

public sealed record SignalValue(
    string SignalId,
    object? Value,
    SignalValueType ValueType,
    DateTimeOffset Timestamp,
    bool IsQualityGood,
    bool IsStale);
