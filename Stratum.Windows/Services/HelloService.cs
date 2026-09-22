// Stratum.Windows - Windows Hello backed unlock + DPAPI stored password.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Credentials.UI;

namespace Stratum.Windows.Services
{
    public static class HelloService
    {
        private static string BlobPath => Path.Combine(Database.AppDataDir, "hello.dat");

        public static async Task<bool> IsAvailableAsync()
        {
            try
            {
                return await UserConsentVerifier.CheckAvailabilityAsync()
                    == UserConsentVerifierAvailability.Available;
            }
            catch
            {
                return false;
            }
        }

        public static bool HasStoredPassword()
        {
            try
            {
                return File.Exists(BlobPath);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> StorePasswordAsync(string password, string reason)
        {
            if (string.IsNullOrEmpty(password))
                return false;

            UserConsentVerificationResult consent;

            try
            {
                consent = await UserConsentVerifier.RequestVerificationAsync(reason);
            }
            catch
            {
                return false;
            }

            if (consent != UserConsentVerificationResult.Verified)
                return false;

            try
            {
                var plain = Encoding.UTF8.GetBytes(password);
                var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                Array.Clear(plain, 0, plain.Length);
                Directory.CreateDirectory(Database.AppDataDir);
                await File.WriteAllBytesAsync(BlobPath, protectedBytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> UnlockAsync(string reason)
        {
            UserConsentVerificationResult consent;

            try
            {
                consent = await UserConsentVerifier.RequestVerificationAsync(reason);
            }
            catch
            {
                return null;
            }

            if (consent != UserConsentVerificationResult.Verified)
                return null;

            try
            {
                var protectedBytes = await File.ReadAllBytesAsync(BlobPath);
                var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                var password = Encoding.UTF8.GetString(plain);
                Array.Clear(plain, 0, plain.Length);
                return password;
            }
            catch
            {
                return null;
            }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(BlobPath))
                    File.Delete(BlobPath);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
