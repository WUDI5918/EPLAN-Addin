using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace TranslationEplAddIn.Tool.UI
{
    public partial class SettingsWindow : Window
    {
        private List<string> _tempGlossaryPaths;

        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            var config = SettingsManager.Current;

            // Set ComboBox selection for Engine
            foreach (ComboBoxItem item in EngineComboBox.Items)
            {
                if (item.Content.ToString() == config.SelectedEngine)
                {
                    item.IsSelected = true;
                    break;
                }
            }

            // Set keys and config
            DeepseekKeyBox.Text = config.DeepseekApiKey;
            DeepseekUrlBox.Text = config.DeepseekEndpoint;
            DeepseekModelBox.Text = config.DeepseekModel;

            ZhipuKeyBox.Text = config.ZhipuApiKey;
            ZhipuUrlBox.Text = config.ZhipuEndpoint;
            ZhipuModelBox.Text = config.ZhipuModel;

            DeeplKeyBox.Text = config.DeeplApiKey;
            GoogleKeyBox.Text = config.GoogleApiKey;

            // Set strategies
            AppendModeCheckBox.IsChecked = config.AppendMode;
            NativeDictCheckBox.IsChecked = config.UseNativeEplanDictionary;
            
            // Set ComboBox selection for Separator
            string sep = config.AppendSeparator;
            if (sep == "\n") SeparatorComboBox.SelectedIndex = 0;
            else if (sep == " / ") SeparatorComboBox.SelectedIndex = 1;
            else if (sep == " ~ ") SeparatorComboBox.SelectedIndex = 2;
            else if (sep == " - ") SeparatorComboBox.SelectedIndex = 3;

            SeparatorComboBox.IsEnabled = config.AppendMode;

            // Set Regex rules
            RegexRulesBox.Text = string.Join(Environment.NewLine, config.RegexProtectionRules);

            // Load glossary paths
            _tempGlossaryPaths = new List<string>(config.GlossaryPaths);
            UpdateGlossaryList();
        }

        private void UpdateGlossaryList()
        {
            GlossaryListBox.Items.Clear();
            foreach (var path in _tempGlossaryPaths)
            {
                GlossaryListBox.Items.Add(Path.GetFileName(path) + " (" + path + ")");
            }
        }

        private void SaveSettings()
        {
            var config = SettingsManager.Current;

            // Save engine
            config.SelectedEngine = ((ComboBoxItem)EngineComboBox.SelectedItem).Content.ToString();

            // Save keys and config
            config.DeepseekApiKey = DeepseekKeyBox.Text;
            config.DeepseekEndpoint = DeepseekUrlBox.Text;
            config.DeepseekModel = DeepseekModelBox.Text;

            config.ZhipuApiKey = ZhipuKeyBox.Text;
            config.ZhipuEndpoint = ZhipuUrlBox.Text;
            config.ZhipuModel = ZhipuModelBox.Text;

            config.DeeplApiKey = DeeplKeyBox.Text;
            config.GoogleApiKey = GoogleKeyBox.Text;

            // Save strategies
            config.AppendMode = AppendModeCheckBox.IsChecked ?? false;
            config.UseNativeEplanDictionary = NativeDictCheckBox.IsChecked ?? false;

            int sepIndex = SeparatorComboBox.SelectedIndex;
            if (sepIndex == 0) config.AppendSeparator = "\n";
            else if (sepIndex == 1) config.AppendSeparator = " / ";
            else if (sepIndex == 2) config.AppendSeparator = " ~ ";
            else if (sepIndex == 3) config.AppendSeparator = " - ";

            // Save Regex rules
            config.RegexProtectionRules = RegexRulesBox.Text
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .ToList();

            // Save glossary
            config.GlossaryPaths = _tempGlossaryPaths;

            // Persist to disk
            SettingsManager.Save();
            
            // Re-load the glossaries in memory
            GlossaryManager.Glossary.Clear();
            foreach (var path in config.GlossaryPaths)
            {
                GlossaryManager.LoadGlossary(path);
            }
        }

        private void EngineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EngineComboBox == null) return;
            string selectedEngine = ((ComboBoxItem)EngineComboBox.SelectedItem).Content.ToString();
            
            if (DeepseekConfigCard != null) DeepseekConfigCard.Visibility = selectedEngine == "Deepseek" ? Visibility.Visible : Visibility.Collapsed;
            if (ZhipuConfigCard != null) ZhipuConfigCard.Visibility = selectedEngine == "Zhipu" ? Visibility.Visible : Visibility.Collapsed;
            if (DeeplConfigCard != null) DeeplConfigCard.Visibility = selectedEngine == "DeepL" ? Visibility.Visible : Visibility.Collapsed;
            if (GoogleConfigCard != null) GoogleConfigCard.Visibility = selectedEngine == "Google" ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null) return;

            string engine = ((ComboBoxItem)EngineComboBox.SelectedItem).Content.ToString();
            string apiKey = "";
            string endpoint = "";
            string modelName = "";

            if (engine == "Deepseek")
            {
                apiKey = DeepseekKeyBox.Text;
                endpoint = DeepseekUrlBox.Text;
                modelName = DeepseekModelBox.Text;
            }
            else if (engine == "Zhipu")
            {
                apiKey = ZhipuKeyBox.Text;
                endpoint = ZhipuUrlBox.Text;
                modelName = ZhipuModelBox.Text;
            }
            else if (engine == "DeepL")
            {
                apiKey = DeeplKeyBox.Text;
            }
            else if (engine == "Google")
            {
                apiKey = GoogleKeyBox.Text;
            }

            btn.IsEnabled = false;
            string originalContent = btn.Content.ToString();
            btn.Content = "测试中...";

            try
            {
                bool success = await TranslationService.TestConnectionAsync(engine, apiKey, endpoint, modelName);
                if (success)
                {
                    CustomMessageBoxWindow.Show("API 连接测试成功！连接正常。", "测试成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                CustomMessageBoxWindow.Show(ex.Message, "测试失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = originalContent;
            }
        }

        private void AppendModeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (SeparatorComboBox != null) SeparatorComboBox.IsEnabled = true;
        }

        private void AppendModeCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (SeparatorComboBox != null) SeparatorComboBox.IsEnabled = false;
        }

        private void AddGlossary_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Terminology Files (*.csv;*.xlsx;*.tmx;*.tbx)|*.csv;*.xlsx;*.tmx;*.tbx|CSV files (*.csv)|*.csv|Excel files (*.xlsx)|*.xlsx|TMX files (*.tmx)|*.tmx|TBX files (*.tbx)|*.tbx";
            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                if (!_tempGlossaryPaths.Contains(filePath))
                {
                    _tempGlossaryPaths.Add(filePath);
                    UpdateGlossaryList();
                }
            }
        }

        private void RemoveGlossary_Click(object sender, RoutedEventArgs e)
        {
            int selectedIndex = GlossaryListBox.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < _tempGlossaryPaths.Count)
            {
                _tempGlossaryPaths.RemoveAt(selectedIndex);
                UpdateGlossaryList();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
