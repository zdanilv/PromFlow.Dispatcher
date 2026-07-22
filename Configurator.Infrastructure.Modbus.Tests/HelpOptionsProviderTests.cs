using Configurator.Application.Services;
using Configurator.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class HelpOptionsProviderTests
{
    [Fact]
    public void User_help_section_replaces_installed_contacts_without_array_merging()
    {
        var installed = BuildConfiguration(
            [
                ("Help:Contacts:0:Label", "Телефон"),
                ("Help:Contacts:0:Value", "8 953 448 31 16"),
                ("Help:Contacts:1:Label", "Сайт"),
                ("Help:Contacts:1:Value", "example.com")
            ]);
        var user = BuildConfiguration(
            [
                ("Help:Contacts:0:Label", "Диспетчер"),
                ("Help:Contacts:0:Value", "+7 000 000 00 00")
            ]);

        var options = new HelpOptionsProvider(installed, user).GetCurrent();

        var contact = Assert.Single(options.Contacts);
        Assert.Equal("Диспетчер", contact.Label);
        Assert.Equal("+7 000 000 00 00", contact.Value);
    }

    [Fact]
    public void Falls_back_to_installed_help_when_user_section_is_absent()
    {
        var installed = BuildConfiguration(
            [
                ("Help:Contacts:0:Label", "Телефон"),
                ("Help:Contacts:0:Value", "8 953 448 31 16")
            ]);

        var options = new HelpOptionsProvider(installed, BuildConfiguration([])).GetCurrent();

        var contact = Assert.Single(options.Contacts);
        Assert.Equal("Телефон", contact.Label);
    }

    private static IConfigurationRoot BuildConfiguration(IEnumerable<(string Key, string Value)> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();
}
