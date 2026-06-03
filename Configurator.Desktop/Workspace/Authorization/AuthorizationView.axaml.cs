using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Configurator.Desktop.Workspace.Authorization;
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
            ViewModel.ErrorInteraction.RegisterHandler(async interaction =>
            {
                // Создаём окно сообщения
                var dialog = new Window { Title = "Ошибка", Width = 300, Height = 150 };
                dialog.Content = new TextBlock 
                { 
                    Text = interaction.Input, 
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, 
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
                await dialog.ShowDialog(TopLevel.GetTopLevel(this) as Window);
                interaction.SetOutput(Unit.Default);
            }).DisposeWith(disposables);
        });
    }
}