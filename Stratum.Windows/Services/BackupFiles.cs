// Stratum.Windows - shared backup file helpers (decrypt .stratum, pick/save).

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Stratum.Core.Backup;
using Stratum.Core.Backup.Encryption;

namespace Stratum.Windows.Services
{
    public static class BackupFiles
    {
        public static async Task<Backup> DecryptStratumAsync(byte[] data, string password)
        {
            var strong = new StrongBackupEncryption();
            var none = new NoBackupEncryption();
            var legacy = new LegacyBackupEncryption();

            if (strong.CanBeDecrypted(data))
                return await strong.DecryptAsync(data, password);
            if (none.CanBeDecrypted(data))
                return await none.DecryptAsync(data, password);
            if (legacy.CanBeDecrypted(data))
                return await legacy.DecryptAsync(data, password);

            throw new ArgumentException("Arquivo não reconhecido como backup Stratum.");
        }

        public static bool LooksEncrypted(byte[] data)
        {
            return new StrongBackupEncryption().CanBeDecrypted(data)
                || new LegacyBackupEncryption().CanBeDecrypted(data);
        }

        public static async Task SaveBytesAsync(Window window, string name, (string, string[]) type, byte[] data)
        {
            var file = await UiHelpers.PickSaveFileAsync(window, Path.GetFileNameWithoutExtension(name), type);
            if (file == null)
                return;
            await File.WriteAllBytesAsync(file.Path, data);
        }

        public static async Task SaveTextAsync(Window window, string name, (string, string[]) type, string text)
        {
            var file = await UiHelpers.PickSaveFileAsync(window, Path.GetFileNameWithoutExtension(name), type);
            if (file == null)
                return;
            await File.WriteAllTextAsync(file.Path, text, Encoding.UTF8);
        }
    }
}
