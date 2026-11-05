using Microsoft.Extensions.Configuration;
using Configurator.Application.Services;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.IO;
using Configurator.Application.Services.Modbus.Configuration;

namespace Configurator.Infrastructure.Services
{
    /// <summary>
    /// Сервис для работы с конфигурацией приложения через DI.
    /// Позволяет читать параметры из appsettings.json и сохранять пользовательские параметры (например, настройки окна).
    /// </summary>
    public class AppConfigService : IAppConfigService
    {
        private readonly IConfiguration _configuration;
        private readonly string _configFilePath;
        private readonly string _userSettingsFilePath;
        private readonly IReadOnlyList<string> _legacyUserSettingsFilePaths;
        private readonly SemaphoreSlim _saveGate = new(1, 1);

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
        {
            _configuration = configuration;
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

                SetSection(root, sectionName, replacement);
                await File.WriteAllTextAsync(_configFilePath, root.ToJsonString(JsonOptions), ct);

                if (_configuration is IConfigurationRoot configurationRoot)
                {
                    configurationRoot.Reload();
                }
            }
            finally
            {
                _saveGate.Release();
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

                await File.WriteAllTextAsync(_configFilePath, root.ToJsonString(JsonOptions), ct);
                if (_configuration is IConfigurationRoot configurationRoot)
                {
                    configurationRoot.Reload();
                }
            }
            finally
            {
                _saveGate.Release();
            }
        }

        /// <summary>
        /// Сохранить пользовательские настройки в отдельный файл.
        /// </summary>
        public void SaveUserSettings(UserSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            var directory = Path.GetDirectoryName(_userSettingsFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_userSettingsFilePath, json);
#if DEBUG
            Debug.WriteLine($"[AppConfigService] UserSettings сохранены в {_userSettingsFilePath}: {json}");
#endif
        }

        /// <summary>
        /// Загрузить пользовательские настройки из файла, если он есть, иначе из конфигурации.
        /// </summary>
        public UserSettings LoadUserSettings()
        {
            var settings = LoadUserSettingsFromFile(_userSettingsFilePath);
            if (settings is not null)
            {
                return settings;
            }

            foreach (var legacyPath in _legacyUserSettingsFilePaths)
            {
                settings = LoadUserSettingsFromFile(legacyPath);
                if (settings is null)
                {
                    continue;
                }

                SaveUserSettings(settings);
#if DEBUG
                Debug.WriteLine($"[AppConfigService] UserSettings перенесены из {legacyPath} в {_userSettingsFilePath}.");
#endif
                return settings;
            }

            return GetSection<UserSettings>("WindowSettings"); // пока секция WindowSettings, но можно расширять
        }

        private static UserSettings? LoadUserSettingsFromFile(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
#if DEBUG
            Debug.WriteLine($"[AppConfigService] UserSettings загружены из {path}: {json}");
#endif
            return JsonSerializer.Deserialize<UserSettings>(json);
        }

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
    }
}
