namespace Configurator.Infrastructure.Services.Autostart;

public interface IWindowsRunRegistry
{
    void SetValue(string name, string command);

    void DeleteValue(string name);
}
