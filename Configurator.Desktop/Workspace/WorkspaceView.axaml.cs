using Configurator.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Workspace;

public partial class WorkspaceView : ReactiveUserControl<WorkspaceViewModel>
{
    public WorkspaceView()
    {
        InitializeComponent();
    }

    public WorkspaceView(
        IAppConfigService configService,
        IOptions<ApplicationOptions> applicationOptions,
        ILogger<WindowStartupSettingsViewModel> logger) : this()
    {
        StartupSettingsPanel.DataContext = new WindowStartupSettingsViewModel(configService, applicationOptions, logger);
    }
}
