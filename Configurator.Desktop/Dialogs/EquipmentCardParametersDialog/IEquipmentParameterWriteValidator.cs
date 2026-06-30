using Configurator.Application.Services.Signals;

namespace Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;

public interface IEquipmentParameterWriteValidator
{
    string? Validate(SignalWriteRequest request);
}

public sealed class NoopEquipmentParameterWriteValidator : IEquipmentParameterWriteValidator
{
    public static NoopEquipmentParameterWriteValidator Instance { get; } = new();

    private NoopEquipmentParameterWriteValidator()
    {
    }

    public string? Validate(SignalWriteRequest request) => null;
}
