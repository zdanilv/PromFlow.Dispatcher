using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Signals;

namespace Configurator.Application.Services.Configuration;

public sealed class AuthorizedAppConfigService : IAppConfigService
{
    private static readonly HashSet<string> ProtectedSections = new(StringComparer.Ordinal)
    {
        ModbusOptions.SectionName,
        ModbusOptions.DemoSectionName,
        RouteMapRuntimeOptions.SectionName,
    };

    private readonly IAppConfigService _inner;
    private readonly IAccessDecisionService _accessDecisionService;

    public AuthorizedAppConfigService(
        IAppConfigService inner,
        IAccessDecisionService accessDecisionService)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _accessDecisionService = accessDecisionService ?? throw new ArgumentNullException(nameof(accessDecisionService));
    }

    public T GetSection<T>(string sectionName)
        where T : class, new()
        => _inner.GetSection<T>(sectionName);

    public string GetValue(string key) => _inner.GetValue(key);

    public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default)
    {
        if (ProtectedSections.Contains(sectionName))
        {
            var decision = _accessDecisionService.Authorize(
                new AccessRequirement(Permission.ConfigureModbus));
            if (!decision.Succeeded)
            {
                throw new UnauthorizedAccessException("Configure Modbus permission is required.");
            }
        }

        return _inner.SaveSectionAsync(sectionName, value, ct);
    }

    public void SaveUserSettings(UserSettings settings) => _inner.SaveUserSettings(settings);

    public UserSettings LoadUserSettings() => _inner.LoadUserSettings();
}
