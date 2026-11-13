using System.Reactive.Subjects;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Configuration;

public sealed record RouteMapConfigurationOperationResult(
    bool IsSuccess,
    IReadOnlyList<RouteMapConfigurationError> Errors,
    string? ErrorMessage = null)
{
    public static RouteMapConfigurationOperationResult Success { get; } = new(true, []);

    internal RouteMapConfigurationDocument? AppliedDocument { get; init; }
}

public sealed class RouteMapConfigurationManager : IDisposable
{
    private readonly RouteMapConfigurationStorage _storage;
    private readonly RouteMapConfigurationMapper _mapper;
    private readonly RouteMapConfigurationValidator _validator;
    private readonly RouteMapConfigurationMigrator _migrator;
    private readonly BehaviorSubject<RouteMapDefinition> _definitionSubject;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

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
                {
                    storage.BackupActive();
                    storage.SaveActive(migration.Document);
                }

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

    public RouteMapConfigurationDocument CreateDraft() => CreateDraft(out _);

    internal RouteMapConfigurationDocument CreateDraft(out RouteMapConfigurationDocument source)
    {
        source = CurrentDocument;
        return _storage.Clone(source);
    }

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

    public RouteMapConfigurationOperationResult Apply(RouteMapConfigurationDocument document) =>
        Apply(document, expectedDocument: null);

    internal RouteMapConfigurationOperationResult Apply(
        RouteMapConfigurationDocument document,
        RouteMapConfigurationDocument? expectedDocument)
    {
        if (!_writeGate.Wait(0))
            return new(false, [], "Конфигурация RouteMap сохраняется. Повторите применение после завершения записи.");

        try
        {
            if (HasConflict(expectedDocument))
                return DraftConflict();

            var prepared = Prepare(document).Document;
            var validation = _validator.Validate(prepared);
            if (!validation.IsValid)
                return new RouteMapConfigurationOperationResult(false, validation.Errors);

            var definition = _mapper.ToDefinition(prepared);
            return Publish(prepared, definition);
        }
        catch (Exception ex)
        {
            return new RouteMapConfigurationOperationResult(false, [], ex.Message);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task<RouteMapConfigurationOperationResult> SaveAndApplyAsync(
        RouteMapConfigurationDocument document,
        CancellationToken cancellationToken = default) =>
        SaveAndApplyAsync(document, expectedDocument: null, cancellationToken);

    internal async Task<RouteMapConfigurationOperationResult> SaveAndApplyAsync(
        RouteMapConfigurationDocument document,
        RouteMapConfigurationDocument? expectedDocument,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _writeGate.WaitAsync(cancellationToken);
            try
            {
                return HasConflict(expectedDocument)
                    ? DraftConflict()
                    : await SaveAndApplyCoreAsync(document, cancellationToken);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (Exception ex)
        {
            return new(false, [], ex.Message);
        }
    }

    internal async Task<RouteMapDefinition> UpdateAsync(
        Action<RouteMapConfigurationDocument> update,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var draft = CreateDraft();
            update(draft);
            var result = await SaveAndApplyCoreAsync(draft, cancellationToken);
            if (!result.IsSuccess)
            {
                var details = result.ErrorMessage ?? string.Join("; ", result.Errors.Select(error => error.Message));
                throw new InvalidOperationException($"Не удалось сохранить RouteMap: {details}");
            }

            return CurrentDefinition;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<RouteMapConfigurationOperationResult> SaveAndApplyCoreAsync(
        RouteMapConfigurationDocument document,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var migration = Prepare(document);
            var prepared = migration.Document;
            var validation = _validator.Validate(prepared);
            if (!validation.IsValid)
                return new RouteMapConfigurationOperationResult(false, validation.Errors);

            var definition = _mapper.ToDefinition(prepared);
            if (migration.WasMigrated && File.Exists(_storage.ActiveFilePath))
                _storage.BackupActive();
            await _storage.SaveActiveAsync(prepared, cancellationToken);
            LastLoadError = null;
            return Publish(prepared, definition);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new RouteMapConfigurationOperationResult(false, [], ex.Message);
        }
    }

    public async Task<RouteMapConfigurationDocument> LoadActiveDraftAsync(
        CancellationToken cancellationToken = default)
    {
        var loaded = await _storage.LoadActiveAsync(cancellationToken);
        return loaded is null ? CreateDefaultDraft() : Prepare(loaded).Document;
    }

    internal async Task<(RouteMapConfigurationDocument Draft, RouteMapConfigurationDocument Source)>
        ReloadEditorDraftAsync()
    {
        await _writeGate.WaitAsync();
        try
        {
            return (await LoadActiveDraftAsync(), CurrentDocument);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    internal RouteMapConfigurationDocument CreateDefaultDraft() => _mapper.CreateSeedDocument();

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
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(ActiveFilePath), pathComparison))
                return new(false, [], "Активный профиль RouteMap изменяется через «Сохранить». Выберите другой путь для экспорта.");

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

    public void Dispose()
    {
        _definitionSubject.Dispose();
        _writeGate.Dispose();
    }

    private RouteMapConfigurationOperationResult Publish(
        RouteMapConfigurationDocument document,
        RouteMapDefinition definition)
    {
        // Prepare already detached this document from the caller's draft.
        CurrentDocument = document;
        CurrentDefinition = definition;
        _definitionSubject.OnNext(definition);
        return RouteMapConfigurationOperationResult.Success with { AppliedDocument = document };
    }

    private bool HasConflict(RouteMapConfigurationDocument? expectedDocument) =>
        expectedDocument is not null && !ReferenceEquals(expectedDocument, CurrentDocument);

    private static RouteMapConfigurationOperationResult DraftConflict() =>
        new(false, [], "Конфигурация RouteMap изменилась. Загрузите актуальный черновик и повторите правки.");

    private RouteMapConfigurationMigrationResult Prepare(RouteMapConfigurationDocument document) =>
        _migrator.Migrate(_storage.Clone(document));

    private static string FormatErrors(IEnumerable<RouteMapConfigurationError> errors) =>
        string.Join("; ", errors.Take(5).Select(x => x.Message));
}
