using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Stratum.Core.Backup;
using Stratum.Core.Backup.Encryption;
using Stratum.Core.Converter;
using Stratum.Windows.Services;
using Windows.Storage;

namespace Stratum.Windows.Views
{
    public sealed partial class BackupPage : Page
    {
        private readonly List<(string Label, Func<BackupConverter> Create)> _sources = new();

        public BackupPage()
        {
            InitializeComponent();

            _sources.Add(("Stratum (.stratum)", null));
            _sources.Add(("Aegis (.json)", () => new AegisBackupConverter(AppServices.IconResolver, AppServices.CustomIconDecoder)));
            _sources.Add(("andOTP (.json)", () => new AndOtpBackupConverter(AppServices.IconResolver)));
            _sources.Add(("FreeOTP+ (.json)", () => new FreeOtpPlusBackupConverter(AppServices.IconResolver)));
            _sources.Add(("FreeOTP (.xml)", () => new FreeOtpBackupConverter(AppServices.IconResolver)));
            _sources.Add(("Google Authenticator (.txt)", () => new GoogleAuthenticatorBackupConverter(AppServices.IconResolver)));
            _sources.Add(("2FAS (.2fas)", () => new TwoFasBackupConverter(AppServices.IconResolver)));
            _sources.Add(("Bitwarden (.json)", () => new BitwardenBackupConverter(AppServices.IconResolver)));
            _sources.Add(("Ente (.txt/.json)", () => new EnteAuthBackupConverter(AppServices.IconResolver)));
            _sources.Add(("KeePass (.kdbx)", () => new KeePassBackupConverter(AppServices.IconResolver)));
            _sources.Add(("LastPass (.json)", () => new LastPassBackupConverter(AppServices.IconResolver)));
            _sources.Add(("Proton Authenticator (.json)", () => new ProtonAuthenticatorBackupConverter(AppServices.IconResolver)));
            _sources.Add(("TOTP Authenticator (.bin)", () => new TotpAuthenticatorBackupConverter(AppServices.IconResolver)));
            _sources.Add(("WinAuth (.zip)", () => new WinAuthBackupConverter(AppServices.IconResolver)));
            _sources.Add(("Authenticator Plus (.db)", () => new AuthenticatorPlusBackupConverter(AppServices.IconResolver)));
            _sources.Add(("Lista de URIs (.txt)", () => new UriListBackupConverter(AppServices.IconResolver)));
            _sources.Add(("HTML do Stratum (.html)", () => new HtmlBackupConverter(AppServices.IconResolver)));

            foreach (var (label, _) in _sources)
                ImportSourceBox.Items.Add(label);
            ImportSourceBox.SelectedIndex = 0;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (!await AppServices.EnsureOpenAsync())
                return;

            AutoBackupCheck.IsChecked = AppServices.Settings.AutoBackupEnabled;
            AutoBackupFolderBox.Text = AppServices.Settings.AutoBackupFolder;
            AutoRestoreCheck.IsChecked = AppServices.Settings.AutoRestoreEnabled;
            AutoRestoreFolderBox.Text = AppServices.Settings.AutoRestoreFolder;
        }

        private async void BackupEncrypted_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            var password = await UiHelpers.PromptPasswordAsync(XamlRoot, "Backup criptografado",
                "Digite uma senha para proteger o backup:", true);

            if (password == null)
                return;
            if (password == UiHelpers.PromptPasswordMismatch)
            {
                ShowStatus("As senhas não conferem.", InfoBarSeverity.Error);
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                ShowStatus("Digite uma senha.", InfoBarSeverity.Error);
                return;
            }

            try
            {
                var backup = await AppServices.BackupService.CreateBackupAsync();
                var data = await new StrongBackupEncryption().EncryptAsync(backup, password);
                await BackupFiles.SaveBytesAsync(App.MainWindow, $"stratum-backup-{DateTime.Now:yyyyMMdd-HHmmss}.stratum",
                    ("Stratum backup", new[] { ".stratum" }), data);
                await AppServices.Settings.MarkBackedUpAsync();
                ShowStatus("Backup criptografado salvo.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Falha no backup: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void BackupPlain_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            var ok = await UiHelpers.ConfirmAsync(XamlRoot, "Backup sem senha",
                "O arquivo conterá todos os seus segredos SEM criptografia. Continuar?", "Salvar mesmo assim");
            if (!ok)
                return;

            try
            {
                var backup = await AppServices.BackupService.CreateBackupAsync();
                var data = await new NoBackupEncryption().EncryptAsync(backup, null);
                await BackupFiles.SaveBytesAsync(App.MainWindow, $"stratum-backup-{DateTime.Now:yyyyMMdd-HHmmss}.stratum",
                    ("Stratum backup", new[] { ".stratum" }), data);
                await AppServices.Settings.MarkBackedUpAsync();
                ShowStatus("Backup salvo.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Falha no backup: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void ExportHtml_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            try
            {
                var html = await AppServices.BackupService.CreateHtmlBackupAsync();
                await BackupFiles.SaveTextAsync(App.MainWindow, $"stratum-backup-{DateTime.Now:yyyyMMdd-HHmmss}.html",
                    ("Página HTML", new[] { ".html" }), html.ToString());
                ShowStatus("HTML exportado. Atenção: ele contém seus segredos.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao exportar: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void ExportUriList_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            try
            {
                var list = await AppServices.BackupService.CreateUriListBackupAsync();
                await BackupFiles.SaveTextAsync(App.MainWindow, $"stratum-uris-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                    ("Texto", new[] { ".txt" }), list.ToString());
                ShowStatus("Lista de URIs exportada. Atenção: ela contém seus segredos.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao exportar: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            var index = ImportSourceBox.SelectedIndex;
            if (index < 0)
                return;

            var (label, create) = _sources[index];
            var file = await UiHelpers.PickOpenFileAsync(App.MainWindow, "Escolher arquivo",
                ("Todos", new[] { "*" }));

            if (file == null)
                return;

            var password = string.IsNullOrEmpty(ImportPasswordBox.Password) ? null : ImportPasswordBox.Password;

            try
            {
                byte[] data = await File.ReadAllBytesAsync(file.Path);
                int added, updated, failed = 0;

                if (create == null)
                {
                    var backup = await BackupFiles.DecryptStratumAsync(data, password);
                    RestoreResult result;

                    if (UpdateExistingCheck.IsChecked == true)
                        result = await AppServices.RestoreService.RestoreAndUpdateAsync(backup);
                    else
                        result = await AppServices.RestoreService.RestoreAsync(backup);

                    added = result.AddedAuthenticatorCount;
                    updated = result.UpdatedAuthenticatorCount;
                }
                else
                {
                    var converter = create();
                    ConversionResult conversion;
                    RestoreResult result;

                    try
                    {
                        conversion = await converter.ConvertAsync(data, password);
                    }
                    catch (BackupPasswordException)
                    {
                        ShowStatus("Senha incorreta para este arquivo.", InfoBarSeverity.Error);
                        return;
                    }

                    failed = conversion.Failures?.Count ?? 0;

                    if (UpdateExistingCheck.IsChecked == true)
                        result = await AppServices.RestoreService.RestoreAndUpdateAsync(conversion.Backup);
                    else
                        result = await AppServices.RestoreService.RestoreAsync(conversion.Backup);

                    added = result.AddedAuthenticatorCount;
                    updated = result.UpdatedAuthenticatorCount;
                }

                await AppServices.CustomIconService.CullUnusedAsync();
                await AppServices.Settings.MarkDirtyAsync();

                var msg = $"{added} adicionada(s), {updated} atualizada(s).";
                if (failed > 0)
                    msg += $" {failed} falharam.";

                ShowStatus($"Importação de {label}: " + msg, InfoBarSeverity.Success);
                ImportPasswordBox.Password = "";
            }
            catch (BackupPasswordException)
            {
                ShowStatus("Senha incorreta.", InfoBarSeverity.Error);
            }
            catch (InvalidOperationException)
            {
                ShowStatus("Banco de dados bloqueado. Desbloqueie e tente de novo.", InfoBarSeverity.Error);
                AppServices.GoToLock();
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao importar: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void AutoBackupFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folder = await UiHelpers.PickFolderAsync(App.MainWindow, "Escolher pasta");
            if (folder != null)
                AutoBackupFolderBox.Text = folder.Path;
        }

        private async void SaveAutoBackupButton_Click(object sender, RoutedEventArgs e)
        {
            AppServices.Settings.AutoBackupEnabled = AutoBackupCheck.IsChecked == true;
            AppServices.Settings.AutoBackupFolder = AutoBackupFolderBox.Text.Trim();
            await AppServices.Settings.SaveAsync();
            ShowStatus("Preferências salvas.", InfoBarSeverity.Success);
        }

        private async void AutoRestoreFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folder = await UiHelpers.PickFolderAsync(App.MainWindow, "Escolher pasta");
            if (folder != null)
                AutoRestoreFolderBox.Text = folder.Path;
        }

        private async void SaveAutoRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            AppServices.Settings.AutoRestoreEnabled = AutoRestoreCheck.IsChecked == true;
            AppServices.Settings.AutoRestoreFolder = AutoRestoreFolderBox.Text.Trim();
            await AppServices.Settings.SaveAsync();
            ShowStatus("Preferências salvas.", InfoBarSeverity.Success);
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusBar.Message = message;
            StatusBar.Severity = severity;
            StatusBar.IsOpen = true;
        }
    }
}

