namespace Configurator.Application.Services.Signals;

public sealed record SignalWriteRequest(
    string SignalId,
    object? Value,
    SignalValueType ValueType);
