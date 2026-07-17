using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services
{
    /// <summary>
    /// Интерфейс для работы с конфигурацией приложения.
    /// </summary>
    public interface IAppConfigService
    {
        /// <summary>
        /// Получить секцию конфигурации и преобразовать в объект.
        /// </summary>
        T GetSection<T>(string sectionName) where T : class, new();
        /// <summary>
        /// Получить строковое значение по ключу.
        /// </summary>
        string GetValue(string key);
        /// <summary>
        /// Сохранить отдельную секцию конфигурации в основной JSON-файл приложения.
        /// </summary>
        Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default);
        /// <summary>
        /// Сохраняет несколько секций одним обновлением конфигурационного файла.
        /// Реализация по умолчанию оставлена для тестовых doubles; production-реализация
        /// должна записывать все переданные секции атомарно.
        /// </summary>
        async Task SaveSectionsAsync(
            IReadOnlyDictionary<string, object> sections,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(sections);

            foreach (var section in sections)
            {
                await SaveSectionAsync(section.Key, section.Value, ct);
            }
        }
        /// <summary>
        /// Сохранить пользовательские настройки в отдельный файл.
        /// </summary>
        void SaveUserSettings(UserSettings settings);
        /// <summary>
        /// Загрузить пользовательские настройки из файла, если он есть, иначе из конфигурации.
        /// </summary>
        UserSettings LoadUserSettings();
    }

    /// <summary>
    /// Класс для хранения пользовательских настроек приложения.
    /// </summary>
    public class UserSettings
    {
        /// <summary>Ширина окна.</summary>
        public double Width { get; set; } = 1200;
        /// <summary>Высота окна.</summary>
        public double Height { get; set; } = 800;
        /// <summary>Полноэкранный режим.</summary>
        public bool IsFullScreen { get; set; } = false;
        public ModbusOptions? Modbus { get; set; }
        public OpcUaOptions? OpcUa { get; set; }
        // Здесь можно добавить любые другие пользовательские параметры
    }
}
