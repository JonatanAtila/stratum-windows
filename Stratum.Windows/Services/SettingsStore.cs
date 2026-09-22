// Stratum.Windows - persistent settings stored as JSON in %LocalAppData%\Stratum.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Stratum.Windows.Services
{
    public class SettingsStore
    {
        private static readonly string SettingsPath = Path.Combine(Database.AppDataDir, "settings.json");

        public string Theme { get; set; } = "System"; // System | Light | Dark
        public string Backdrop { get; set; } = "Mica"; // Mica | Acrylic | Solid
        public string SortMode { get; set; } = "IssuerAsc"; // IssuerAsc | IssuerDesc | CopyCount | CopyCountAsc | Custom
        public string ViewMode { get; set; } = "Default"; // Default | Compact | Tile
        public bool TapToCopy { get; set; } = true;
        public bool TapToReveal { get; set; } = false;
        public int RevealDurationSeconds { get; set; } = 10;
        public string CodeGrouping { get; set; } = "Halves"; // None | Two | Three | Four | Halves | Thirds
        public bool SkipToNext { get; set; } = false;
        public bool ShowUsernames { get; set; } = true;
        public bool ShowUncategorised { get; set; } = false;
        public string DefaultCategoryId { get; set; } = "";
        public bool MinimizeToTray { get; set; } = false;
        public int AutoLockMinutes { get; set; } = 0; // 0 = disabled
        public bool AutoBackupEnabled { get; set; } = false;
        public string AutoBackupFolder { get; set; } = "";
        public bool AutoRestoreEnabled { get; set; } = false;
        public string AutoRestoreFolder { get; set; } = "";
        public string LastAutoRestoreUtc { get; set; } = "";
        public bool HasUnbackedChanges { get; set; } = false;
        public bool FirstRunDone { get; set; } = false;
        public bool HelloEnabled { get; set; } = false;

        public static async Task<SettingsStore> LoadAsync()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = await File.ReadAllTextAsync(SettingsPath);
                    var parsed = JsonSerializer.Deserialize<SettingsStore>(json);
                    if (parsed != null)
                        return parsed;
                }
            }
            catch
            {
                // Corrupt settings -> defaults
            }

            return new SettingsStore();
        }

        public async Task SaveAsync()
        {
            try
            {
                Directory.CreateDirectory(Database.AppDataDir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(SettingsPath, json);
            }
            catch
            {
                // Settings are best-effort
            }
        }

        public async Task MarkDirtyAsync()
        {
            HasUnbackedChanges = true;
            await SaveAsync();
        }

        public async Task MarkBackedUpAsync()
        {
            HasUnbackedChanges = false;
            await SaveAsync();
        }
    }
}
