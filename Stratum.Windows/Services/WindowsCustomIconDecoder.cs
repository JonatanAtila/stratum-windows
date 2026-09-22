// Stratum.Windows - custom icon decoding via SkiaSharp (resize to 128px PNG).

using System;
using System.Threading.Tasks;
using SkiaSharp;
using Stratum.Core;
using Stratum.Core.Entity;
using Stratum.Core.Util;

namespace Stratum.Windows.Services
{
    public class WindowsCustomIconDecoder : ICustomIconDecoder
    {
        public Task<CustomIcon> DecodeAsync(byte[] rawData, bool shouldPreProcess)
        {
            return Task.Run(() =>
            {
                using var original = SKBitmap.Decode(rawData);

                if (original == null)
                    throw new ArgumentException("Image could not be loaded.");

                SKBitmap squared = original;

                if (shouldPreProcess && (original.Width != original.Height))
                {
                    var side = Math.Max(original.Width, original.Height);
                    var square = new SKBitmap(side, side, true);
                    using var canvas = new SKCanvas(square);
                    canvas.Clear(SKColors.Transparent);
                    var dx = (side - original.Width) / 2;
                    var dy = (side - original.Height) / 2;
                    canvas.DrawBitmap(original, dx, dy);
                    squared = square;
                }

                var size = Math.Min(CustomIcon.MaxSize, Math.Min(squared.Width, squared.Height));
                using var resized = squared.Resize(new SKImageInfo(size, size), new SKSamplingOptions(SKCubicResampler.Mitchell));
                using var image = SKImage.FromBitmap(resized);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                var data = encoded.ToArray();

                if (squared != original)
                    squared.Dispose();

                var hash = HashUtil.Sha1(Convert.ToBase64String(data));
                var id = hash.Truncate(8);

                return new CustomIcon { Id = id, Data = data };
            });
        }
    }
}
