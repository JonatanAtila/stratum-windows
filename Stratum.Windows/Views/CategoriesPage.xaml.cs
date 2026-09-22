using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Stratum.Core.Entity;
using Stratum.Windows.Services;

namespace Stratum.Windows.Views
{
    public sealed partial class CategoriesPage : Page
    {
        private readonly ObservableCollection<CategoryRow> _rows = new();

        public CategoriesPage()
        {
            InitializeComponent();
            CategoryList.ItemsSource = _rows;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (!await AppServices.EnsureOpenAsync())
                return;

            await LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            try
            {
                var categories = await AppServices.CategoryRepository.GetAllAsync();
                var bindings = await AppServices.AuthenticatorCategoryRepository.GetAllAsync();

                _rows.Clear();
                var defaultId = AppServices.Settings.DefaultCategoryId;
                foreach (var cat in categories.OrderBy(c => c.Ranking).ThenBy(c => c.Name))
                {
                    var count = bindings.Count(b => b.CategoryId == cat.Id);
                    _rows.Add(new CategoryRow(cat, count, cat.Id == defaultId));
                }
            }
            catch (Exception ex)
            {
                ShowStatus("Erro ao carregar: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            await AddAsync();
        }

        private async void NewCategoryBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == global::Windows.System.VirtualKey.Enter)
                await AddAsync();
        }

        private async System.Threading.Tasks.Task AddAsync()
        {
            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            var name = NewCategoryBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
                return;

            try
            {
                await AppServices.CategoryService.AddCategoryAsync(new Category(name));
                NewCategoryBox.Text = "";
                await AppServices.Settings.MarkDirtyAsync();
                await LoadAsync();
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao adicionar: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void RenameButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not CategoryRow row)
                return;

            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            var box = new TextBox { Text = row.Category.Name, MaxLength = Category.NameMaxLength };
            var dialog = new ContentDialog
            {
                Title = "Renomear categoria",
                Content = box,
                PrimaryButtonText = "Salvar",
                CloseButtonText = "Cancelar",
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            var next = new Category(box.Text.Trim()) { Ranking = row.Category.Ranking };

            try
            {
                await AppServices.CategoryService.TransferAsync(row.Category, next);

                if (AppServices.Settings.DefaultCategoryId == row.Category.Id)
                {
                    AppServices.Settings.DefaultCategoryId = next.Id;
                    await AppServices.Settings.SaveAsync();
                }

                await AppServices.Settings.MarkDirtyAsync();
                await LoadAsync();
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao renomear: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not CategoryRow row)
                return;

            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            var ok = await UiHelpers.ConfirmAsync(XamlRoot, "Excluir categoria",
                $"Excluir \"{row.Category.Name}\"? As contas serão mantidas.", "Excluir");
            if (!ok)
                return;

            try
            {
                await AppServices.CategoryService.DeleteWithCategoryBindingsASync(row.Category);

                if (AppServices.Settings.DefaultCategoryId == row.Category.Id)
                {
                    AppServices.Settings.DefaultCategoryId = "";
                    await AppServices.Settings.SaveAsync();
                }

                await AppServices.Settings.MarkDirtyAsync();
                await LoadAsync();
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao excluir: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void UpButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not CategoryRow row)
                return;

            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            await MoveAsync(row, -1);
        }

        private async void DownButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not CategoryRow row)
                return;

            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            await MoveAsync(row, 1);
        }

        private async System.Threading.Tasks.Task MoveAsync(CategoryRow row, int delta)
        {
            try
            {
                var ordered = _rows.Select(r => r.Category).ToList();
                var index = ordered.IndexOf(row.Category);
                var next = index + delta;

                if (index < 0 || next < 0 || next >= ordered.Count)
                    return;

                (ordered[index], ordered[next]) = (ordered[next], ordered[index]);

                for (var i = 0; i < ordered.Count; i++)
                    ordered[i].Ranking = i;

                await AppServices.CategoryService.UpdateManyCategoriesAsync(ordered);
                await AppServices.Settings.MarkDirtyAsync();
                await LoadAsync();
            }
            catch (Exception ex)
            {
                ShowStatus("Falha ao reordenar: " + ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void DefaultButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not CategoryRow row)
                return;

            App.MainWindow.NotifyActivity();

            if (!await AppServices.EnsureOpenAsync())
                return;

            AppServices.Settings.DefaultCategoryId =
                AppServices.Settings.DefaultCategoryId == row.Category.Id ? "" : row.Category.Id;
            await AppServices.Settings.SaveAsync();
            await LoadAsync();

            ShowStatus(
                string.IsNullOrEmpty(AppServices.Settings.DefaultCategoryId)
                    ? "Categoria padrão removida."
                    : $"\"{row.Category.Name}\" é a categoria padrão para novas contas.",
                InfoBarSeverity.Success);
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusBar.Message = message;
            StatusBar.Severity = severity;
            StatusBar.IsOpen = true;
        }

        public class CategoryRow
        {
            public Category Category { get; }
            public int Count { get; }
            public bool IsDefault { get; }

            public CategoryRow(Category category, int count, bool isDefault = false)
            {
                Category = category;
                Count = count;
                IsDefault = isDefault;
            }

            public string Name => Category.Name;
            public string CountText => Count == 1 ? "1 conta" : $"{Count} contas";
        }
    }

}
