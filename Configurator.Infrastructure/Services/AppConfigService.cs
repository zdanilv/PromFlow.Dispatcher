using Microsoft.Extensions.Configuration;
using Configurator.Application.Services;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.IO;

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
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private const string UserSettingsFile = "user_settings.json";

        public AppConfigService(IConfiguration configuration)
            : this(configuration, ApplicationConfigPaths.SharedAppSettingsPath)
        {
        }

        public AppConfigService(IConfiguration configuration, string configFilePath)
        {
            _configuration = configuration;
            _configFilePath = configFilePath;
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
                if (File.Exists(_configFilePath))
                {
                    var json = await File.ReadAllTextAsync(_configFilePath, ct);
                    if (!string.IsNullOrWhiteSpace(json))
                        root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
                }

                root[sectionName] = JsonSerializer.SerializeToNode(value, JsonOptions);
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
                if (File.Exists(_configFilePath))
                {
                    var json = await File.ReadAllTextAsync(_configFilePath, ct);
                    if (!string.IsNullOrWhiteSpace(json))
                        root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
                }

                foreach (var section in sections)
                {
                    root[section.Key] = JsonSerializer.SerializeToNode(section.Value, JsonOptions);
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
            File.WriteAllText(UserSettingsFile, json);
#if DEBUG
            Debug.WriteLine($"[AppConfigService] UserSettings сохранены: {json}");
#endif
        }

        /// <summary>
        /// Загрузить пользовательские настройки из файла, если он есть, иначе из конфигурации.
        /// </summary>
        public UserSettings LoadUserSettings()
        {
            if (File.Exists(UserSettingsFile))
            {
                var json = File.ReadAllText(UserSettingsFile);
#if DEBUG
                Debug.WriteLine($"[AppConfigService] UserSettings загружены из user_settings.json: {json}");
#endif
                var settings = JsonSerializer.Deserialize<UserSettings>(json);
                if (settings != null) return settings;
            }
            return GetSection<UserSettings>("WindowSettings"); // пока секция WindowSettings, но можно расширять
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
