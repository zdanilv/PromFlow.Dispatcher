using Configurator.Application.Services.Archiving;

namespace Configurator.Infrastructure.Persistence.Common;

public interface IAppDataPathProvider
{
    string GetArchiveBaseDirectory(ArchiveOptions options);

    string GetArchiveExportDirectory(ArchiveOptions options);
}
