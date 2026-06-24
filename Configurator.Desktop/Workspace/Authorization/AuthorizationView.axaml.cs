using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ReactiveUI;
using ReactiveUI.Avalonia;
using System.Reactive;
using System.Reactive.Disposables.Fluent;

namespace Configurator.Desktop.Workspace.Authorization;

public partial class AuthorizationView : ReactiveUserControl<AuthorizationViewModel>
{
    public AuthorizationView()
    {
        AvaloniaXamlLoader.Load(this);
        this.WhenActivated(disposables =>
        {
            if (ViewModel is null)
            {
                return;
            }

            ViewModel.ErrorInteraction.RegisterHandler(async interaction =>
            {
                var dialog = new Window { Title = "Error", Width = 300, Height = 150 };
                dialog.Content = new TextBlock
                {
                    Text = interaction.Input,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };

                if (TopLevel.GetTopLevel(this) is Window owner)
                {
                    await dialog.ShowDialog(owner);
                }

                interaction.SetOutput(Unit.Default);
            }).DisposeWith(disposables);
        });
    }
}
