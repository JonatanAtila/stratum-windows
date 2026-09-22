using System;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Serilog;
using Stratum.Windows.Services;
using Stratum.Windows.Views;

namespace Stratum.Windows
{
    public partial class App : Application
    {
        public static new App Current => (App)Application.Current;
        public static MainWindow MainWindow { get; private set; }

        private AppInstance _mainInstance;

        public App()
        {
            InitializeComponent();
            UnhandledException += (s, e) =>
            {
            try
            {
                Serilog.Log.Fatal(e.Exception, "Unhandled exception");
                Serilog.Log.CloseAndFlush();
            }
                catch
                {
                    // logging é melhor esforço
                }
            };
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Instância única: segunda execução redireciona para a principal
            // (traz a janela para frente) e encerra. Evita dois processos
            // disputando o mesmo banco de dados.
            AppInstance instance;

            try
            {
                instance = AppInstance.FindOrRegisterForKey("StratumMain");
            }
            catch (Exception ex)
            {
                try { Serilog.Log.Warning(ex, "Single-instance register failed"); } catch { }
                instance = null;
            }

            if (instance != null && !instance.IsCurrent)
            {
                try
                {
                    var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
                    await instance.RedirectActivationToAsync(activated);
                }
                catch (Exception ex)
                {
                    try { Serilog.Log.Warning(ex, "Single-instance redirect failed"); } catch { }
                }

                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
            }

            if (instance != null)
            {
                _mainInstance = instance;
                _mainInstance.Activated += OnInstanceActivated;
            }

            try
            {
                var logDir = System.IO.Path.Combine(Services.Database.AppDataDir, "logs");
                System.IO.Directory.CreateDirectory(logDir);
                Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(
                        System.IO.Path.Combine(logDir, "stratum-.log"),
                        rollingInterval: Serilog.RollingInterval.Day,
                        retainedFileCountLimit: 7)
                    .CreateLogger();
            }
            catch
            {
                // segue sem log em arquivo
            }
            // SQLCipher native for Windows (StratumAuth.SQLCipher ships no win
            // native, so we use the e_sqlcipher provider: SQLCipher 4.x community
            // with default settings, keeping .db3 files fully compatible with
            // the Android app. Key handling is via "pragma key" (provider-agnostic).
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlcipher());
            SQLitePCL.raw.FreezeProvider();

            await AppServices.InitAsync();

            MainWindow = new MainWindow();
            ApplyTheme();

            // O backdrop (Mica/Acrílico/Sólido) é aplicado pelo MainWindow
            // via ApplyBackdrop(), conforme as configurações salvas.

            MainWindow.Activate();

            var locked = !await AppServices.Database.IsOpenAsync();
            var needsSetup = locked && !Database.Exists;

            if (locked)
                MainWindow.Navigate(typeof(LockPage), needsSetup ? "setup" : "unlock");
            else
                MainWindow.Navigate(typeof(AuthenticatorsPage));
        }

        private void OnInstanceActivated(object sender, AppActivationArguments e)
        {
            // Segunda instância tentou abrir: traz a janela principal para frente.
            MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (MainWindow.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped &&
                        overlapped.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
                    {
                        overlapped.Restore();
                    }

                    MainWindow.AppWindow.Show();
                    MainWindow.Activate();
                    MainWindow.NotifyActivity();
                }
                catch (Exception ex)
                {
                    try { Serilog.Log.Warning(ex, "Single-instance activate failed"); } catch { }
                }
            });
        }

        public void ApplyTheme()
        {
            if (MainWindow?.Content is FrameworkElement root && AppServices.Settings != null)
            {
                var want = AppServices.Settings.Theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };

                // Evita re-render (pisca) quando o tema não mudou
                if (root.RequestedTheme != want)
                    root.RequestedTheme = want;
            }
        }
    }
}
