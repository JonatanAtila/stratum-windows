// Stratum.Windows - brand icon picker (full bundled pack with search).
// Implemented as a Popup (not ContentDialog): WinUI allows only one
// ContentDialog open at a time, and this picker opens above AuthDialog.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Stratum.Windows.Dialogs
{
    public class IconPickerDialog
    {
        private readonly ObservableCollection<IconEntry> _entries = new();
        private List<string> _all = new();
        private ListView _list;
        private TextBox _search;
        private Popup _popup;
        private readonly TaskCompletionSource<string> _tcs = new();

        private IconPickerDialog() { }

        public static Task<string> ShowAsync(XamlRoot root, string currentKey, bool dark)
        {
            var picker = new IconPickerDialog();
            picker.Build(root, currentKey, dark);
            return picker._tcs.Task;
        }

        private void Build(XamlRoot root, string currentKey, bool dark)
        {
            const double width = 400;
            const double height = 520;

            var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
            panel.Children.Add(new TextBlock
            {
                Text = "Escolher logo",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 16
            });

            _search = new TextBox { PlaceholderText = "Pesquisar serviço..." };
            _search.TextChanged += (s, e) => ApplyFilter();
            panel.Children.Add(_search);

            _list = new ListView
            {
                ItemsSource = _entries,
                SelectionMode = ListViewSelectionMode.Single,
                Height = 360,
                MinWidth = 340
            };
            _list.ItemTemplate = BuildTemplate();
            _list.DoubleTapped += (s, e) => CommitSelection();
            panel.Children.Add(_list);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Usar logo" };
            ok.Click += (s, e) => CommitSelection();
            var cancel = new Button { Content = "Cancelar" };
            cancel.Click += (s, e) => Close(null);
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            var card = new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Width = width,
                Height = height,
                Child = new ScrollViewer { Content = panel }
            };

            _popup = new Popup
            {
                XamlRoot = root,
                Child = card,
                IsLightDismissEnabled = true,
                HorizontalOffset = Math.Max(0, (root.Size.Width - width) / 2),
                VerticalOffset = Math.Max(0, (root.Size.Height - height) / 2)
            };
            _popup.Closed += (s, e) => _tcs.TrySetResult(null);

            _ = LoadAsync(currentKey, dark);
            _popup.IsOpen = true;
        }

        private void CommitSelection()
        {
            if (_list.SelectedItem is IconEntry entry)
                Close(entry.Key);
        }

        private void Close(string key)
        {
            _tcs.TrySetResult(key);
            _popup.IsOpen = false;
        }

        private DataTemplate BuildTemplate()
        {
            const string xaml =
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<StackPanel Orientation=\"Horizontal\" Spacing=\"12\" Padding=\"4\">" +
                "<Image Width=\"28\" Height=\"28\" Source=\"{Binding ImagePath}\" />" +
                "<TextBlock Text=\"{Binding Key}\" VerticalAlignment=\"Center\"/>" +
                "</StackPanel></DataTemplate>";
            return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
        }

        private async Task LoadAsync(string currentKey, bool dark)
        {
            await Task.Run(() =>
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons");
                if (!Directory.Exists(dir))
                    return;

                _all = Directory.GetFiles(dir, "*.png")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => !n.EndsWith("_dark") && n != "default")
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();
            });

            ApplyFilter();

            if (!string.IsNullOrEmpty(currentKey))
            {
                var existing = _entries.FirstOrDefault(e => e.Key == currentKey);
                if (existing != null)
                    _list.SelectedItem = existing;
            }
        }

        private void ApplyFilter()
        {
            var query = (_search?.Text ?? "").Trim().ToLowerInvariant();
            _entries.Clear();

            foreach (var key in _all)
            {
                if (!string.IsNullOrEmpty(query) && !key.Contains(query))
                    continue;

                _entries.Add(new IconEntry
                {
                    Key = key,
                    ImagePath = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", key + ".png")).AbsoluteUri
                });

                if (_entries.Count >= 200)
                    break;
            }
        }

        public class IconEntry
        {
            public string Key { get; set; }
            public string ImagePath { get; set; }
        }
    }
}

