// Stratum.Windows - brand icons shared with the Android app (icons/*.png).

using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Stratum.Windows.Services
{
    public static class BrandIcons
    {
        private static string IconsDir => Path.Combine(System.AppContext.BaseDirectory, "Assets", "Icons");

        public static string ResolvePath(string key, bool dark)
        {
            string Probe(string name)
            {
                var full = Path.Combine(IconsDir, name + ".png");
                return File.Exists(full) ? full : null;
            }

            if (!string.IsNullOrEmpty(key))
            {
                if (dark)
                {
                    var darkPath = Probe(key + "_dark");
                    if (darkPath != null)
                        return darkPath;
                }

                var lightPath = Probe(key);
                if (lightPath != null)
                    return lightPath;
            }

            if (dark)
            {
                var defaultDark = Probe("default_dark");
                if (defaultDark != null)
                    return defaultDark;
            }

            return Probe("default");
        }

        public static async Task<BitmapImage> LoadAsync(string key, bool dark)
        {
            var path = ResolvePath(key, dark);

            if (path == null)
                return null;

            try
            {
                return await UiHelpers.BitmapImageFromBytesAsync(await File.ReadAllBytesAsync(path));
            }
            catch
            {
                return null;
            }
        }
    }
}
