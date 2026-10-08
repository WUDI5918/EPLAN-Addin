using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;
using TranslationEplAddIn.Tool.Models;

namespace TranslationEplAddIn.Tool.UI
{
    public partial class ProofreadingPanel : UserControl
    {
        private List<TranslationItem> _items;
        public event Action<string> TextEdited;
        public event Action<List<TranslationItem>> RetranslateRequested;

        public ProofreadingPanel()
        {
            InitializeComponent();
        }

        public void LoadData(List<TranslationItem> items)
        {
            _items = items;
            TranslationDataGrid.ItemsSource = null;
            TranslationDataGrid.ItemsSource = _items;
            UpdateSummary();
        }

        public List<TranslationItem> GetData()
        {
            return _items;
        }

        private void UpdateSummary()
        {
            if (_items == null || _items.Count == 0)
            {
                SummaryTextBlock.Text = "未提取到需要翻译的文本。";
                return;
            }

            int total = _items.Count;
            int translated = _items.Count(i => i.Status == "Translated");
            int glossary = _items.Count(i => i.Status == "Glossary");
            int error = _items.Count(i => i.Status == "Error");
            int pending = _items.Count(i => i.Status == "Pending");

            SummaryTextBlock.Text = $"总条目数: {total} | 已翻译: {translated} | 词典匹配: {glossary} | 待处理: {pending} | 失败: {error}";
        }


        private void LocateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TranslationItem item)
            {
                bool success = DrawingLocator.LocateAndHighlight(item.ObjectId);
                if (!success)
                {
                    CustomMessageBoxWindow.Show($"无法定位到图纸元素: {item.SourceText}", "定位失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void TranslationDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                var textBox = e.EditingElement as TextBox;
                if (textBox != null)
                {
                    var item = e.Row.Item as TranslationItem;
                    if (item != null)
                    {
                        string newText = textBox.Text ?? string.Empty;
                        if (item.TranslatedText != newText)
                        {
                            item.TranslatedText = newText;
                            TextEdited?.Invoke($"手动修正词条 [{item.PageName}] - 翻译文: \"{newText}\"");

                            if (!string.IsNullOrWhiteSpace(newText))
                            {
                                item.Status = "Translated";
                                item.EngineUsed = "Manual";
                            }
                            else
                            {
                                item.Status = "Pending";
                            }

                            UpdateSummary();

                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                TranslationDataGrid.Items.Refresh();
                            }));
                        }
                    }
                }
            }
        }

        private void FindBtn_Click(object sender, RoutedEventArgs e)
        {
            FindNext(true);
        }

        private bool FindNext(bool showWarning)
        {
            string findText = FindTextBox.Text;
            if (string.IsNullOrEmpty(findText))
            {
                if (showWarning)
                {
                    CustomMessageBoxWindow.Show("请输入查找内容。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }

            if (_items == null || _items.Count == 0) return false;

            int startIndex = 0;
            if (TranslationDataGrid.SelectedItem is TranslationItem currentItem)
            {
                startIndex = _items.IndexOf(currentItem) + 1;
            }

            for (int i = 0; i < _items.Count; i++)
            {
                int index = (startIndex + i) % _items.Count;
                var item = _items[index];
                if (item.TranslatedText != null && item.TranslatedText.IndexOf(findText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TranslationDataGrid.SelectedItem = item;
                    TranslationDataGrid.ScrollIntoView(item);
                    return true;
                }
            }

            if (showWarning)
            {
                CustomMessageBoxWindow.Show("未找到指定的文本。", "查找完毕", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return false;
        }

        private void ReplaceBtn_Click(object sender, RoutedEventArgs e)
        {
            string findText = FindTextBox.Text;
            string replaceText = ReplaceTextBox.Text ?? "";

            if (string.IsNullOrEmpty(findText))
            {
                CustomMessageBoxWindow.Show("请输入查找内容。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TranslationDataGrid.SelectedItem is TranslationItem selectedItem)
            {
                if (selectedItem.TranslatedText != null && selectedItem.TranslatedText.IndexOf(findText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    selectedItem.TranslatedText = ReplaceFirstOccurrence(selectedItem.TranslatedText, findText, replaceText);
                    if (selectedItem.Status != "Glossary" && selectedItem.Status != "Translated")
                    {
                        selectedItem.Status = "Translated"; // Mark as translated if edited
                    }
                    
                    TranslationDataGrid.Items.Refresh();
                    UpdateSummary();

                    FindNext(false);
                    return;
                }
            }

            if (!FindNext(true))
            {
                if (TranslationDataGrid.SelectedItem is TranslationItem nextItem && nextItem.TranslatedText != null && nextItem.TranslatedText.IndexOf(findText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    nextItem.TranslatedText = ReplaceFirstOccurrence(nextItem.TranslatedText, findText, replaceText);
                    if (nextItem.Status != "Glossary" && nextItem.Status != "Translated")
                    {
                        nextItem.Status = "Translated";
                    }
                    TranslationDataGrid.Items.Refresh();
                    UpdateSummary();
                }
            }
        }

        private string ReplaceFirstOccurrence(string source, string find, string replace)
        {
            if (string.IsNullOrEmpty(source)) return source;
            string escapedFind = Regex.Escape(find);
            var regex = new Regex(escapedFind, RegexOptions.IgnoreCase);
            return regex.Replace(source, replace, 1);
        }

        private void ReplaceAllBtn_Click(object sender, RoutedEventArgs e)
        {
            string findText = FindTextBox.Text;
            string replaceText = ReplaceTextBox.Text ?? "";

            if (string.IsNullOrEmpty(findText))
            {
                CustomMessageBoxWindow.Show("请输入查找内容。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_items == null || _items.Count == 0) return;

            int count = 0;
            string escapedFind = Regex.Escape(findText);

            foreach (var item in _items)
            {
                if (item.TranslatedText != null)
                {
                    var matches = Regex.Matches(item.TranslatedText, escapedFind, RegexOptions.IgnoreCase);
                    if (matches.Count > 0)
                    {
                        item.TranslatedText = Regex.Replace(item.TranslatedText, escapedFind, replaceText, RegexOptions.IgnoreCase);
                        count += matches.Count;
                        if (item.Status != "Glossary")
                        {
                            item.Status = "Translated"; // Mark as edited/translated
                        }
                    }
                }
            }

            if (count > 0)
            {
                TranslationDataGrid.Items.Refresh();
                UpdateSummary();
                CustomMessageBoxWindow.Show($"全部替换完成，共替换了 {count} 处。", "替换成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                CustomMessageBoxWindow.Show("未找到指定的文本，替换未执行。", "替换结果", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DeleteSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedItems();
        }

        private void TranslationDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                DeleteSelectedItems();
                e.Handled = true;
            }
        }

        private void DeleteSelectedItems()
        {
            if (TranslationDataGrid.SelectedItems == null || TranslationDataGrid.SelectedItems.Count == 0)
            {
                return;
            }

            var selectedList = TranslationDataGrid.SelectedItems.Cast<TranslationItem>().ToList();
            if (selectedList.Count == 0) return;

            var result = CustomMessageBoxWindow.Show(
                $"确定要删除选中的 {selectedList.Count} 条数据吗？（删除后将不进行翻译及回写）",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var item in selectedList)
                {
                    _items.Remove(item);
                }

                // Refresh DataGrid source
                TranslationDataGrid.ItemsSource = null;
                TranslationDataGrid.ItemsSource = _items;
                UpdateSummary();
                TextEdited?.Invoke($"批量删除了 {selectedList.Count} 条图纸文本元素");
            }
        }

        private void RetranslateSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (TranslationDataGrid.SelectedItems == null || TranslationDataGrid.SelectedItems.Count == 0)
            {
                CustomMessageBoxWindow.Show("请先在列表中选中需要重新翻译的文本。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedList = TranslationDataGrid.SelectedItems.Cast<TranslationItem>().ToList();
            if (selectedList.Count == 0) return;

            RetranslateRequested?.Invoke(selectedList);
        }
    }
}
