using System;
using System.IO;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stratum.Windows.Services;
using Stratum.Windows.Tray;
using Stratum.Windows.Views;
using Windows.Graphics;

namespace Stratum.Windows
{
    public sealed partial class MainWindow : Window
    {
        // Janela fixa: 2 colunas de contas (modo Padrão).
        private const int FixedWidth = 710;
        private const int FixedHeight = 810;

        private const uint WM_NCLBUTTONDBLCLK = 0x00A3;

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
            IntPtr uIdSubclass, IntPtr dwRefData);

        private SubclassProc _subclassRef;

        [System.Runtime.InteropServices.DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
            IntPtr uIdSubclass, IntPtr dwRefData);

        [System.Runtime.InteropServices.DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass);

        private DispatcherTimer _autoLockTimer;
        private DispatcherTimer _trayTimer;
        private DateTime _lastActivity = DateTime.UtcNow;
        private bool _allowClose;

        public MainWindow()
        {
            InitializeComponent();
            Title = "Stratum";

            try
            {
                // Ícone da janela (barra de tarefas, Alt+Tab, título).
                // O ApplicationIcon do exe só aparece no Explorer — no WinUI
                // unpackaged a janela precisa do SetIcon explícito.
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");

                if (File.Exists(iconPath))
                    AppWindow.SetIcon(iconPath);
            }
            catch
            {
                // segue sem ícone customizado
            }

            try
            {
                var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");

                if (File.Exists(logoPath))
                    TitleBarIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(logoPath));
            }
            catch
            {
                // segue sem logo na title bar
            }

            // Mica + title bar integrada ao fluxo
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            try
            {
                if (AppWindow.TitleBar is { } titleBar)
                    titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            }
            catch
            {
                // opção indisponível (Windows 10)
            }

            UpdateTitleBarColors();

            if (Content is FrameworkElement root)
                root.ActualThemeChanged += (s, e) => UpdateTitleBarColors();

            ApplyBackdrop();

            var appWindow = AppWindow;

            // Janela fixa: 2 colunas de contas (modo Padrão).
            // Trava tripla contra maximizar: sem botão (IsMaximizable),
            // sem duplo-clique (subclass engole WM_NCLBUTTONDBLCLK +
            // DoubleTapped marcado) e cão de guarda que restaura o tamanho.
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }

            appWindow.Resize(new SizeInt32(FixedWidth, FixedHeight));

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                _subclassRef = WindowSubclassProc;
                SetWindowSubclass(hwnd, _subclassRef, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // segue sem a trava de subclass
            }

            appWindow.Changed += AppWindow_Changed;

            _autoLockTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _autoLockTimer.Tick += AutoLockTimer_Tick;
            _autoLockTimer.Start();

            _trayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _trayTimer.Tick += TrayTimer_Tick;
            _trayTimer.Start();

            SetupTray();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private void TrayTimer_Tick(object sender, object e)
        {
            try
            {
                if (AppServices.Settings?.MinimizeToTray != true)
                    return;

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hwnd == IntPtr.Zero)
                    return;

                if (IsIconic(hwnd) && AppWindow.IsVisible)
                    AppWindow.Hide();
            }
            catch
            {
                // best-effort
            }
        }

        private void SetupTray()
        {
            try
            {
                TrayService.EnsureCreated(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
                TrayService.OpenRequested += ShowFromTray;
                TrayService.ExitRequested += ExitFromTray;
                TrayService.SetVisible(AppServices.Settings?.MinimizeToTray == true);

                // Minimizar pelo botão é capturado pelo TrayTimer (IsIconic);
                // fechar pelo X é interceptado em AppWindow_Closing.
                AppWindow.Closing += AppWindow_Closing;
                Closed += (s, e) =>
                {
                    try
                    {
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                        if (_subclassRef != null)
                            RemoveWindowSubclass(hwnd, _subclassRef, IntPtr.Zero);
                    }
                    catch
                    {
                        // melhor esforço
                    }

                    TrayService.Dispose();
                };
            }
            catch
            {
                // tray is best-effort
            }
        }

        public void RefreshTrayVisibility()
        {
            TrayService.SetVisible(AppServices.Settings?.MinimizeToTray == true);
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_allowClose)
                return;

            try
            {
                if (AppServices.Settings?.MinimizeToTray == true)
                {
                    args.Cancel = true;
                    AppWindow.Hide();
                }
            }
            catch
            {
                // fecha normalmente
            }
        }

        private void ShowFromTray()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (AppWindow.Presenter is OverlappedPresenter overlapped &&
                        overlapped.State == OverlappedPresenterState.Minimized)
                    {
                        overlapped.Restore();
                    }

                    AppWindow.Show();
                    Activate();
                    NotifyActivity();
                }
                catch
                {
                    // best-effort
                }
            });
        }

        private void ExitFromTray()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _allowClose = true;
                TrayService.SetVisible(false);
                TrayService.Dispose();
                Close();
            });
        }

        /// <summary>
        /// Deixa os botões da legenda (min/max/fechar) legíveis sobre o Mica
        /// no tema atual. Chamado no arranque e a cada troca de tema.
        /// </summary>
        public void UpdateTitleBarColors()
        {
            try
            {
                var dark = Content is FrameworkElement root && root.ActualTheme == ElementTheme.Dark;
                var titleBar = AppWindow.TitleBar;

                var foreground = dark
                    ? Microsoft.UI.Colors.White
                    : Microsoft.UI.Colors.Black;
                var hoverFg = foreground;
                var hoverBg = dark
                    ? global::Windows.UI.Color.FromArgb(22, 255, 255, 255)
                    : global::Windows.UI.Color.FromArgb(22, 0, 0, 0);

                titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonForegroundColor = foreground;
                titleBar.ButtonHoverBackgroundColor = hoverBg;
                titleBar.ButtonHoverForegroundColor = hoverFg;
                titleBar.ButtonPressedBackgroundColor = hoverBg;
                titleBar.ButtonPressedForegroundColor = hoverFg;
                titleBar.ButtonInactiveForegroundColor = dark
                    ? global::Windows.UI.Color.FromArgb(255, 128, 128, 128)
                    : global::Windows.UI.Color.FromArgb(255, 110, 110, 110);
            }
            catch
            {
                // customização indisponível: mantém o padrão do sistema
            }
        }

        /// <summary>
        /// Aplica o material de fundo (Mica / Acrílico / Sólido), respeitando
        /// a chave "Efeitos de transparência" do Windows.
        /// </summary>
        public void ApplyBackdrop()
        {
            try
            {
                var kind = AppServices.Settings?.Backdrop ?? "Mica";

                var effectsEnabled = true;
                try
                {
                    effectsEnabled = new global::Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled;
                }
                catch
                {
                    // assume efeitos ativos
                }

                if (!effectsEnabled)
                    kind = "Solid";

                // Reaplicar o mesmo backdrop faz a janela piscar (o compositor
                // reconstrói o fundo). Só troca quando o tipo muda.
                var current = SystemBackdrop;
                var same = (kind == "Acrylic" && current is Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop)
                    || (kind == "Mica" && current is Microsoft.UI.Xaml.Media.MicaBackdrop)
                    || (kind == "Solid" && current == null);

                if (same)
                    return;

                if (kind == "Acrylic")
                {
                    SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
                }
                else if (kind == "Solid")
                {
                    SystemBackdrop = null;
                }
                else
                {
                    SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                }
            }
            catch
            {
                try
                {
                    SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                }
                catch
                {
                    // sem backdrop customizado
                }
            }
        }

        private void AppTitleBar_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            // A janela é fixa: sem maximizar por duplo-clique (IsMaximizable
            // remove o botão, mas o gesto na title bar custom precisa ser
            // contido aqui). Arrastar para mover continua funcionando.
            e.Handled = true;
        }

        private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
            IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_NCLBUTTONDBLCLK)
                return IntPtr.Zero;

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (!args.DidSizeChange)
                return;

            try
            {
                var size = sender.Size;
                if (size.Width != FixedWidth || size.Height != FixedHeight)
                    sender.Resize(new SizeInt32(FixedWidth, FixedHeight));
            }
            catch
            {
                // melhor esforço
            }
        }

        public void Navigate(Type page, object parameter = null)        {
            ContentFrame.Navigate(page, parameter);

            NavView.SelectedItem = page switch
            {
                _ when page == typeof(AuthenticatorsPage) => NavAuthenticators,
                _ when page == typeof(CategoriesPage) => NavCategories,
                _ when page == typeof(BackupPage) => NavBackup,
                _ when page == typeof(SettingsPage) => NavSettings,
                _ => null
            };
        }

        public void NotifyActivity()
        {
            _lastActivity = DateTime.UtcNow;
        }

        private async void AutoLockTimer_Tick(object sender, object e)
        {
            var minutes = AppServices.Settings?.AutoLockMinutes ?? 0;
            if (minutes <= 0)
                return;

            if (!await AppServices.Database.IsOpenAsync())
                return;

            if ((DateTime.UtcNow - _lastActivity).TotalMinutes < minutes)
                return;

            await AppServices.LockAsync();
            Navigate(typeof(LockPage), "unlock");
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item)
                return;

            NotifyActivity();

            var tag = item.Tag as string;
            var target = tag switch
            {
                "auth" => typeof(AuthenticatorsPage),
                "categories" => typeof(CategoriesPage),
                "backup" => typeof(BackupPage),
                "settings" => typeof(SettingsPage),
                _ => null
            };

            if (target != null && ContentFrame.CurrentSourcePageType != target)
                ContentFrame.Navigate(target);
        }
    }
}
