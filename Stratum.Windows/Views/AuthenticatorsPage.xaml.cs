using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SimpleBase;
using Stratum.Core.Entity;
using Stratum.Windows.Dialogs;
using Stratum.Windows.Models;
using Stratum.Windows.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Stratum.Windows.Views
{
    public sealed partial class AuthenticatorsPage : Page
    {
        private readonly ObservableCollection<AuthenticatorRow> _rows = new();
        private List<AuthenticatorRow> _allRows = new();
        private Dictionary<string, List<string>> _bindingsBySecret = new();
        private List<Category> _categories = new();
        private DispatcherTimer _timer;
        private bool _loaded;

        public AuthenticatorsPage()
        {
            InitializeComponent();
            AuthList.ItemsSource = _rows;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (!await AppServices.EnsureOpenAsync())
                return;

            AuthenticatorRow.ShowUsernames = AppServices.Settings.ShowUsernames;
            AuthenticatorRow.GroupingMode = AppServices.Settings.CodeGrouping;
            AuthenticatorRow.TapToRevealEnabled = AppServices.Settings.TapToReveal;
            AuthenticatorRow.RevealDuration = TimeSpan.FromSeconds(AppServices.Settings.RevealDurationSeconds);
            AuthenticatorRow.SkipToNextEnabled = AppServices.Settings.SkipToNext;

            ApplyViewMode();
            await LoadAsync();
            StartTimer();
            await CheckAutoRestoreAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _timer?.Stop();
        }

        private void ApplyViewMode()
        {
            var mode = AppServices.Settings.ViewMode;
            if (mode != "Compact" && mode != "Tile")
                mode = "Default";

            if (Resources.TryGetValue("Auth" + mode + "Template", out var template))
                AuthList.ItemTemplate = (DataTemplate)template;

            if (Resources.TryGetValue("Auth" + mode + "Panel", out var panel))
                AuthList.ItemsPanel = (ItemsPanelTemplate)panel;
        }

        private async System.Threading.Tasks.Task CheckAutoRestoreAsync()
        {
            try
            {
                var settings = AppServices.Settings;
                if (!settings.AutoRestoreEnabled || string.IsNullOrWhiteSpace(settings.AutoRestoreFolder))
                    return;

                if (!System.IO.Directory.Exists(settings.AutoRestoreFolder))
                    return;

                var newest = System.IO.Directory.GetFiles(settings.AutoRestoreFolder, "*.stratum")
                    .Select(f => new System.IO.FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (newest == null)
                    return;

                DateTime.TryParse(settings.LastAutoRestoreUtc, out var last);
                if (newest.LastWriteTimeUtc <= last)
                    return;

                var ok = await UiHelpers.ConfirmAsync(XamlRoot, "Restauração automática",
                    $"Há um backup mais novo ({newest.Name}, {newest.LastWriteTime}). Restaurar agora (atualiza os existentes)?",
                    "Restaurar");
                if (!ok)
                    return;

                var data = await System.IO.File.ReadAllBytesAsync(newest.FullName);
                string password = null;

                if (BackupFiles.LooksEncrypted(data))
                {
                    password = await UiHelpers.PromptPasswordAsync(XamlRoot, "Backup criptografado",
                        "Digite a senha do backup:");
                    if (password == null)
                        return;
                }

                var backup = await BackupFiles.DecryptStratumAsync(data, password);
                var result = await AppServices.RestoreService.RestoreAndUpdateAsync(backup);
                await AppServices.CustomIconService.CullUnusedAsync();

                settings.LastAutoRestoreUtc = DateTime.UtcNow.ToString("o");
                await settings.SaveAsync();
                await ReloadAfterChangeAsync();

                ShowStatus($"{result.AddedAuthenticatorCount} adicionada(s), {result.UpdatedAuthenticatorCount} atualizada(s).",
                    InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Falha na restauração automática: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private void StartTimer()
        {
            _timer?.Stop();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += (s, e) =>
            {
                foreach (var row in _allRows)
                    row.Tick();
            };
            _timer.Start();

            foreach (var row in _allRows)
                row.Tick();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            _loaded = false;

            try
            {
                var auths = await AppServices.AuthenticatorRepository.GetAllAsync();
                var bindings = await AppServices.AuthenticatorCategoryRepository.GetAllAsync();
                _categories = await AppServices.CategoryRepository.GetAllAsync();
                var customIcons = await AppServices.CustomIconRepository.GetAllAsync();
                var iconById = customIcons.ToDictionary(i => CustomIcon.Prefix + i.Id);

                _allRows = new List<AuthenticatorRow>();
                _bindingsBySecret = new Dictionary<string, List<string>>();
                var dark = UiHelpers.IsDarkTheme();

                foreach (var b in bindings)
                {
                    if (!_bindingsBySecret.TryGetValue(b.AuthenticatorSecret, out var list))
                    {
                        list = new List<string>();
                        _bindingsBySecret[b.AuthenticatorSecret] = list;
                    }

                    list.Add(b.CategoryId);
                }

                var nameById = _categories.ToDictionary(c => c.Id, c => c.Name);

                foreach (var auth in auths)
                {
                    var row = new AuthenticatorRow(auth);

                    if (_bindingsBySecret.TryGetValue(auth.Secret, out var ids))
                        row.CategoryNames = string.Join(", ", ids.Where(id => nameById.ContainsKey(id)).Select(id => nameById[id]));

                    if (!string.IsNullOrEmpty(auth.Icon) && auth.Icon[0] == CustomIcon.Prefix &&
                        iconById.TryGetValue(auth.Icon, out var icon))
                    {
                        try
                        {
                            row.CustomIcon = await UiHelpers.BitmapImageFromBytesAsync(icon.Data);
                        }
                        catch
                        {
                            // keep fallbacks below
                        }
                    }

                    if (row.CustomIcon == null)
                    {
                        var key = auth.Icon ?? AppServices.IconResolver.FindServiceKeyByName(auth.Issuer);
                        row.CustomIcon = await BrandIcons.LoadAsync(key, dark);
                    }

                    _allRows.Add(row);
                }

                CategoryFilter.Items.Clear();
                CategoryFilter.Items.Add(new ComboBoxItem { Content = "Todas", Tag = "" });

                if (AppServices.Settings.ShowUncategorised)
                    CategoryFilter.Items.Add(new ComboBoxItem { Content = "Sem categoria", Tag = "__none__" });

                foreach (var cat in _categories.OrderBy(c => c.Name))
                    CategoryFilter.Items.Add(new ComboBoxItem { Content = cat.Name, Tag = cat.Id });
                CategoryFilter.SelectedIndex = 0;

                foreach (var item in SortBox.Items.OfType<ComboBoxItem>())
                {
                    if ((item.Tag as string) == AppServices.Settings.SortMode)
                    {
                        SortBox.SelectedItem = item;
                        break;
                    }
                }

                if (SortBox.SelectedItem == null)
                    SortBox.SelectedIndex = 0;

                ApplyFilter();
            }
            catch (Exception ex)
            {
                ShowStatus("Erro ao carregar: " + ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                _loaded = true;
            }
        }

        private void ApplyFilter()
        {
            var query = (SearchBox?.Text ?? "").Trim().ToLowerInvariant();
            var catId = (CategoryFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            var sort = (SortBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "IssuerAsc";

            IEnumerable<AuthenticatorRow> rows = _allRows;

            if (!string.IsNullOrEmpty(query))
                rows = rows.Where(r => (r.Issuer ?? "").ToLowerInvariant().Contains(query) ||
                                       (r.Username ?? "").ToLowerInvariant().Contains(query));

            if (!string.IsNullOrEmpty(catId))
            {
                if (catId == "__none__")
                    rows = rows.Where(r => !_bindingsBySecret.TryGetValue(r.Authenticator.Secret, out var noneIds) || noneIds.Count == 0);
                else
                    rows = rows.Where(r => _bindingsBySecret.TryGetValue(r.Authenticator.Secret, out var ids) && ids.Contains(catId));
            }

            rows = sort switch
            {
                "IssuerDesc" => rows.OrderByDescending(r => r.Issuer).ThenBy(r => r.Username),
                "CopyCount" => rows.OrderByDescending(r => r.Authenticator.CopyCount).ThenBy(r => r.Issuer),
                "CopyCountAsc" => rows.OrderBy(r => r.Authenticator.CopyCount).ThenBy(r => r.Issuer),
                "Custom" => rows.OrderBy(r => r.Authenticator.Ranking).ThenBy(r => r.Issuer),
                _ => rows.OrderBy(r => r.Issuer).ThenBy(r => r.Username)
            };

            _rows.Clear();
            foreach (var row in rows)
                _rows.Add(row);

            foreach (var row in _rows)
                row.Tick();

            UpdateReorderMode(sort);
            UpdateBackupReminder();
            UpdateEmptyState();
        }

        private void UpdateReorderMode(string sort)
        {
            var custom = sort == "Custom";
            AuthList.CanDragItems = custom;
            AuthList.CanReorderItems = custom;
            AuthList.AllowDrop = custom;
        }

        private void UpdateBackupReminder()
        {
            BackupReminderBar.IsOpen = AppServices.Settings.HasUnbackedChanges && _allRows.Count > 0;
        }

        private void UpdateEmptyState()
        {
            EmptyState.Visibility = _rows.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void BackupReminderButton_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();
            App.MainWindow.Navigate(typeof(BackupPage));
        }

        private async void AuthList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            if (args.DropResult != DataPackageOperation.Move)
                return;

            App.MainWindow.NotifyActivity();

            try
            {
                var index = 0;
                foreach (var row in _rows)
                {
                    row.Authenticator.Ranking = index++;
                    await AppServices.AuthenticatorService.UpdateAsync(row.Authenticator);
                }

                await AppServices.Settings.MarkDirtyAsync();
                ShowStatus("Ordem salva.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao salvar ordem: " + ex.Message, InfoBarSeverity.Error);
                await LoadAsync();
            }
        }
        private void Filter_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loaded) ApplyFilter();
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loaded) ApplyFilter();
        }

        private async void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded)
                return;

            if ((SortBox.SelectedItem as ComboBoxItem)?.Tag is string sort)
            {
                AppServices.Settings.SortMode = sort;
                await AppServices.Settings.SaveAsync();
            }

            ApplyFilter();
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not AuthenticatorRow row)
                return;

            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            await CopyRowAsync(row);
        }

        private async void Card_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            // Toques vindos de elementos interativos do card (botão ⋮)
            // servem só para abrir o menu — não copiam o código.
            var el = e.OriginalSource as DependencyObject;
            while (el != null)
            {
                if (el is Button)
                    return;
                el = VisualTreeHelper.GetParent(el);
            }

            if ((sender as FrameworkElement)?.Tag is not AuthenticatorRow row)
                return;

            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            var settings = AppServices.Settings;

            if (settings.TapToReveal)
                row.Reveal();

            if (settings.TapToCopy)
                await CopyRowAsync(row);
        }

        private async System.Threading.Tasks.Task CopyRowAsync(AuthenticatorRow row)
        {
            if (row.Code == "------" || row.Code == "erro")
                row.Tick();

            var plain = row.Code.Replace(" ", "");

            try
            {
                var package = new DataPackage();
                package.SetText(plain);
                Clipboard.SetContent(package);

                await AppServices.AuthenticatorService.IncrementCopyCountAsync(row.Authenticator);
                ShowStatus($"Código de {row.Issuer} copiado.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao copiar: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not AuthenticatorRow row)
                return;

            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            try
            {
                await AppServices.AuthenticatorService.IncrementCounterAsync(row.Authenticator);
                row.RefreshHotpCode();
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao avançar contador: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void QrButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not AuthenticatorRow row)
                return;

            App.MainWindow.NotifyActivity();
            await QrDialog.ShowAsync(XamlRoot, row.Authenticator);
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not AuthenticatorRow row)
                return;

            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            var changed = await AuthDialog.ShowAsync(XamlRoot, row.Authenticator);
            if (changed)
            {
                await ReloadAfterChangeAsync();
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is not AuthenticatorRow row)
                return;

            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            var ok = await UiHelpers.ConfirmAsync(XamlRoot, "Excluir",
                $"Excluir \"{row.Issuer}\" permanentemente?", "Excluir");
            if (!ok)
                return;

            try
            {
                await AppServices.AuthenticatorService.DeleteWithCategoryBindingsAsync(row.Authenticator);
                await AppServices.CustomIconService.CullUnusedAsync();
                await ReloadAfterChangeAsync();
                ShowStatus("Excluído.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao excluir: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();
        }

        private async void AddManual_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            var created = await AuthDialog.ShowAsync(XamlRoot, null);
            if (created)
            {
                await ReloadAfterChangeAsync();
            }
        }

        private async void AddUri_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
            panel.Children.Add(new TextBlock
            {
                Text = "Cole uma URI otpauth://, motp:// ou otpauth-migration://",
                TextWrapping = TextWrapping.Wrap
            });
            var box = new TextBox { PlaceholderText = "otpauth://totp/...", AcceptsReturn = true, MinHeight = 80, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(box);

            var dialog = new ContentDialog
            {
                Title = "Adicionar por URI",
                Content = panel,
                PrimaryButtonText = "Adicionar",
                CloseButtonText = "Cancelar",
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            var uri = box.Text.Trim();
            if (string.IsNullOrEmpty(uri))
                return;

            await ProcessUriAsync(uri);
        }

        private async void AddQrImage_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            try
            {
                var file = await UiHelpers.PickOpenFileAsync(App.MainWindow, "Escolher imagem com QR",
                    ("Imagens", new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }));

                if (file == null)
                    return;

                string uri;
                try
                {
                    uri = await UiHelpers.DecodeQrFromImageAsync(file.Path);
                }
                catch (Exception ex)
                {
                    ShowStatus("Não foi possível ler a imagem: " + ex.Message, InfoBarSeverity.Error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(uri))
                {
                    ShowStatus("Nenhum QR Code encontrado na imagem.", InfoBarSeverity.Warning);
                    return;
                }

                await ProcessUriAsync(uri.Trim());
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao ler QR: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async System.Threading.Tasks.Task ProcessUriAsync(string uri)
        {
            try
            {
                if (uri.StartsWith("otpauth-migration://"))
                {
                    var migration = Stratum.Core.UriParser.ParseOtpAuthMigrationUri(uri);
                    var added = 0;

                    foreach (var payload in migration.Authenticators ?? new List<Stratum.Core.OtpAuthMigration.MigrationAuthenticator>())
                    {
                        try
                        {
                            var secret = Base32.Rfc4648.Encode(payload.Secret ?? Array.Empty<byte>());
                            var isHotp = payload.Type == Stratum.Core.OtpAuthMigration.Type.Hotp;
                            var label = string.IsNullOrEmpty(payload.Issuer)
                                ? payload.Username ?? ""
                                : string.IsNullOrEmpty(payload.Username)
                                    ? payload.Issuer
                                    : $"{payload.Issuer}:{payload.Username}";
                            var algo = payload.Algorithm switch
                            {
                                Stratum.Core.OtpAuthMigration.Algorithm.Sha256 => "&algorithm=SHA256",
                                Stratum.Core.OtpAuthMigration.Algorithm.Sha512 => "&algorithm=SHA512",
                                _ => ""
                            };
                            var counter = isHotp ? $"&counter={payload.Counter}" : "";
                            var stdUri = $"otpauth://{(isHotp ? "hotp" : "totp")}/{Uri.EscapeDataString(label)}?secret={secret}&issuer={Uri.EscapeDataString(payload.Issuer ?? "")}{algo}{counter}";
                            var result = Stratum.Core.UriParser.ParseStandardUri(stdUri, AppServices.IconResolver);
                            await AppServices.AuthenticatorService.AddAsync(result.Authenticator);
                            added++;
                        }
                        catch
                        {
                            // skip invalid entries
                        }
                    }

                    await ReloadAfterChangeAsync();
                    ShowStatus($"{added} conta(s) importada(s).", InfoBarSeverity.Success);
                    return;
                }

                var parsed = Stratum.Core.UriParser.ParseStandardUri(uri, AppServices.IconResolver);
                var created = await AuthDialog.ShowAsync(XamlRoot, parsed.Authenticator, parsed.PinLength);
                if (created)
                {
                    await ReloadAfterChangeAsync();
                }
            }
            catch (Exception ex)
            {
                ShowStatus("URI inválida: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            await LoadAsync();
        }

        private async void LockButton_Click(object sender, RoutedEventArgs e)
        {
            await AppServices.LockAsync();
            App.MainWindow.Navigate(typeof(LockPage), "unlock");
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusBar.Message = message;
            StatusBar.Severity = severity;
            StatusBar.IsOpen = true;
        }

        private async System.Threading.Tasks.Task ReloadAfterChangeAsync()
        {
            await AppServices.Settings.MarkDirtyAsync();
            await LoadAsync();
            await AutoBackupAsync();
        }

        internal async System.Threading.Tasks.Task AutoBackupAsync()        {
            var settings = AppServices.Settings;
            if (!settings.AutoBackupEnabled || string.IsNullOrWhiteSpace(settings.AutoBackupFolder))
                return;

            try
            {
                var backup = await AppServices.BackupService.CreateBackupAsync();
                byte[] data;

                if (!string.IsNullOrEmpty(AppServices.SessionPassword))
                    data = await new Stratum.Core.Backup.Encryption.StrongBackupEncryption().EncryptAsync(backup, AppServices.SessionPassword);
                else
                    data = await new Stratum.Core.Backup.Encryption.NoBackupEncryption().EncryptAsync(backup, null);

                var name = $"stratum-autobackup-{DateTime.Now:yyyyMMdd-HHmmss}.stratum";
                await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(settings.AutoBackupFolder, name), data);
                await AppServices.Settings.MarkBackedUpAsync();
            }
            catch
            {
                // auto-backup is best-effort
            }
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var b = value is bool v && v;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value;
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var isNull = value == null;
            if (Invert) isNull = !isNull;
            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value;
        }
    }
}
