// Stratum.Windows - one-time welcome dialog after fresh setup.

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Stratum.Windows.Dialogs
{
    public static class WelcomeDialog
    {
        public static async Task ShowAsync(XamlRoot root)
        {
            var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
            panel.Children.Add(new TextBlock
            {
                Text = "Bem-vindo ao Stratum para Windows!",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 16
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Adicione contas manualmente, colando uma URI, lendo um QR de imagem ou importando um backup " +
                       "(.stratum, Aegis, Google Authenticator e outros) em Backup e importação.\n\n" +
                       "Toque num card para copiar o código. Use categorias para organizar e ative o backup automático " +
                       "para nunca perder o acesso.",
                TextWrapping = TextWrapping.Wrap
            });

            var dialog = new ContentDialog
            {
                Title = "Começando",
                Content = new ScrollViewer { Content = panel },
                PrimaryButtonText = "Começar",
                XamlRoot = root
            };

            await dialog.ShowAsync();
        }
    }
}
