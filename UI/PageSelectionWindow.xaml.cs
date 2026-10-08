using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TranslationEplAddIn.Tool.UI
{
    public partial class PageSelectionWindow : Window
    {
        private List<PageSelectionItem> _allItems = new List<PageSelectionItem>();
        private ObservableCollection<PageSelectionItem> _visibleItems = new ObservableCollection<PageSelectionItem>();

        public List<Eplan.EplApi.DataModel.Page> SelectedPages { get; private set; } = new List<Eplan.EplApi.DataModel.Page>();

        public PageSelectionWindow(Eplan.EplApi.DataModel.Page[] allPages, Eplan.EplApi.DataModel.Page[] preSelectedPages)
        {
            InitializeComponent();

            HashSet<string> preSelectedIds = new HashSet<string>();
            if (preSelectedPages != null)
            {
                foreach (var p in preSelectedPages)
                {
                    if (p != null) preSelectedIds.Add(p.ToStringIdentifier());
                }
            }

            foreach (var page in allPages)
            {
                if (page == null) continue;

                string desc = "";
                try
                {
                    if (page.Properties.Exists(11011))
                    {
                        desc = page.Properties[11011].ToString();
                    }
                }
                catch { }

                bool isSelected = preSelectedIds.Count > 0 ? preSelectedIds.Contains(page.ToStringIdentifier()) : true;

                _allItems.Add(new PageSelectionItem
                {
                    IsSelected = isSelected,
                    PageName = page.Name,
                    PageDescription = desc,
                    PageObject = page
                });
            }

            ApplyFilter();
            PagesListBox.ItemsSource = _visibleItems;
            UpdateSelectionCount();
        }

        private void ApplyFilter()
        {
            string filterText = SearchTextBox.Text?.Trim() ?? "";
            _visibleItems.Clear();
            foreach (var item in _allItems)
            {
                if (string.IsNullOrEmpty(filterText) ||
                    item.PageName.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.PageDescription.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _visibleItems.Add(item);
                }
            }
        }

        private void UpdateSelectionCount()
        {
            int selectedCount = _allItems.Count(i => i.IsSelected);
            int totalCount = _allItems.Count;
            SelectionCountTextBlock.Text = $"已选择: {selectedCount} / {totalCount}";
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void SelectAllBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _visibleItems)
            {
                item.IsSelected = true;
            }
            UpdateSelectionCount();
        }

        private void DeselectAllBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _visibleItems)
            {
                item.IsSelected = false;
            }
            UpdateSelectionCount();
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelectionCount();
        }

        private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            SelectedPages.Clear();
            foreach (var item in _allItems)
            {
                if (item.IsSelected)
                {
                    SelectedPages.Add(item.PageObject);
                }
            }
            DialogResult = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class PageSelectionItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string PageName { get; set; } = string.Empty;
        public string PageDescription { get; set; } = string.Empty;
        public Eplan.EplApi.DataModel.Page PageObject { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
