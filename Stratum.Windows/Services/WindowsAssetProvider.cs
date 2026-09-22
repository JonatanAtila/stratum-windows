// Stratum.Windows - reads bundled templates next to the executable.

using System;
using System.IO;
using System.Threading.Tasks;
using Stratum.Core;

namespace Stratum.Windows.Services
{
    public class WindowsAssetProvider : IAssetProvider
    {
        public Task<byte[]> ReadBytesAsync(string path)
        {
            var full = Path.Combine(AppContext.BaseDirectory, "Assets", path);
            return File.ReadAllBytesAsync(full);
        }

        public Task<string> ReadStringAsync(string path)
        {
            var full = Path.Combine(AppContext.BaseDirectory, "Assets", path);
            return File.ReadAllTextAsync(full);
        }
    }
}
