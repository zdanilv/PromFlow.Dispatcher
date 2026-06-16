using System.Reactive.Subjects;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Configuration;

public sealed record RouteMapConfigurationOperationResult(
    bool IsSuccess,
    IReadOnlyList<RouteMapConfigurationError> Errors,
    string? ErrorMessage = null)
{
    public static RouteMapConfigurationOperationResult Success { get; } = new(true, []);
}

public sealed class RouteMapConfigurationManager : IDisposable
{
    private readonly RouteMapConfigurationStorage _storage;
    private readonly RouteMapConfigurationMapper _mapper;
    private readonly RouteMapConfigurationValidator _validator;
    private readonly RouteMapConfigurationMigrator _migrator;
    private readonly BehaviorSubject<RouteMapDefinition> _definitionSubject;

    public RouteMapConfigurationManager(
        RouteMapConfigurationStorage storage,
        RouteMapConfigurationMapper mapper,
        RouteMapConfigurationValidator validator,
        RouteMapConfigurationMigrator migrator)
    {
        _storage = storage;
        _mapper = mapper;
        _validator = validator;
        _migrator = migrator;

        var seedDocument = mapper.CreateSeedDocument();
        var initialDocument = seedDocument;
        var initialDefinition = mapper.SeedDefinition;
        try
        {
            var loaded = storage.LoadActive();
            if (loaded is not null)
            {
                var migration = Prepare(loaded);
                var validation = validator.Validate(migration.Document);
                if (!validation.IsValid)
                    throw new InvalidDataException(FormatErrors(validation.Errors));

                if (migration.WasMigrated)
                    storage.SaveActive(migration.Document);

                initialDocument = storage.Clone(migration.Document);
                initialDefinition = mapper.ToDefinition(migration.Document);
            }
        }
        catch (Exception ex)
        {
            LastLoadError = $"Не удалось загрузить '{storage.ActiveFilePath}': {ex.Message}";
        }

        CurrentDocument = initialDocument;
        CurrentDefinition = initialDefinition;
        _definitionSubject = new BehaviorSubject<RouteMapDefinition>(CurrentDefinition);
    }

    public RouteMapConfigurationDocument CurrentDocument { get; private set; }
    public RouteMapDefinition CurrentDefinition { get; private set; }
    public string ActiveFilePath => _storage.ActiveFilePath;
    public string? LastLoadError { get; private set; }
    public IObservable<RouteMapDefinition> DefinitionChanges => _definitionSubject;

    public RouteMapConfigurationDocument CreateDraft() => _storage.Clone(CurrentDocument);

    public RouteMapConfigurationOperationResult Validate(RouteMapConfigurationDocument document)
    {
        try
        {
            var validation = _validator.Validate(Prepare(document).Document);
            return validation.IsValid
                ? RouteMapConfigurationOperationResult.Success
                : new RouteMapConfigurationOperationResult(false, validation.Errors);
        }
        catch (Exception ex)
        {
            return new RouteMapConfigurationOperationResult(false, [], ex.Message);
        }
    }

    public RouteMapConfigurationOperationResult Apply(RouteMapConfigurationDocument document)
    {
        try
        {
            var prepared = Prepare(document).Document;
            var validation = _validator.Validate(prepared);
            if (!validation.IsValid)
                return new RouteMapConfigurationOperationResult(false, validation.Errors);

            var definition = _mapper.ToDefinition(prepared);
            Publish(prepared, definition);
            return RouteMapConfigurationOperationResult.Success;
        }
        catch (Exception ex)
        {
            return new RouteMapConfigurationOperationResult(false, [], ex.Message);
        }
    }

    public async Task<RouteMapConfigurationOperationResult> SaveAndApplyAsync(
        RouteMapConfigurationDocument document,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prepared = Prepare(document).Document;
            var validation = _validator.Validate(prepared);
            if (!validation.IsValid)
                return new RouteMapConfigurationOperationResult(false, validation.Errors);

            var definition = _mapper.ToDefinition(prepared);
            await _storage.SaveActiveAsync(prepared, cancellationToken);
            LastLoadError = null;
            Publish(prepared, definition);
            return RouteMapConfigurationOperationResult.Success;
        }
        catch (Exception ex)
        {
            return new RouteMapConfigurationOperationResult(false, [], ex.Message);
        }
    }

    public async Task<RouteMapConfigurationDocument> LoadActiveDraftAsync(
        CancellationToken cancellationToken = default)
    {
        var loaded = await _storage.LoadActiveAsync(cancellationToken);
        return loaded is null ? _mapper.CreateSeedDocument() : Prepare(loaded).Document;
    }

    public async Task<RouteMapConfigurationDocument> ImportDraftAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var loaded = await _storage.LoadAsync(path, cancellationToken);
        return Prepare(loaded).Document;
    }

    public async Task<RouteMapConfigurationOperationResult> ExportDraftAsync(
        string path,
        RouteMapConfigurationDocument document,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prepared = Prepare(document).Document;
            var validation = _validator.Validate(prepared);
            if (!validation.IsValid)
                return new RouteMapConfigurationOperationResult(false, validation.Errors);

            await _storage.SaveAsync(path, prepared, cancellationToken);
            return RouteMapConfigurationOperationResult.Success;
        }
        catch (Exception ex)
        {
            return new RouteMapConfigurationOperationResult(false, [], ex.Message);
        }
    }

    public void Dispose() => _definitionSubject.Dispose();

    private void Publish(RouteMapConfigurationDocument document, RouteMapDefinition definition)
    {
        CurrentDocument = _storage.Clone(document);
        CurrentDefinition = definition;
        _definitionSubject.OnNext(definition);
    }

    private RouteMapConfigurationMigrationResult Prepare(RouteMapConfigurationDocument document) =>
        _migrator.Migrate(_storage.Clone(document));

    private static string FormatErrors(IEnumerable<RouteMapConfigurationError> errors) =>
        string.Join("; ", errors.Take(5).Select(x => x.Message));
}
