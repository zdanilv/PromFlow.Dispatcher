using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Modbus.Configuration;

/// <summary>
/// Привязывает потребителя настроек к одной именованной секции конфигурации.
/// </summary>
internal sealed class NamedOptionsMonitor<TOptions>(
    IOptionsMonitor<TOptions> inner,
    string name) : IOptionsMonitor<TOptions>
    where TOptions : class
{
    private readonly string _name = name;

    /// <summary>
    /// Возвращает текущие настройки для имени, переданного адаптеру при создании.
    /// </summary>
    public TOptions CurrentValue => inner.Get(_name);

    /// <summary>
    /// Возвращает текущий экземпляр настроек.
    /// </summary>
    public TOptions Get(string? requestedName)
        => inner.Get(string.IsNullOrWhiteSpace(requestedName) ? _name : requestedName);

    /// <summary>
    /// Регистрирует обработчик изменений и возвращает объект для отмены подписки.
    /// </summary>
    public IDisposable? OnChange(Action<TOptions, string?> listener)
    {
        return inner.OnChange((options, changedName) =>
        {
            if (string.Equals(changedName, _name, StringComparison.Ordinal))
            {
                listener(options, changedName);
            }
        });
    }
}
