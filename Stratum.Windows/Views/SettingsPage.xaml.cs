using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Stratum.Windows.Models;
using Stratum.Windows.Services;

namespace Stratum.Windows.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _loaded;

        public SettingsPage()
        {
            InitializeComponent();
            VersionText.Text = "Versão 1.0.0 — WinUI 3 / .NET 10";
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (!await AppServices.EnsureOpenAsync())
                return;

            SelectByTag(ThemeBox, AppServices.Settings.Theme);
            SelectByTag(BackdropBox, AppServices.Settings.Backdrop);
            SelectByTag(GroupingBox, AppServices.Settings.CodeGrouping);
            SelectByTag(ViewModeBox, AppServices.Settings.ViewMode);
            SelectByTag(RevealDurationBox, AppServices.Settings.RevealDurationSeconds.ToString());
            ShowUsernamesCheck.IsChecked = AppServices.Settings.ShowUsernames;
            ShowUncategorisedCheck.IsChecked = AppServices.Settings.ShowUncategorised;
            TapToCopyCheck.IsChecked = AppServices.Settings.TapToCopy;
            TapToRevealCheck.IsChecked = AppServices.Settings.TapToReveal;
            SkipToNextCheck.IsChecked = AppServices.Settings.SkipToNext;
            MinimizeToTrayCheck.IsChecked = AppServices.Settings.MinimizeToTray;
            SelectByTag(AutoLockBox, AppServices.Settings.AutoLockMinutes.ToString());
            RefreshPasswordState();
            await RefreshHelloCheckAsync();
            RevealDurationBox.IsEnabled = TapToRevealCheck.IsChecked == true;

            _loaded = true;
        }

        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            await SaveAsync();
            App.Current.ApplyTheme();
        }

        private async void BackdropBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded)
                return;

            App.MainWindow.NotifyActivity();

            if ((BackdropBox.SelectedItem as ComboBoxItem)?.Tag is string backdrop)
            {
                AppServices.Settings.Backdrop = backdrop;
                await AppServices.Settings.SaveAsync();
                App.MainWindow.ApplyBackdrop();
            }
        }

        private async System.Threading.Tasks.Task RefreshHelloCheckAsync()
        {
            try
            {
                var available = await HelloService.IsAvailableAsync();
                HelloCheck.IsEnabled = available && !string.IsNullOrEmpty(AppServices.SessionPassword);
                HelloCheck.IsChecked = AppServices.Settings.HelloEnabled && HelloService.HasStoredPassword();

                if (!available)
                    HelloCheck.Content = "Desbloquear com Windows Hello (indisponível neste PC)";
            }
            catch
            {
                HelloCheck.IsEnabled = false;
            }
        }

        private async void HelloCheck_Changed(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (HelloCheck.IsChecked == true)
            {
                if (string.IsNullOrEmpty(AppServices.SessionPassword))
                {
                    HelloCheck.IsChecked = false;
                    ShowStatus("Defina uma senha no banco de dados primeiro.", InfoBarSeverity.Warning);
                    return;
                }

                var stored = await HelloService.StorePasswordAsync(AppServices.SessionPassword,
                    "Confirme para ativar o desbloqueio com Windows Hello");

                if (!stored)
                {
                    HelloCheck.IsChecked = false;
                    ShowStatus("Windows Hello não confirmou.", InfoBarSeverity.Error);
                    return;
                }

                AppServices.Settings.HelloEnabled = true;
                await AppServices.Settings.SaveAsync();
                ShowStatus("Windows Hello ativado.", InfoBarSeverity.Success);
            }
            else
            {
                HelloService.Clear();
                AppServices.Settings.HelloEnabled = false;
                await AppServices.Settings.SaveAsync();
            }
        }

        private async void GithubButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenUrlAsync(AppLinks.WindowsRepoUrl);
        }

        private async void OriginalButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenUrlAsync(AppLinks.OriginalRepoUrl);
        }

        private async void CreatorLink_Click(Microsoft.UI.Xaml.Documents.Hyperlink sender,
            Microsoft.UI.Xaml.Documents.HyperlinkClickEventArgs args)
        {
            await OpenUrlAsync("https://github.com/jamiemh");
        }

        private async System.Threading.Tasks.Task OpenUrlAsync(string url)
        {
            try
            {
                await global::Windows.System.Launcher.LaunchUriAsync(new Uri(url));
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao abrir: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private void TapToRevealCheck_Changed(object sender, RoutedEventArgs e)
        {
            RevealDurationBox.IsEnabled = TapToRevealCheck.IsChecked == true;
        }

        private static void SelectByTag(ComboBox box, string tag)
        {
            foreach (var item in box.Items.OfType<ComboBoxItem>())
            {
                if ((item.Tag as string) == tag)
                {
                    box.SelectedItem = item;
                    return;
                }
            }

            box.SelectedIndex = 0;
        }

        private async System.Threading.Tasks.Task SaveAsync()
        {
            var settings = AppServices.Settings;
            settings.Theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "System";
            settings.Backdrop = (BackdropBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Mica";
            settings.CodeGrouping = (GroupingBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Halves";
            settings.ViewMode = (ViewModeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Default";
            settings.ShowUsernames = ShowUsernamesCheck.IsChecked == true;
            settings.ShowUncategorised = ShowUncategorisedCheck.IsChecked == true;
            settings.TapToCopy = TapToCopyCheck.IsChecked == true;
            settings.TapToReveal = TapToRevealCheck.IsChecked == true;
            settings.SkipToNext = SkipToNextCheck.IsChecked == true;
            settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;

            if (int.TryParse((RevealDurationBox.SelectedItem as ComboBoxItem)?.Tag as string, out var revealSecs))
                settings.RevealDurationSeconds = revealSecs;

            if (int.TryParse((AutoLockBox.SelectedItem as ComboBoxItem)?.Tag as string, out var minutes))
                settings.AutoLockMinutes = minutes;

            AuthenticatorRow.ShowUsernames = settings.ShowUsernames;
            AuthenticatorRow.GroupingMode = settings.CodeGrouping;
            AuthenticatorRow.TapToRevealEnabled = settings.TapToReveal;
            AuthenticatorRow.RevealDuration = TimeSpan.FromSeconds(settings.RevealDurationSeconds);
            AuthenticatorRow.SkipToNextEnabled = settings.SkipToNext;
            await settings.SaveAsync();
            App.MainWindow.NotifyActivity();
            App.MainWindow.RefreshTrayVisibility();
        }

        private void RefreshPasswordState()
        {
            DbPasswordState.Text = string.IsNullOrEmpty(AppServices.SessionPassword)
                ? "Banco de dados: sem senha."
                : "Banco de dados: protegido por senha.";
        }

        private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            var password = await UiHelpers.PromptPasswordAsync(XamlRoot, "Senha do banco de dados",
                "Digite a nova senha (ela protege o arquivo authenticator.db3):", true);

            if (password == null)
                return;
            if (password == UiHelpers.PromptPasswordMismatch)
            {
                ShowStatus("As senhas não conferem.", InfoBarSeverity.Error);
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                ShowStatus("Digite uma senha ou use “Remover senha”.", InfoBarSeverity.Error);
                return;
            }

            try
            {
                await AppServices.Database.SetPasswordAsync(AppServices.SessionPassword, password);
                AppServices.SessionPassword = password;
                HelloService.Clear();
                AppServices.Settings.HelloEnabled = false;
                await AppServices.Settings.SaveAsync();
                await RefreshHelloCheckAsync();
                RefreshPasswordState();
                ShowStatus("Senha atualizada. Reative o Windows Hello se desejar.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao alterar senha: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void RemovePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            if (string.IsNullOrEmpty(AppServices.SessionPassword))
            {
                ShowStatus("O banco já está sem senha.", InfoBarSeverity.Informational);
                return;
            }

            var ok = await UiHelpers.ConfirmAsync(XamlRoot, "Remover senha",
                "Remover a senha do banco de dados? Qualquer pessoa com acesso ao PC poderá ler seus segredos.",
                "Remover");
            if (!ok)
                return;

            try
            {
                await AppServices.Database.SetPasswordAsync(AppServices.SessionPassword, null);
                AppServices.SessionPassword = null;
                HelloService.Clear();
                AppServices.Settings.HelloEnabled = false;
                await AppServices.Settings.SaveAsync();
                await RefreshHelloCheckAsync();
                RefreshPasswordState();
                ShowStatus("Senha removida.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao remover senha: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void LockNowButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveAsync();
            await AppServices.LockAsync();
            App.MainWindow.Navigate(typeof(LockPage), "unlock");
        }

        private async void ResetCopyCountsButton_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            try
            {
                await AppServices.AuthenticatorService.ResetCopyCountsAsync();
                ShowStatus("Contadores zerados.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Falha: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusBar.Message = message;
            StatusBar.Severity = severity;
            StatusBar.IsOpen = true;
        }
    }
}
