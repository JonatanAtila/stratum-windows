// Stratum.Windows - manual service wiring (no DI container needed).

using System;
using System.Threading.Tasks;
using Stratum.Core;
using Stratum.Core.Comparer;
using Stratum.Core.Persistence;
using Stratum.Core.Service;
using Stratum.Core.Service.Impl;

namespace Stratum.Windows.Services
{
    public static class AppServices
    {
        public static Database Database { get; private set; }
        public static SettingsStore Settings { get; private set; }

        public static IAuthenticatorRepository AuthenticatorRepository { get; private set; }
        public static ICategoryRepository CategoryRepository { get; private set; }
        public static IAuthenticatorCategoryRepository AuthenticatorCategoryRepository { get; private set; }
        public static ICustomIconRepository CustomIconRepository { get; private set; }
        public static IIconPackRepository IconPackRepository { get; private set; }
        public static IIconPackEntryRepository IconPackEntryRepository { get; private set; }

        public static IAuthenticatorService AuthenticatorService { get; private set; }
        public static ICategoryService CategoryService { get; private set; }
        public static ICustomIconService CustomIconService { get; private set; }
        public static IIconPackService IconPackService { get; private set; }
        public static IBackupService BackupService { get; private set; }
        public static IRestoreService RestoreService { get; private set; }
        public static IImportService ImportService { get; private set; }

        public static IIconResolver IconResolver { get; private set; }
        public static IAssetProvider AssetProvider { get; private set; }
        public static ICustomIconDecoder CustomIconDecoder { get; private set; }

        /// <summary>Database password held in memory for this session only.</summary>
        public static string SessionPassword { get; set; }

        public static async Task InitAsync()
        {
            Settings = await SettingsStore.LoadAsync();

            Database = new Database();
            IconResolver = new WindowsIconResolver();
            AssetProvider = new WindowsAssetProvider();
            CustomIconDecoder = new WindowsCustomIconDecoder();

            AuthenticatorRepository = new AuthenticatorRepository(Database);
            CategoryRepository = new CategoryRepository(Database);
            AuthenticatorCategoryRepository = new AuthenticatorCategoryRepository(Database);
            CustomIconRepository = new CustomIconRepository(Database);
            IconPackRepository = new IconPackRepository(Database);
            IconPackEntryRepository = new IconPackEntryRepository(Database);

            CustomIconService = new CustomIconService(CustomIconRepository, AuthenticatorRepository);
            AuthenticatorService = new AuthenticatorService(AuthenticatorRepository, AuthenticatorCategoryRepository,
                CustomIconService, new AuthenticatorComparer());
            CategoryService = new CategoryService(CategoryRepository, AuthenticatorCategoryRepository,
                new CategoryComparer(), new AuthenticatorCategoryComparer());
            IconPackService = new IconPackService(IconPackRepository, IconPackEntryRepository);
            BackupService = new BackupService(AuthenticatorRepository, CategoryRepository,
                AuthenticatorCategoryRepository, CustomIconRepository, AssetProvider);
            RestoreService = new RestoreService(AuthenticatorService, CategoryService, CustomIconService);
            ImportService = new ImportService(RestoreService);
        }

        public static async Task<bool> IsLockedAsync()
        {
            return !await Database.IsOpenAsync();
        }

        public static async Task LockAsync()
        {
            SessionPassword = null;
            await Database.CloseAsync();
        }

        /// <summary>
        /// Navigates to the lock page via the UI thread queue, safe to call
        /// even from inside another navigation.
        /// </summary>
        public static void GoToLock()
        {
            var window = App.MainWindow;

            if (window == null)
                return;

            window.DispatcherQueue.TryEnqueue(() => window.Navigate(typeof(Views.LockPage), "unlock"));
        }

        /// <summary>
        /// Ensures the database is open before data actions. Tries a silent
        /// reopen with the session password; otherwise redirects to the lock
        /// page and returns false.
        /// </summary>
        public static async Task<bool> EnsureOpenAsync()
        {
            if (await Database.IsOpenAsync())
                return true;

            if (!string.IsNullOrEmpty(SessionPassword))
            {
                try
                {
                    await Database.OpenAsync(SessionPassword);

                    if (await Database.IsOpenAsync())
                        return true;
                }
                catch
                {
                    // fall through to lock page
                }
            }

            SessionPassword = null;
            await Database.CloseAsync();
            GoToLock();
            return false;
        }
    }
}
