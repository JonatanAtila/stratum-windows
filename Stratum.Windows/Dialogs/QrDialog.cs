// Stratum.Windows - QR code viewer (pure WinUI 3, built in code).

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stratum.Core.Entity;
using Stratum.Windows.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Stratum.Windows.Dialogs
{
    public static class QrDialog
    {
        public static async Task ShowAsync(XamlRoot root, Authenticator auth)
        {
            string uri;
            try
            {
                uri = auth.GetUri();
            }
            catch (Exception ex)
            {
                await UiHelpers.ShowMessageAsync(root, "QR Code", "Não foi possível gerar a URI: " + ex.Message);
                return;
            }

            var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
            var image = new Image { Width = 280, Height = 280, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(image);
            panel.Children.Add(new TextBox
            {
                Text = uri,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 120
            });

            var dialog = new ContentDialog
            {
                Title = auth.Issuer,
                Content = new ScrollViewer { Content = panel },
                PrimaryButtonText = "Copiar URI",
                SecondaryButtonText = "Copiar segredo",
                CloseButtonText = "Fechar",
                XamlRoot = root
            };

            try
            {
                image.Source = await UiHelpers.QrCodeImageAsync(uri);
            }
            catch
            {
                // URI text is still useful
            }

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                var package = new DataPackage();
                package.SetText(uri);
                Clipboard.SetContent(package);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                var package = new DataPackage();
                package.SetText(auth.Secret);
                Clipboard.SetContent(package);
            }
        }
    }
}
