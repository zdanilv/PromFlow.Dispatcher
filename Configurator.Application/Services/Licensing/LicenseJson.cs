using System.Text.Json;
using System.Text.Json.Serialization;

namespace Configurator.Application.Services.Licensing;

public static class LicenseJson
{
    public static JsonSerializerOptions Options { get; } = Create(writeIndented: false);

    public static JsonSerializerOptions IndentedOptions { get; } = Create(writeIndented: true);

    private static JsonSerializerOptions Create(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = writeIndented
        };
        options.Converters.Add(new JsonStringEnumConverter<LicenseEdition>());
        options.Converters.Add(new JsonStringEnumConverter<LicenseInstallationBindingMode>());

        return options;
    }
}
