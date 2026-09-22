// Stratum.Windows - WinUI helpers: pickers, dialogs, QR images, avatars.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Stratum.Windows.Services
{
    public static class UiHelpers
    {
        public static nint GetWindowHandle(Window window)
        {
            return WinRT.Interop.WindowNative.GetWindowHandle(window);
        }

        public static void InitPicker(object picker, Window window)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, GetWindowHandle(window));
        }

        public static async Task<StorageFile> PickOpenFileAsync(Window window, string commitText, params (string Label, string[] Extensions)[] filters)
        {
            var picker = new FileOpenPicker();
            InitPicker(picker, window);
            picker.CommitButtonText = commitText;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            foreach (var (label, exts) in filters)
                picker.FileTypeFilter.Add(exts.Length == 1 && exts[0] == "*" ? "*" : exts[0]);

            // WinUI FileOpenPicker needs one filter per extension
            picker.FileTypeFilter.Clear();
            foreach (var (label, exts) in filters)
                foreach (var ext in exts)
                    picker.FileTypeFilter.Add(ext);

            return await picker.PickSingleFileAsync();
        }

        public static async Task<StorageFile> PickSaveFileAsync(Window window, string suggestedName, params (string Label, string[] Extensions)[] types)
        {
            var picker = new FileSavePicker();
            InitPicker(picker, window);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.SuggestedFileName = suggestedName;

            foreach (var (label, exts) in types)
                picker.FileTypeChoices.Add(label, new List<string>(exts));

            return await picker.PickSaveFileAsync();
        }

        public static async Task<StorageFolder> PickFolderAsync(Window window, string commitText)
        {
            var picker = new FolderPicker();
            InitPicker(picker, window);
            picker.CommitButtonText = commitText;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");
            return await picker.PickSingleFolderAsync();
        }

        public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string confirm = "Confirmar")
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = confirm,
                CloseButtonText = "Cancelar",
                XamlRoot = root,
                DefaultButton = ContentDialogButton.Close
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        public static async Task ShowMessageAsync(XamlRoot root, string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new ScrollViewer
                {
                    Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    MaxHeight = 400
                },
                CloseButtonText = "OK",
                XamlRoot = root
            };

            await dialog.ShowAsync();
        }

        public static async Task<string> PromptPasswordAsync(XamlRoot root, string title, string message, bool confirm = false)
        {
            var panel = new StackPanel { Spacing = 8, MinWidth = 280 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

            var box = new PasswordBox { PlaceholderText = "Senha" };
            panel.Children.Add(box);

            PasswordBox confirmBox = null;
            if (confirm)
            {
                confirmBox = new PasswordBox { PlaceholderText = "Confirmar senha" };
                panel.Children.Add(confirmBox);
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancelar",
                XamlRoot = root,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return null;

            if (confirm && box.Password != confirmBox.Password)
                return PromptPasswordMismatch;

            return box.Password;
        }

        public static async Task<string> DecodeQrFromImageAsync(string path)
        {
            return await Task.Run(() =>
            {
                using var bitmap = (System.Drawing.Bitmap)System.Drawing.Image.FromFile(path);
                var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
                var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                try
                {
                    // Copia justa (sem padding de stride) para o ZXing
                    var tight = new byte[bitmap.Width * bitmap.Height * 4];
                    var src = new byte[data.Stride * data.Height];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, src, 0, src.Length);

                    for (var y = 0; y < bitmap.Height; y++)
                        System.Buffer.BlockCopy(src, y * data.Stride, tight, y * bitmap.Width * 4, bitmap.Width * 4);

                    var source = new ZXing.RGBLuminanceSource(tight, bitmap.Width, bitmap.Height,
                        ZXing.RGBLuminanceSource.BitmapFormat.BGRA32);

                    var reader = new ZXing.BarcodeReaderGeneric
                    {
                        AutoRotate = true,
                        Options = new ZXing.Common.DecodingOptions
                        {
                            TryHarder = true,
                            TryInverted = true,
                            PossibleFormats = new List<ZXing.BarcodeFormat> { ZXing.BarcodeFormat.QR_CODE }
                        }
                    };

                    return reader.Decode(source)?.Text;
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            });
        }

        public const string PromptPasswordMismatch = "\u0000MISMATCH\u0000";

        public static async Task<BitmapImage> QrCodeImageAsync(string uri, int pixelsPerModule = 12)
        {
            var png = await Task.Run(() =>
            {
                using var generator = new QRCodeGenerator();
                var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
                return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
            });

            var image = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            await image.SetSourceAsync(stream);
            return image;
        }

        public static async Task<BitmapImage> BitmapImageFromBytesAsync(byte[] png)
        {
            var image = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            await image.SetSourceAsync(stream);
            return image;
        }

        private static readonly string[] AvatarBrushes =
        {
            "#5B5BD6", "#0078D4", "#038387", "#498205", "#8764B8",
            "#C239B3", "#E81123", "#D13438", "#CA5010", "#986F0B"
        };

        public static SolidColorBrush AvatarBrush(string issuer)
        {
            var hash = string.IsNullOrEmpty(issuer) ? 0 : issuer.GetHashCode();
            var color = AvatarBrushes[Math.Abs(hash) % AvatarBrushes.Length];
            var c = global::Windows.UI.Color.FromArgb(255,
                Convert.ToByte(color.Substring(1, 2), 16),
                Convert.ToByte(color.Substring(3, 2), 16),
                Convert.ToByte(color.Substring(5, 2), 16));
            return new SolidColorBrush(c);
        }

        public static string AvatarLetter(string issuer)
        {
            if (string.IsNullOrWhiteSpace(issuer))
                return "?";
            return issuer.Trim()[0].ToString().ToUpperInvariant();
        }

        public static bool IsDarkTheme()
        {
            if (App.MainWindow?.Content is FrameworkElement root)
                return root.ActualTheme == ElementTheme.Dark;
            return false;
        }
    }
}
