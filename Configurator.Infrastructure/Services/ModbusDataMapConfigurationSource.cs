using Configurator.Application.Services.Modbus.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Configurator.Infrastructure.Services;

/// <summary>
/// Supplies install defaults, except that a user DataMap replaces the entire default array.
/// </summary>
public sealed class ModbusDataMapConfigurationSource(IConfiguration defaults, IConfiguration user) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new DefaultsProvider(new ChainedConfigurationProvider(new ChainedConfigurationSource { Configuration = defaults }), user);

    private sealed class DefaultsProvider(IConfigurationProvider defaultsProvider, IConfiguration user)
        : IConfigurationProvider
    {
        private const string DataMapKey = nameof(ModbusOptions.DataMap);
        private const string DataMapPath = ModbusOptions.SectionName + ":" + DataMapKey;

        public bool TryGet(string key, out string? value)
        {
            if (!IsOverridden(key))
                return defaultsProvider.TryGet(key, out value);

            value = null;
            return false;
        }

        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath) =>
            IsOverridden(parentPath) ? earlierKeys : defaultsProvider.GetChildKeys(earlierKeys, parentPath);

        public void Set(string key, string? value) => defaultsProvider.Set(key, value);
        public void Load() => defaultsProvider.Load();
        public IChangeToken GetReloadToken() => defaultsProvider.GetReloadToken();

        private bool IsOverridden(string? path) =>
            path is not null &&
            (path.Equals(DataMapPath, StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith(DataMapPath + ":", StringComparison.OrdinalIgnoreCase)) &&
            user.GetSection(ModbusOptions.SectionName).GetChildren()
                .Any(section => section.Key.Equals(DataMapKey, StringComparison.OrdinalIgnoreCase));
    }
}
