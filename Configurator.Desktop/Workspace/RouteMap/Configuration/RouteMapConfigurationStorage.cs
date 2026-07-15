using System.Text.Json;
using System.Text.Json.Serialization;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Configuration;

public sealed class RouteMapConfigurationStorage
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public RouteMapConfigurationStorage()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Configurator",
            "RouteMap",
            "route-map.json"))
    {
    }

    public RouteMapConfigurationStorage(string activeFilePath)
    {
        ActiveFilePath = activeFilePath;
    }

    public string ActiveFilePath { get; }

    public RouteMapConfigurationDocument? LoadActive()
    {
        if (!File.Exists(ActiveFilePath))
            return null;

        return Load(ActiveFilePath);
    }

    public RouteMapConfigurationDocument Load(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var document = JsonSerializer.Deserialize<RouteMapConfigurationDocument>(stream, JsonOptions);
        return Normalize(document ?? throw new InvalidDataException("JSON не содержит документа RouteMap."));
    }

    public async Task<RouteMapConfigurationDocument?> LoadActiveAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ActiveFilePath))
            return null;

        return await LoadAsync(ActiveFilePath, cancellationToken);
    }

    public async Task<RouteMapConfigurationDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var document = await JsonSerializer.DeserializeAsync<RouteMapConfigurationDocument>(
            stream,
            JsonOptions,
            cancellationToken);
        return Normalize(document ?? throw new InvalidDataException("JSON не содержит документа RouteMap."));
    }

    public Task SaveActiveAsync(
        RouteMapConfigurationDocument document,
        CancellationToken cancellationToken = default) =>
        SaveAsync(ActiveFilePath, document, cancellationToken);

    public void SaveActive(RouteMapConfigurationDocument document) =>
        Save(ActiveFilePath, document);

    public void Save(string path, RouteMapConfigurationDocument document)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        try
        {
            using (var stream = File.Open(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task SaveAsync(
        string path,
        RouteMapConfigurationDocument document,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        try
        {
            await using (var stream = File.Open(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public RouteMapConfigurationDocument Clone(RouteMapConfigurationDocument document)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);
        return Normalize(JsonSerializer.Deserialize<RouteMapConfigurationDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Не удалось клонировать документ RouteMap."));
    }

    private static RouteMapConfigurationDocument Normalize(RouteMapConfigurationDocument document)
    {
        document.Map ??= new RouteMapSettingsConfiguration();
        document.Map.Palette ??= new RouteMapPaletteConfiguration();
        document.TopBar ??= RouteTopBarConfiguration.CreateDefault();
        document.TopBar.Automatic ??= RouteTopBarButtonConfiguration.Create("АВТОМАТ", SignalBindingRole.AutomaticModeCommand, "system.mode.automatic");
        document.TopBar.Manual ??= RouteTopBarButtonConfiguration.Create("РУЧНОЙ", SignalBindingRole.ManualModeCommand, "system.mode.manual");
        document.TopBar.Reset ??= RouteTopBarButtonConfiguration.Create(
            "СБРОС",
            SignalBindingRole.ResetCommand,
            "system.reset",
            normalBackground: "#FEFFB8",
            checkedBackground: "#B7791F",
            pressedBackground: "#A0A300",
            hoverBackground: "#FFFFE6",
            normalForeground: "#101820");
        document.TopBar.Emergency ??= RouteTopBarEmergencyButtonConfiguration.Create("АВАРИЯ", SignalBindingRole.EmergencyCommand, "system.emergency");
        document.TopBar.Automatic.Bindings ??= [];
        document.TopBar.Manual.Bindings ??= [];
        document.TopBar.Reset.Bindings ??= [];
        document.TopBar.Emergency.Bindings ??= [];
        document.Chains ??= [];
        document.Nodes ??= [];
        document.Segments ??= [];
        document.Cards ??= [];
        document.PlaceholderRules ??= [];

        foreach (var chain in document.Chains)
        {
            chain.NodeIds ??= [];
            chain.SegmentIds ??= [];
        }
        foreach (var node in document.Nodes)
        {
            node.Style ??= new RouteNodeStyleConfiguration();
            node.Bindings ??= [];
        }
        foreach (var segment in document.Segments)
        {
            segment.Style ??= new RouteSegmentStyleConfiguration();
            segment.Bindings ??= [];
        }
        foreach (var card in document.Cards)
        {
            card.Style ??= new EquipmentCardStyleConfiguration();
            card.Style.Margin ??= RouteThicknessConfiguration.Uniform(5);
            card.Style.Padding ??= new RouteThicknessConfiguration();
            card.Style.BorderThickness ??= new RouteThicknessConfiguration();
            card.Style.CornerRadius ??= new RouteCornerRadiusConfiguration();
            card.Bindings ??= [];
            card.Parameters ??= [];
        }
        foreach (var rule in document.PlaceholderRules)
        {
            rule.Style ??= new RoutePlaceholderStyleConfiguration();
            rule.Style.Margin ??= RouteThicknessConfiguration.Uniform(5);
            rule.Style.BorderThickness ??= new RouteThicknessConfiguration();
            rule.Style.CornerRadius ??= new RouteCornerRadiusConfiguration();
        }

        return document;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
