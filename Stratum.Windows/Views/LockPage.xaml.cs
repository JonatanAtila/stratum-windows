using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Stratum.Windows.Services;

namespace Stratum.Windows.Views
{
    public sealed partial class LockPage : Page
    {
        private bool _isSetup;

        public LockPage()
        {
            InitializeComponent();
            _ = LoadLogoAsync();
        }

        private async System.Threading.Tasks.Task LoadLogoAsync()
        {
            try
            {
                var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
                if (System.IO.File.Exists(path))
                    LogoImage.Source = await UiHelpers.BitmapImageFromBytesAsync(await System.IO.File.ReadAllBytesAsync(path));
            }
            catch
            {
                // segue só com o nome
            }
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isSetup = (e.Parameter as string) == "setup";

            if (_isSetup)
            {
                SubtitleText.Text = "Crie uma senha para proteger seus códigos neste PC. Você pode continuar sem senha, se preferir.";
                ConfirmBox.Visibility = Visibility.Visible;
                NoPasswordButton.Visibility = Visibility.Visible;
                ResetButton.Visibility = Visibility.Collapsed;
                UnlockButton.Content = "Criar e entrar";
            }
            else
            {
                SubtitleText.Text = "O banco de dados está bloqueado. Digite a senha para continuar. Se você nunca definiu uma senha, deixe em branco e clique em Desbloquear.";
                ConfirmBox.Visibility = Visibility.Collapsed;
                NoPasswordButton.Visibility = Visibility.Collapsed;
                ResetButton.Visibility = Visibility.Visible;
                UnlockButton.Content = "Desbloquear";
            }

            PasswordBox.Focus(FocusState.Programmatic);
            RefreshHelloButton();
        }

        private void RefreshHelloButton()
        {
            try
            {
                HelloButton.Visibility =
                    !_isSetup && AppServices.Settings.HelloEnabled && HelloService.HasStoredPassword()
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
            catch
            {
                HelloButton.Visibility = Visibility.Collapsed;
            }
        }

        private async void HelloButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorBar.IsOpen = false;

            var password = await HelloService.UnlockAsync("Desbloquear o Stratum");
            if (string.IsNullOrEmpty(password))
            {
                ShowError("Não foi possível desbloquear com Windows Hello.");
                return;
            }

            try
            {
                UnlockButton.IsEnabled = false;
                await AppServices.Database.OpenAsync(password);
                AppServices.SessionPassword = password;
                App.MainWindow.Navigate(typeof(AuthenticatorsPage));
            }
            catch (Exception)
            {
                ShowError("Senha armazenada inválida. Use a senha do banco.");
            }
            finally
            {
                UnlockButton.IsEnabled = true;
            }
        }

        private async System.Threading.Tasks.Task MaybeShowWelcomeAsync()
        {
            try
            {
                if (AppServices.Settings.FirstRunDone)
                    return;

                AppServices.Settings.FirstRunDone = true;
                await AppServices.Settings.SaveAsync();
                await Dialogs.WelcomeDialog.ShowAsync(XamlRoot);
            }
            catch
            {
                // boas-vindas são opcionais
            }
        }

        private async void UnlockButton_Click(object sender, RoutedEventArgs e)
        {
            await TryUnlockAsync();
        }

        private async void PasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == global::Windows.System.VirtualKey.Enter)
                await TryUnlockAsync();
        }

        private async void NoPasswordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ErrorBar.IsOpen = false;
                await AppServices.Database.OpenAsync(null);
                AppServices.SessionPassword = null;
                App.MainWindow.Navigate(typeof(AuthenticatorsPage));
                await MaybeShowWelcomeAsync();
            }
            catch (Exception ex)
            {
                ShowError("Não foi possível abrir o banco de dados: " + ex.Message);
            }
        }

        private async System.Threading.Tasks.Task TryUnlockAsync()
        {
            var password = PasswordBox.Password;

            if (_isSetup)
            {
                if (!string.IsNullOrEmpty(password) && password != ConfirmBox.Password)
                {
                    ShowError("As senhas não conferem.");
                    return;
                }
            }
            else if (string.IsNullOrEmpty(password))
            {
                // Banco sem senha: tenta abrir sem chave (fluxo opcional de senha).
                try
                {
                    ErrorBar.IsOpen = false;
                    await AppServices.Database.OpenAsync(null);
                    AppServices.SessionPassword = null;
                    App.MainWindow.Navigate(typeof(AuthenticatorsPage));
                    return;
                }
                catch
                {
                    ShowError("Digite a senha.");
                    return;
                }
            }

            try
            {
                ErrorBar.IsOpen = false;
                UnlockButton.IsEnabled = false;
                await AppServices.Database.OpenAsync(string.IsNullOrEmpty(password) ? null : password);
                AppServices.SessionPassword = string.IsNullOrEmpty(password) ? null : password;
                PasswordBox.Password = "";
                ConfirmBox.Password = "";
                App.MainWindow.Navigate(typeof(AuthenticatorsPage));

                if (_isSetup)
                    await MaybeShowWelcomeAsync();
            }
            catch (Exception)
            {
                ShowError(_isSetup
                    ? "Não foi possível criar o banco de dados."
                    : "Senha incorreta ou banco de dados corrompido.");
            }
            finally
            {
                UnlockButton.IsEnabled = true;
            }
        }

        private void ShowError(string message)
        {
            ErrorBar.Message = message;
            ErrorBar.IsOpen = true;
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var ok = await UiHelpers.ConfirmAsync(XamlRoot, "Apagar tudo e recomeçar",
                "Isso apaga PERMANENTEMENTE o banco de dados deste PC, incluindo todas as contas e categorias. " +
                "Só faça isso se esqueceu a senha ou se quer começar do zero. Deseja continuar?",
                "Apagar tudo");

            if (!ok)
                return;

            try
            {
                await AppServices.Database.CloseAsync();
                AppServices.SessionPassword = null;

                var dbPath = Stratum.Windows.Services.Database.DbPath;
                var candidates = new[]
                {
                    dbPath,
                    dbPath.Replace(".db3", ".db3-shm"),
                    dbPath.Replace(".db3", ".db3-wal"),
                    dbPath + ".backup",
                    dbPath + ".temp"
                };

                foreach (var file in candidates)
                {
                    try
                    {
                        if (System.IO.File.Exists(file))
                            System.IO.File.Delete(file);
                    }
                    catch
                    {
                        // best-effort
                    }
                }

                PasswordBox.Password = "";
                App.MainWindow.Navigate(typeof(LockPage), "setup");
            }
            catch (Exception ex)
            {
                ShowError("Não foi possível apagar: " + ex.Message);
            }
        }
    }
}
