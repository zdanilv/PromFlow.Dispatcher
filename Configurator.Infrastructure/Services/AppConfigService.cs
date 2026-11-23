using Microsoft.Extensions.Configuration;
using Configurator.Application.Services;
using Configurator.Application.Services.History;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.IO;
using Configurator.Application.Services.Modbus.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Configurator.Infrastructure.Services
{
    /// <summary>
    /// Сервис для работы с конфигурацией приложения через DI.
    /// Позволяет читать параметры из appsettings.json и сохранять пользовательские параметры (например, настройки окна).
    /// </summary>
    public class AppConfigService : IAppConfigService
    {
        private readonly IConfiguration _configuration;
        private readonly IConfigurationRoot? _writableConfiguration;
        private readonly string _configFilePath;
        private readonly string _userSettingsFilePath;
        private readonly IReadOnlyList<string> _legacyUserSettingsFilePaths;
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private readonly object _userSettingsGate = new();
        private byte[]? _invalidUserSettingsBytes;
        internal ILogger<AppConfigService> Logger { get; set; } = NullLogger<AppConfigService>.Instance;

        // Deterministic failure/cancellation injection immediately before the commit point.
        internal Action<string, string>? BeforeSettingsReplace { get; set; }

        public AppConfigService(IConfiguration configuration)
            : this(
                configuration,
                ApplicationConfigPaths.SharedAppSettingsPath,
                ApplicationConfigPaths.UserSettingsPath)
        {
        }

        public AppConfigService(IConfiguration configuration, string configFilePath)
            : this(configuration, configFilePath, ApplicationConfigPaths.UserSettingsPath)
        {
        }

        public AppConfigService(
            IConfiguration configuration,
            string configFilePath,
            string userSettingsFilePath,
            IEnumerable<string>? legacyUserSettingsFilePaths = null)
            : this(configuration, configFilePath, userSettingsFilePath, legacyUserSettingsFilePaths, writableConfiguration: null)
        {
        }

        public AppConfigService(
            IConfiguration configuration,
            string configFilePath,
            string userSettingsFilePath,
            IEnumerable<string>? legacyUserSettingsFilePaths,
            IConfigurationRoot? writableConfiguration)
        {
            _configuration = configuration;
            _writableConfiguration = writableConfiguration;
            _configFilePath = configFilePath;
            _userSettingsFilePath = userSettingsFilePath;
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var currentPath = Path.GetFullPath(_userSettingsFilePath);
            _legacyUserSettingsFilePaths = (legacyUserSettingsFilePaths ?? GetDefaultLegacyUserSettingsPaths())
                .Select(Path.GetFullPath)
                .Where(path => !pathComparer.Equals(path, currentPath))
                .Distinct(pathComparer)
                .ToArray();
        }

        /// <summary>
        /// Получить секцию конфигурации и преобразовать в объект.
        /// </summary>
        public T GetSection<T>(string sectionName) where T : class, new()
        {
            var section = new T();
            _configuration.GetSection(sectionName).Bind(section);
            return section;
        }

        /// <summary>
        /// Получить строковое значение по ключу.
        /// </summary>
        public string GetValue(string key)
        {
            return _configuration[key] ?? string.Empty;
        }

        /// <summary>
        /// Сохранить одну секцию в appsettings.json, не перезаписывая остальные разделы конфигурации.
        /// </summary>
        public async Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sectionName))
            {
                throw new ArgumentException("Section name must not be empty.", nameof(sectionName));
            }

            await _saveGate.WaitAsync(ct);
            try
            {
                var directory = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var root = new JsonObject();
                string? originalJson = null;
                if (File.Exists(_configFilePath))
                {
                    originalJson = await File.ReadAllTextAsync(_configFilePath, ct);
                    if (!string.IsNullOrWhiteSpace(originalJson))
                        root = JsonNode.Parse(originalJson)?.AsObject() ?? new JsonObject();
                }

                var replacement = JsonSerializer.SerializeToNode(value, JsonOptions);
                if (sectionName == ModbusOptions.SectionName && value is ModbusOptions &&
                    GetProperty(root, sectionName) is JsonObject oldModbus && replacement is JsonObject newModbus)
                {
                    var needsBackup = RemovesObsoleteModeMapping(oldModbus, newModbus);
                    // A saved profile may contain fields this application version does not model.
                    // Keep them on every save, including repeated reconciliation.
                    PreserveUnknownModbusFields(oldModbus, newModbus);
                    if (needsBackup && originalJson is not null)
                    {
                        var backupPath = _configFilePath + ".before-mode-cleanup." + Guid.NewGuid().ToString("N") + ".bak";
                        await File.WriteAllTextAsync(backupPath, originalJson, ct);
                    }
                }

                if (sectionName == HistoryArchiveOptions.SectionName && value is HistoryArchiveOptions &&
                    GetProperty(root, sectionName) is JsonObject oldHistory && replacement is JsonObject newHistory)
                {
                    PreserveMissingProperties(oldHistory, newHistory);
                }

                SetSection(root, sectionName, replacement);
                await WriteSettingsAtomicallyAsync(root, ct);
            }
            finally
            {
                _saveGate.Release();
            }
        }

        public async Task<bool> TryRemoveModbusDataPointAsync(ModbusDataPointOptions expected, CancellationToken ct = default)
        {
            await _saveGate.WaitAsync(ct);
            try
            {
                var root = await ReadSettingsAsync(ct);
                var modbus = GetProperty(root, ModbusOptions.SectionName) as JsonObject ?? new JsonObject();
                var map = GetProperty(modbus, nameof(ModbusOptions.DataMap)) as JsonArray
                    ?? (JsonArray)JsonSerializer.SerializeToNode(
                        GetSection<ModbusOptions>(ModbusOptions.SectionName).DataMap, JsonOptions)!;

                var matching = map.OfType<JsonObject>().Where(point =>
                    string.Equals(GetProperty(point, nameof(ModbusDataPointOptions.Name))?.GetValue<string>(),
                        expected.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matching.Length == 0 || !matching.Any(point =>
                    point.Deserialize<ModbusDataPointOptions>(PointJsonOptions) is { } current &&
                    current.HasSameMapping(expected)))
                {
                    return false;
                }
                // Names identify points in the runtime, including when an invalid map has duplicate names.
                foreach (var point in matching)
                    map.Remove(point);

                if (map.Parent is null)
                    SetSection(modbus, nameof(ModbusOptions.DataMap), map);
                if (modbus.Parent is null)
                    SetSection(root, ModbusOptions.SectionName, modbus);

                await WriteSettingsAtomicallyAsync(root, ct);
                return true;
            }
            finally
            {
                _saveGate.Release();
            }
        }

        public Task SaveModbusAlarmsAsync(ModbusOptions options, CancellationToken ct = default) =>
            SaveModbusArrayAsync(nameof(ModbusOptions.AlarmMap), options.AlarmMap, ct, "Id");

        public Task SaveModbusAddressLabelsAsync(ModbusOptions options, CancellationToken ct = default) =>
            SaveModbusArrayAsync(nameof(ModbusOptions.AddressLabels), options.AddressLabels, ct, "Area", "Address", "BitIndex");

        private async Task SaveModbusArrayAsync<T>(string key, T items, CancellationToken ct, params string[] identity)
        {
            await _saveGate.WaitAsync(ct);
            try
            {
                var root = await ReadSettingsAsync(ct);
                var modbus = GetProperty(root, ModbusOptions.SectionName) as JsonObject ?? new JsonObject();
                var replacement = new JsonObject { [key] = JsonSerializer.SerializeToNode(items, JsonOptions) };
                PreserveArrayEntries(modbus, replacement, key, identity);
                SetSection(modbus, key, replacement[key]?.DeepClone());
                if (modbus.Parent is null)
                    SetSection(root, ModbusOptions.SectionName, modbus);
                await WriteSettingsAtomicallyAsync(root, ct);
            }
            finally
            {
                _saveGate.Release();
            }
        }

        private async Task<JsonObject> ReadSettingsAsync(CancellationToken ct)
        {
            var json = File.Exists(_configFilePath) ? await File.ReadAllTextAsync(_configFilePath, ct) : null;
            return string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json)!.AsObject();
        }

        private async Task WriteSettingsAtomicallyAsync(JsonObject root, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_configFilePath))!);
            var temporaryPath = _configFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var moved = false;
            try
            {
                await File.WriteAllTextAsync(temporaryPath, root.ToJsonString(JsonOptions), ct);
                BeforeSettingsReplace?.Invoke(temporaryPath, _configFilePath);
                ct.ThrowIfCancellationRequested();
                File.Move(temporaryPath, _configFilePath, overwrite: true);
                moved = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogWarning(ex, "Failed to save configuration atomically at {Path}", _configFilePath);
                throw;
            }
            finally
            {
                if (!moved)
                    CleanupTemporaryFile(temporaryPath);
            }

            ReloadConfiguration();
        }

        private void CleanupTemporaryFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogWarning(ex, "Failed to remove temporary settings file {Path}", path);
            }
        }

        private static bool RemovesObsoleteModeMapping(JsonObject oldModbus, JsonObject newModbus)
        {
            if (GetProperty(oldModbus, "DataMap") is not JsonArray oldMap ||
                GetProperty(newModbus, "DataMap") is not JsonArray newMap)
                return false;

            return oldMap.OfType<JsonObject>()
                .Select(point => GetProperty(point, "Name")?.GetValue<string>())
                .Where(IsObsoleteModeSignal)
                .Any(name => !newMap.OfType<JsonObject>().Any(point =>
                    string.Equals(GetProperty(point, "Name")?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool IsObsoleteModeSignal(string? name) =>
            string.Equals(name, "system.mode.automatic", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "system.mode.manual", StringComparison.OrdinalIgnoreCase);

        private static void PreserveUnknownModbusFields(JsonObject oldModbus, JsonObject newModbus)
        {
            PreserveMissingProperties(oldModbus, newModbus);
            PreserveArrayEntries(oldModbus, newModbus, "DataMap", "Name");
            PreserveArrayEntries(oldModbus, newModbus, "AlarmMap", "Id");
            PreserveArrayEntries(oldModbus, newModbus, "AddressLabels", "Area", "Address", "BitIndex");
        }

        private static void PreserveArrayEntries(JsonObject source, JsonObject target, string arrayName, params string[] identity)
        {
            if (GetProperty(source, arrayName) is not JsonArray oldArray ||
                GetProperty(target, arrayName) is not JsonArray nextArray)
                return;

            foreach (var next in nextArray.OfType<JsonObject>())
            {
                var old = oldArray.OfType<JsonObject>().FirstOrDefault(point =>
                    identity.All(key => string.Equals(GetProperty(point, key)?.ToJsonString(),
                        GetProperty(next, key)?.ToJsonString(), StringComparison.OrdinalIgnoreCase)));
                if (old is not null)
                    PreserveMissingProperties(old, next);
            }
        }

        private static void PreserveMissingProperties(JsonObject source, JsonObject target)
        {
            foreach (var property in source)
            {
                var existing = GetProperty(target, property.Key);
                if (!target.Any(item => string.Equals(item.Key, property.Key, StringComparison.OrdinalIgnoreCase)))
                    target[property.Key] = property.Value?.DeepClone();
                else if (property.Value is JsonObject sourceChild && existing is JsonObject targetChild)
                    PreserveMissingProperties(sourceChild, targetChild);
            }
        }

        private static JsonNode? GetProperty(JsonObject source, string name) =>
            source.FirstOrDefault(property => string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

        private static void SetSection(JsonObject root, string sectionName, JsonNode? replacement)
        {
            var existingName = root.Select(property => property.Key)
                .FirstOrDefault(name => string.Equals(name, sectionName, StringComparison.OrdinalIgnoreCase));
            root[existingName ?? sectionName] = replacement;
        }

        /// <inheritdoc />
        public async Task SaveSectionsAsync(
            IReadOnlyDictionary<string, object> sections,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(sections);
            if (sections.Count == 0)
            {
                return;
            }

            if (sections.Keys.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("Section name must not be empty.", nameof(sections));
            }

            await _saveGate.WaitAsync(ct);
            try
            {
                var directory = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var root = new JsonObject();
                string? originalJson = null;
                if (File.Exists(_configFilePath))
                {
                    originalJson = await File.ReadAllTextAsync(_configFilePath, ct);
                    if (!string.IsNullOrWhiteSpace(originalJson))
                        root = JsonNode.Parse(originalJson)?.AsObject() ?? new JsonObject();
                }

                foreach (var section in sections)
                {
                    var replacement = JsonSerializer.SerializeToNode(section.Value, JsonOptions);
                    if (section.Key == ModbusOptions.SectionName && section.Value is ModbusOptions &&
                        GetProperty(root, section.Key) is JsonObject oldModbus && replacement is JsonObject newModbus)
                    {
                        var needsBackup = RemovesObsoleteModeMapping(oldModbus, newModbus);
                        PreserveUnknownModbusFields(oldModbus, newModbus);
                        if (needsBackup && originalJson is not null)
                        {
                            var backupPath = _configFilePath + ".before-mode-cleanup." + Guid.NewGuid().ToString("N") + ".bak";
                            await File.WriteAllTextAsync(backupPath, originalJson, ct);
                        }
                    }

                    SetSection(root, section.Key, replacement);
                }

                await WriteSettingsAtomicallyAsync(root, ct);
            }
            finally
            {
                _saveGate.Release();
            }
        }

        private void ReloadConfiguration() => (_writableConfiguration ?? _configuration as IConfigurationRoot)?.Reload();

        /// <summary>
        /// Сохранить пользовательские настройки в отдельный файл.
        /// </summary>
        public void SaveUserSettings(UserSettings settings) => SaveUserSettings(settings, legacyRoot: null);

        private void SaveUserSettings(UserSettings settings, JsonObject? legacyRoot)
        {
            lock (_userSettingsGate)
            {
                string? temporaryPath = null;
                try
                {
                    ValidateWindowSettings(settings);
                    JsonObject? previous = null;
                    try
                    {
                        LoadUserSettingsFromFile(_userSettingsFilePath, out previous);
                        previous ??= legacyRoot;
                    }
                    catch (JsonException ex)
                    {
                        Logger.LogWarning(ex, "Preserving invalid window settings before replacement: {Path}", _userSettingsFilePath);
                    }

                    var replacement = (JsonObject)JsonSerializer.SerializeToNode(settings, UserSettingsJsonOptions)!;
                    if (previous is not null)
                    {
                        PreserveMissingProperties(previous, replacement);
                        if (previous[nameof(UserSettings.Modbus)] is JsonObject oldModbus &&
                            replacement[nameof(UserSettings.Modbus)] is JsonObject newModbus)
                            PreserveUnknownModbusFields(oldModbus, newModbus);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_userSettingsFilePath))!);
                    temporaryPath = _userSettingsFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(temporaryPath, replacement.ToJsonString(UserSettingsJsonOptions));
                    if (_invalidUserSettingsBytes is not null)
                    {
                        var backup = _userSettingsFilePath + ".corrupt." + Guid.NewGuid().ToString("N") + ".bak";
                        File.WriteAllBytes(backup, _invalidUserSettingsBytes);
                        Logger.LogWarning("Invalid window settings preserved at {Path}", backup);
                        _invalidUserSettingsBytes = null;
                    }
                    BeforeSettingsReplace?.Invoke(temporaryPath, _userSettingsFilePath);
                    File.Move(temporaryPath, _userSettingsFilePath, overwrite: true);
                }
                catch (Exception ex) when (IsWindowSettingsFailure(ex))
                {
                    Logger.LogWarning(ex, "Failed to save window settings at {Path}", _userSettingsFilePath);
                    throw;
                }
                finally
                {
                    if (temporaryPath is not null)
                        CleanupTemporaryFile(temporaryPath);
                }
            }
        }

        /// <summary>
        /// Загрузить пользовательские настройки из файла, если он есть, иначе из конфигурации.
        /// </summary>
        public UserSettings LoadUserSettings()
        {
            lock (_userSettingsGate)
            {
                UserSettings? settings;
                try
                {
                    settings = LoadUserSettingsFromFile(_userSettingsFilePath, out _);
                    if (settings is not null)
                        return settings;
                }
                catch (Exception ex) when (IsWindowSettingsFailure(ex))
                {
                    Logger.LogWarning(ex, "Failed to load window settings at {Path}; using WindowSettings defaults", _userSettingsFilePath);
                    return GetWindowSettingsDefaults();
                }

                foreach (var legacyPath in _legacyUserSettingsFilePaths)
                {
                    JsonObject? legacyRoot;
                    try
                    {
                        settings = LoadUserSettingsFromFile(legacyPath, out legacyRoot);
                        if (settings is null)
                            continue;
                    }
                    catch (Exception ex) when (IsWindowSettingsFailure(ex))
                    {
                        Logger.LogWarning(ex, "Failed to load legacy window settings at {Path}", legacyPath);
                        continue;
                    }

                    try
                    {
                        SaveUserSettings(settings, legacyRoot);
                    }
                    catch (Exception ex) when (IsWindowSettingsFailure(ex))
                    {
                        Logger.LogWarning(ex, "Could not migrate window settings from {Path}; using legacy settings for this session", legacyPath);
                    }
                    return settings;
                }

                return GetWindowSettingsDefaults();
            }
        }

        private UserSettings GetWindowSettingsDefaults()
        {
            var settings = GetSection<UserSettings>("WindowSettings");
            if (!HasValidWindowSize(settings))
            {
                Logger.LogWarning("Invalid WindowSettings default dimensions; using built-in window defaults");
                return new UserSettings();
            }
            return settings;
        }

        private UserSettings? LoadUserSettingsFromFile(string path, out JsonObject? root)
        {
            root = null;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }

            try
            {
                // StreamReader keeps the existing BOM-aware text-file contract; retain raw bytes for diagnostics.
                using var reader = new StreamReader(new MemoryStream(bytes));
                root = JsonNode.Parse(reader.ReadToEnd(), documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject
                    ?? throw new JsonException("Window settings must contain a JSON object.");
                var settings = root.Deserialize<UserSettings>()
                    ?? throw new JsonException("Window settings must not be null.");
                ValidateWindowSettings(settings);
                return settings;
            }
            catch (JsonException)
            {
                root = null;
                if (path == _userSettingsFilePath)
                    _invalidUserSettingsBytes = bytes;
                throw;
            }
        }

        private static bool HasValidWindowSize(UserSettings settings) =>
            double.IsFinite(settings.Width) && settings.Width > 0 &&
            double.IsFinite(settings.Height) && settings.Height > 0;

        private static void ValidateWindowSettings(UserSettings settings)
        {
            if (!HasValidWindowSize(settings))
                throw new JsonException("Window dimensions must be finite positive numbers.");
        }

        private static bool IsWindowSettingsFailure(Exception ex) =>
            ex is IOException or UnauthorizedAccessException or JsonException;

        private static readonly JsonSerializerOptions UserSettingsJsonOptions = new() { WriteIndented = true };

        private static IEnumerable<string> GetDefaultLegacyUserSettingsPaths()
        {
            // Старые версии читали относительный user_settings.json из текущего каталога.
            yield return Path.Combine(Environment.CurrentDirectory, ApplicationConfigPaths.UserSettingsFileName);

            // Этот вариант сохраняет совместимость с portable-публикациями, запущенными рядом с файлом настроек.
            yield return Path.Combine(AppContext.BaseDirectory, ApplicationConfigPaths.UserSettingsFileName);
        }

        /*
         * Как добавлять новые параметры:
         * 1. Добавьте нужную секцию/ключ в appsettings.json.
         * 2. Создайте класс-модель для секции (например, WindowSettings).
         * 3. Для чтения: используйте GetSection<T>("SectionName").
         * 4. Для сохранения: реализуйте SaveSection аналогично WindowSettings или расширьте метод.
         * 5. Для динамического обновления: вызывайте SaveSection при изменении параметров.
         * 6. Для чтения на лету: используйте Load... методы или GetSection/GetValue.
         */

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly JsonSerializerOptions PointJsonOptions = new(JsonOptions)
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
    }
}
