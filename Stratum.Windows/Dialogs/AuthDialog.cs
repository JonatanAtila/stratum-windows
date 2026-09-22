// Stratum.Windows - add/edit authenticator dialog (pure WinUI 3, built in code).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SimpleBase;
using Stratum.Core;
using Stratum.Core.Entity;
using Stratum.Core.Generator;
using CoreHashAlgorithm = Stratum.Core.Generator.HashAlgorithm;
using Stratum.Core.Util;
using Stratum.Windows.Services;

namespace Stratum.Windows.Dialogs
{
    public class AuthDialog : ContentDialog
    {
        private readonly Authenticator _original;
        private readonly bool _isNew;

        private TextBox _issuerBox;
        private TextBox _usernameBox;
        private TextBox _secretBox;
        private ComboBox _typeBox;
        private ComboBox _algorithmBox;
        private ComboBox _digitsBox;
        private TextBox _periodBox;
        private TextBox _counterBox;
        private TextBox _pinBox;
        private Image _iconPreview;
        private TextBlock _iconLabel;
        private CustomIcon _pendingCustomIcon;
        private string _pendingIconKey;
        private List<(CheckBox Box, Category Category)> _categoryBoxes = new();
        private TextBlock _errorText;
        private StackPanel _categoryHost;

        private AuthDialog(Authenticator existing)
        {
            _original = existing;
            _isNew = existing == null;

            Title = _isNew ? "Adicionar conta" : "Editar conta";
            PrimaryButtonText = "Salvar";
            CloseButtonText = "Cancelar";
            DefaultButton = ContentDialogButton.Primary;

            PrimaryButtonClick += async (s, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    args.Cancel = !await SaveAsync();
                }
                finally
                {
                    deferral.Complete();
                }
            };

            BuildContent();
            LoadValues();
        }

        public static async Task<bool> ShowAsync(XamlRoot root, Authenticator existing, int pinLengthHint = 0)
        {
            var dialog = new AuthDialog(existing) { XamlRoot = root };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private void BuildContent()
        {
            var scroll = new ScrollViewer { MaxHeight = 560 };
            var panel = new StackPanel { Spacing = 8, MinWidth = 360 };

            panel.Children.Add(Labeled("Emissor", _issuerBox = new TextBox { PlaceholderText = "Ex.: Google" }));
            panel.Children.Add(Labeled("Usuário (opcional)", _usernameBox = new TextBox { PlaceholderText = "Ex.: voce@exemplo.com" }));

            var secretRow = new Grid { ColumnSpacing = 8 };
            secretRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            secretRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _secretBox = new TextBox { PlaceholderText = "Segredo (base32 ou hexadecimal p/ mOTP)" };
            Grid.SetColumn(_secretBox, 0);
            secretRow.Children.Add(_secretBox);
            var genButton = new Button { Content = "Gerar" };
            genButton.Click += (s, e) => _secretBox.Text = GenerateSecret(CurrentType());
            Grid.SetColumn(genButton, 1);
            secretRow.Children.Add(genButton);
            panel.Children.Add(Labeled("Segredo", secretRow));

            _typeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            _typeBox.Items.Add(new ComboBoxItem { Content = "TOTP (baseado em tempo)", Tag = AuthenticatorType.Totp });
            _typeBox.Items.Add(new ComboBoxItem { Content = "HOTP (baseado em contador)", Tag = AuthenticatorType.Hotp });
            _typeBox.Items.Add(new ComboBoxItem { Content = "mOTP (Mobile-OTP)", Tag = AuthenticatorType.MobileOtp });
            _typeBox.Items.Add(new ComboBoxItem { Content = "Steam", Tag = AuthenticatorType.SteamOtp });
            _typeBox.Items.Add(new ComboBoxItem { Content = "Yandex", Tag = AuthenticatorType.YandexOtp });
            _typeBox.SelectionChanged += (s, e) => RefreshTypeVisibility();
            panel.Children.Add(Labeled("Tipo", _typeBox));

            _algorithmBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            _algorithmBox.Items.Add(new ComboBoxItem { Content = "SHA1", Tag = CoreHashAlgorithm.Sha1 });
            _algorithmBox.Items.Add(new ComboBoxItem { Content = "SHA256", Tag = CoreHashAlgorithm.Sha256 });
            _algorithmBox.Items.Add(new ComboBoxItem { Content = "SHA512", Tag = CoreHashAlgorithm.Sha512 });
            panel.Children.Add(Labeled("Algoritmo", _algorithmBox));

            _digitsBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(Labeled("Dígitos", _digitsBox));

            _periodBox = new TextBox { PlaceholderText = "30" };
            panel.Children.Add(Labeled("Período (segundos)", _periodBox));

            _counterBox = new TextBox { PlaceholderText = "0" };
            panel.Children.Add(Labeled("Contador inicial", _counterBox));

            _pinBox = new TextBox { PlaceholderText = "PIN" };
            panel.Children.Add(Labeled("PIN", _pinBox));

            var iconRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _iconPreview = new Image { Width = 40, Height = 40 };
            _iconLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
            var iconButton = new Button { Content = "Escolher imagem…" };
            iconButton.Click += IconButton_Click;
            var logoButton = new Button { Content = "Trocar logo…" };
            logoButton.Click += LogoButton_Click;
            var autoIconButton = new Button { Content = "Automático" };
            autoIconButton.Click += (s, e) =>
            {
                _pendingCustomIcon = null;
                _pendingIconKey = null;
                _iconPreview.Source = null;
                _iconLabel.Text = "Automático pelo emissor";
            };
            iconRow.Children.Add(_iconPreview);
            iconRow.Children.Add(_iconLabel);
            iconRow.Children.Add(iconButton);
            iconRow.Children.Add(logoButton);
            iconRow.Children.Add(autoIconButton);
            panel.Children.Add(Labeled("Ícone", iconRow));

            var catHeader = new TextBlock { Text = "Categorias", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            panel.Children.Add(catHeader);
            _categoryHost = new StackPanel { Spacing = 2 };
            panel.Children.Add(_categoryHost);

            _errorText = new TextBlock { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_errorText);

            scroll.Content = panel;
            Content = scroll;
        }

        private static StackPanel Labeled(string label, UIElement control)
        {
            var p = new StackPanel { Spacing = 4 };
            p.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            p.Children.Add(control);
            p.Tag = label;
            return p;
        }

        private AuthenticatorType CurrentType()
        {
            return (_typeBox.SelectedItem as ComboBoxItem)?.Tag is AuthenticatorType t ? t : AuthenticatorType.Totp;
        }

        private void RefreshTypeVisibility()
        {
            var type = CurrentType();
            SetVisible(_algorithmBox, type.HasVariableAlgorithm());
            SetVisible(_periodBox, type.HasVariablePeriod());
            SetVisible(_counterBox, type == AuthenticatorType.Hotp);
            SetVisible(_pinBox, type.HasPin());

            _digitsBox.Items.Clear();
            if (type.GetMinDigits() == type.GetMaxDigits())
            {
                _digitsBox.Items.Add(new ComboBoxItem { Content = type.GetMinDigits().ToString(), Tag = type.GetMinDigits() });
                _digitsBox.SelectedIndex = 0;
                _digitsBox.IsEnabled = false;
            }
            else
            {
                for (var d = type.GetMinDigits(); d <= type.GetMaxDigits(); d++)
                    _digitsBox.Items.Add(new ComboBoxItem { Content = d.ToString(), Tag = d });
                _digitsBox.SelectedIndex = 0;
                _digitsBox.IsEnabled = true;
            }

            if (type == AuthenticatorType.MobileOtp)
                _pinBox.PlaceholderText = $"PIN de {MobileOtp.PinLength} dígitos";
            else if (type == AuthenticatorType.YandexOtp)
                _pinBox.PlaceholderText = "PIN de 4 a 16 dígitos";
        }

        private static void SetVisible(UIElement control, bool visible)
        {
            // control is wrapped in a Labeled StackPanel
            var parent = control;
            while (parent != null && !(parent is StackPanel sp && sp.Tag is string))
                parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent) as UIElement;
            if (parent is UIElement el)
                el.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void LoadValues()
        {
            var auth = _original ?? new Authenticator();
            _issuerBox.Text = auth.Issuer ?? "";
            _usernameBox.Text = auth.Username ?? "";
            _secretBox.Text = auth.Secret ?? "";

            foreach (var item in _typeBox.Items.OfType<ComboBoxItem>())
            {
                if ((AuthenticatorType)item.Tag == auth.Type)
                {
                    _typeBox.SelectedItem = item;
                    break;
                }
            }

            RefreshTypeVisibility();

            foreach (var item in _algorithmBox.Items.OfType<ComboBoxItem>())
            {
                if ((CoreHashAlgorithm)item.Tag == auth.Algorithm)
                {
                    _algorithmBox.SelectedItem = item;
                    break;
                }
            }

            foreach (var item in _digitsBox.Items.OfType<ComboBoxItem>())
            {
                if ((int)item.Tag == auth.Digits)
                {
                    _digitsBox.SelectedItem = item;
                    break;
                }
            }

            if (_digitsBox.SelectedItem == null && _digitsBox.Items.Count > 0)
                _digitsBox.SelectedIndex = 0;

            _periodBox.Text = auth.Period.ToString();
            _counterBox.Text = auth.Counter.ToString();
            _pinBox.Text = auth.Pin ?? "";

            // icon preview
            if (!string.IsNullOrEmpty(auth.Icon) && auth.Icon[0] == CustomIcon.Prefix)
            {
                var icon = await AppServices.CustomIconRepository.GetAsync(auth.Icon.Substring(1));
                if (icon != null)
                {
                    _pendingCustomIcon = icon;
                    _iconPreview.Source = await UiHelpers.BitmapImageFromBytesAsync(icon.Data);
                    _iconLabel.Text = "Ícone personalizado";
                }
            }
            else if (!string.IsNullOrEmpty(auth.Icon))
            {
                _pendingIconKey = auth.Icon;
                _iconLabel.Text = "Ícone: " + auth.Icon;
            }
            else
            {
                _iconLabel.Text = "Automático pelo emissor";
            }

            // categories
            try
            {
                var categories = await AppServices.CategoryRepository.GetAllAsync();
                var bound = new HashSet<string>();

                if (!_isNew)
                {
                    var bindings = await AppServices.AuthenticatorCategoryRepository.GetAllForAuthenticatorAsync(_original);
                    bound = bindings.Select(b => b.CategoryId).ToHashSet();
                }

                foreach (var cat in categories.OrderBy(c => c.Name))
                {
                    var check = new CheckBox { Content = cat.Name, IsChecked = bound.Contains(cat.Id) };
                    _categoryBoxes.Add((check, cat));
                    _categoryHost.Children.Add(check);
                }

                if (categories.Count == 0)
                    _categoryHost.Children.Add(new TextBlock { Text = "Nenhuma categoria. Crie em Categorias.", Opacity = 0.6 });
            }
            catch
            {
                // categories are optional
            }
        }

        private async void IconButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = await UiHelpers.PickOpenFileAsync(App.MainWindow, "Escolher ícone",
                    ("Imagens", new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" }));
                if (file == null)
                    return;

                var bytes = await System.IO.File.ReadAllBytesAsync(file.Path);
                var decoded = await AppServices.CustomIconDecoder.DecodeAsync(bytes, true);
                _pendingCustomIcon = decoded;
                _pendingIconKey = null;
                _iconPreview.Source = await UiHelpers.BitmapImageFromBytesAsync(decoded.Data);
                _iconLabel.Text = "Ícone personalizado";
            }
            catch (Exception ex)
            {
                _errorText.Text = "Não foi possível carregar a imagem: " + ex.Message;
            }
        }

        private async void LogoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picked = await IconPickerDialog.ShowAsync(XamlRoot, _pendingIconKey, UiHelpers.IsDarkTheme());
                if (string.IsNullOrEmpty(picked))
                    return;

                _pendingIconKey = picked;
                _pendingCustomIcon = null;
                _iconPreview.Source = await BrandIcons.LoadAsync(picked, UiHelpers.IsDarkTheme());
                _iconLabel.Text = "Logo: " + picked;
            }
            catch (Exception ex)
            {
                try { Serilog.Log.Error(ex, "IconPicker failed"); } catch { }
                _errorText.Text = "Não foi possível carregar a logo: " + ex.Message;
            }
        }

        private static string GenerateSecret(AuthenticatorType type)
        {
            var bytes = new byte[type == AuthenticatorType.MobileOtp ? 16 : 20];
            RandomNumberGenerator.Fill(bytes);

            if (type == AuthenticatorType.MobileOtp)
                return Convert.ToHexString(bytes).ToLowerInvariant();

            return Base32.Rfc4648.Encode(bytes);
        }

        private async Task<bool> SaveAsync()
        {
            _errorText.Text = "";

            try
            {
                var type = CurrentType();
                var secret = SecretUtil.Normalise(_secretBox.Text.Trim(), type);

                var auth = new Authenticator
                {
                    Issuer = _issuerBox.Text.Trim(),
                    Username = string.IsNullOrWhiteSpace(_usernameBox.Text) ? null : _usernameBox.Text.Trim(),
                    Secret = secret,
                    Type = type,
                    Algorithm = (_algorithmBox.SelectedItem as ComboBoxItem)?.Tag is CoreHashAlgorithm alg ? alg : Authenticator.DefaultAlgorithm,
                    Digits = (_digitsBox.SelectedItem as ComboBoxItem)?.Tag is int d ? d : type.GetDefaultDigits(),
                    Period = int.TryParse(_periodBox.Text.Trim(), out var period) ? period : type.GetDefaultPeriod(),
                    Counter = long.TryParse(_counterBox.Text.Trim(), out var counter) ? counter : 0,
                    Pin = string.IsNullOrWhiteSpace(_pinBox.Text) ? null : _pinBox.Text.Trim()
                };

                if (type == AuthenticatorType.SteamOtp)
                    auth.Digits = SteamOtp.Digits;
                if (type == AuthenticatorType.MobileOtp)
                    auth.Digits = MobileOtp.Digits;
                if (type == AuthenticatorType.YandexOtp)
                    auth.Digits = YandexOtp.Digits;

                auth.Validate();

                if (type.HasPin() && string.IsNullOrEmpty(auth.Pin))
                {
                    _errorText.Text = "Este tipo exige um PIN.";
                    return false;
                }

                if (type == AuthenticatorType.MobileOtp && auth.Pin.Length != MobileOtp.PinLength)
                {
                    _errorText.Text = $"O PIN do mOTP deve ter {MobileOtp.PinLength} dígitos.";
                    return false;
                }

                if (type == AuthenticatorType.YandexOtp && (auth.Pin.Length < 4 || auth.Pin.Length > 16))
                {
                    _errorText.Text = "O PIN do Yandex deve ter de 4 a 16 dígitos.";
                    return false;
                }

                // icon
                if (_pendingCustomIcon != null)
                {
                    auth.Icon = CustomIcon.Prefix + _pendingCustomIcon.Id;
                }
                else if (_pendingIconKey != null)
                {
                    auth.Icon = _pendingIconKey;
                }
                else
                {
                    auth.Icon = AppServices.IconResolver.FindServiceKeyByName(auth.Issuer);
                }

                if (_isNew)
                {
                    if (_pendingCustomIcon != null)
                        await AppServices.CustomIconService.AddIfNotExistsAsync(_pendingCustomIcon);

                    await AppServices.AuthenticatorService.AddAsync(auth);

                    var defaultCategoryId = AppServices.Settings.DefaultCategoryId;
                    if (!string.IsNullOrEmpty(defaultCategoryId))
                    {
                        try
                        {
                            var def = await AppServices.CategoryService.GetCategoryByIdAsync(defaultCategoryId);
                            if (def != null)
                                await AppServices.CategoryService.AddBindingAsync(auth, def);
                        }
                        catch
                        {
                            // categoria padrão é melhor esforço
                        }
                    }
                }
                else
                {
                    auth.CopyCount = _original.CopyCount;
                    auth.Ranking = _original.Ranking;

                    if (auth.Secret != _original.Secret)
                    {
                        await AppServices.AuthenticatorService.ChangeSecretAsync(_original, auth.Secret);
                        _original.Secret = auth.Secret;
                    }

                    _original.Issuer = auth.Issuer;
                    _original.Username = auth.Username;
                    _original.Type = auth.Type;
                    _original.Algorithm = auth.Algorithm;
                    _original.Digits = auth.Digits;
                    _original.Period = auth.Period;
                    _original.Counter = auth.Counter;
                    _original.Pin = auth.Pin;
                    _original.Icon = auth.Icon;

                    if (_pendingCustomIcon != null)
                        await AppServices.AuthenticatorService.SetCustomIconAsync(_original, _pendingCustomIcon);
                    else
                        await AppServices.AuthenticatorService.UpdateAsync(_original);

                    auth = _original;
                }

                // categories
                var wanted = _categoryBoxes.Where(c => c.Box.IsChecked == true).Select(c => c.Category).ToList();
                var existing = _isNew
                    ? new List<AuthenticatorCategory>()
                    : await AppServices.AuthenticatorCategoryRepository.GetAllForAuthenticatorAsync(auth);

                foreach (var cat in wanted)
                {
                    if (existing.All(b => b.CategoryId != cat.Id))
                        await AppServices.CategoryService.AddBindingAsync(auth, cat);
                }

                foreach (var binding in existing)
                {
                    if (wanted.All(c => c.Id != binding.CategoryId))
                    {
                        var cat = await AppServices.CategoryService.GetCategoryByIdAsync(binding.CategoryId);
                        if (cat != null)
                            await AppServices.CategoryService.RemoveBindingAsync(auth, cat);
                    }
                }

                await AppServices.CustomIconService.CullUnusedAsync();
                return true;
            }
            catch (Exception ex)
            {
                _errorText.Text = ex.Message;
                return false;
            }
        }
    }
}
