using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.Gui;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.MasterData;
using TranslationEplAddIn.Tool.Models;

using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Input;

namespace TranslationEplAddIn.Tool.UI
{
    public partial class MainWindow : Window
    {
        private List<TranslationItem> _extractedItems = new List<TranslationItem>();
        private CancellationTokenSource _cts;
        private List<string> _tempGlossaryPaths = new List<string>();

        private ObservableCollection<RegexRuleItem> _regexRules = new ObservableCollection<RegexRuleItem>();
        private ObservableCollection<GlossaryDisplayItem> _glossaryTerms = new ObservableCollection<GlossaryDisplayItem>();
        private ObservableCollection<LogEntry> _logEntries = new ObservableCollection<LogEntry>();

        private string _logLevelFilter = "ALL";
        private string _logSearchQuery = "";

        public MainWindow() : this(0)
        {
        }

        public MainWindow(int initialTab)
        {
            InitializeComponent();
            _cts = new CancellationTokenSource();

            // Wire up collections to list controls
            RegexRulesItemsControl.ItemsSource = _regexRules;
            GlossaryDisplayListBox.ItemsSource = _glossaryTerms;
            LogListView.ItemsSource = _logEntries;

            // Log manual edits from Proofreading panel
            ProofreadingGrid.TextEdited += (msg) => {
                Log("SUCCESS", msg);
                UpdateExportStats(); // Keep stats updated
            };

            ProofreadingGrid.RetranslateRequested += async (selectedItems) => {
                await RunTranslationProcess(selectedItems, true);
            };

            MainTabControl.SelectedIndex = initialTab;
            LoadSettings();
            UpdateEngineLabel();
            UpdateProjectName();

            // Hook window Activated event to auto-refresh project name
            this.Activated += (sender, e) => {
                UpdateProjectName();
            };

            Log("INFO", "成功加载 EPLAN 智能翻译云助手系统组件。");
            UpdateTabButtons(initialTab);
        }

        private void UpdateEngineLabel()
        {
            EngineLabel.Text = $"当前引擎: {SettingsManager.Current.SelectedEngine}";
        }

        private Project GetActiveProject()
        {
            Project activeProject = null;
            
            // Method 1: Try SelectionSet (with locking disabled to prevent conflicts)
            try
            {
                SelectionSet selection = new SelectionSet();
                selection.LockProjectByDefault = false;
                selection.LockSelectionByDefault = false;
                activeProject = selection.GetCurrentProject(false);
            }
            catch (Exception ex)
            {
                Log("WARNING", $"通过 SelectionSet 获取当前项目失败: {ex.Message}");
            }

            // Method 2: Try ProjectManager CurrentProject
            if (activeProject == null)
            {
                try
                {
                    ProjectManager pm = new ProjectManager();
                    pm.LockProjectByDefault = false;
                    activeProject = pm.CurrentProject;
                }
                catch (Exception ex)
                {
                    Log("WARNING", $"通过 ProjectManager 获取当前项目失败: {ex.Message}");
                }
            }

            // Method 3: Try ProjectManager OpenProjects
            if (activeProject == null)
            {
                try
                {
                    ProjectManager pm = new ProjectManager();
                    pm.LockProjectByDefault = false;
                    var openProjects = pm.OpenProjects;
                    if (openProjects != null && openProjects.Length > 0)
                    {
                        activeProject = openProjects[0];
                    }
                }
                catch (Exception ex)
                {
                    Log("WARNING", $"通过 ProjectManager 获取打开项目列表失败: {ex.Message}");
                }
            }

            return activeProject;
        }

        private void UpdateProjectName()
        {
            try
            {
                Project activeProject = GetActiveProject();
                if (activeProject != null)
                {
                    ProjectNameLabel.Text = $"当前项目: {activeProject.ProjectName}";
                }
                else
                {
                    ProjectNameLabel.Text = "当前项目: 无活跃项目";
                }
            }
            catch (Exception ex)
            {
                ProjectNameLabel.Text = "当前项目: 未检测到";
                Log("ERROR", $"更新当前项目名称失败: {ex.Message}");
            }
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainTabControl == null) return;
            
            UpdateTabButtons(MainTabControl.SelectedIndex);

            // If the user switched to the Export & Logs tab (index 2), refresh statistics
            if (MainTabControl.SelectedIndex == 2)
            {
                UpdateExportStats();
            }
        }

        private void NavBtn_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null) return;

            if (btn == NavProjBtn)
            {
                MainTabControl.SelectedIndex = 0;
            }
            else if (btn == NavSetBtn)
            {
                MainTabControl.SelectedIndex = 1;
            }
            else if (btn == NavLogBtn)
            {
                MainTabControl.SelectedIndex = 2;
            }
        }

        private void UpdateTabButtons(int selectedIndex)
        {
            if (NavProjBtn == null || NavSetBtn == null || NavLogBtn == null) return;

            var unselectedBg = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E5E4DB"));
            var unselectedFg = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#626058"));
            
            NavProjBtn.Background = unselectedBg;
            NavProjBtn.Foreground = unselectedFg;
            NavSetBtn.Background = unselectedBg;
            NavSetBtn.Foreground = unselectedFg;
            NavLogBtn.Background = unselectedBg;
            NavLogBtn.Foreground = unselectedFg;

            Button activeBtn = null;
            if (selectedIndex == 0) activeBtn = NavProjBtn;
            else if (selectedIndex == 1) activeBtn = NavSetBtn;
            else if (selectedIndex == 2) activeBtn = NavLogBtn;

            if (activeBtn != null)
            {
                activeBtn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3D3D3A"));
                activeBtn.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF"));
            }
        }

        private void ExportTabBtn_Click(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedIndex = 2; // Switch to Export & Logs Tab
        }

        private void SettingsTabBtn_Click(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedIndex = 1; // Switch to Settings Tab
        }

        // ==================== TAB 1: TRANSLATION ACTIONS ====================

        private void ExtractBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log("INFO", "启动图纸提取核心...");
                SelectionSet selection = new SelectionSet();
                Project[] selectedProjects = selection.SelectedProjects;
                List<Project> projectsToProcess = new List<Project>();

                if (selectedProjects != null && selectedProjects.Length > 0)
                {
                    projectsToProcess.AddRange(selectedProjects);
                }
                else
                {
                    Project activeProject = GetActiveProject();
                    if (activeProject != null)
                    {
                        projectsToProcess.Add(activeProject);
                    }
                }

                if (projectsToProcess.Count == 0)
                {
                    MessageBox.Show("未检测到打开 of EPLAN 项目，请先打开项目再进行提取。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                List<Eplan.EplApi.DataModel.Page> pagesToExtract = new List<Eplan.EplApi.DataModel.Page>();
                bool entireProject = ScopeComboBox.SelectedIndex == 0;

                if (!entireProject)
                {
                    Project activeProj = projectsToProcess.FirstOrDefault();
                    if (activeProj == null)
                    {
                        MessageBox.Show("未检测到打开 of EPLAN 项目，请先打开项目再进行提取。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var allPages = activeProj.Pages;
                    var preSelectedPages = selection.GetSelectedPages();
                    if (preSelectedPages == null || preSelectedPages.Length == 0)
                    {
                        if (selection.CurrentlyEdited is Eplan.EplApi.DataModel.Page page)
                        {
                            preSelectedPages = new[] { page };
                        }
                    }

                    // Show custom page selection dialog
                    PageSelectionWindow pageSelector = new PageSelectionWindow(allPages, preSelectedPages);
                    pageSelector.Owner = this;
                    if (pageSelector.ShowDialog() == true)
                    {
                        pagesToExtract = pageSelector.SelectedPages;
                    }
                    else
                    {
                        // User cancelled extraction
                        return;
                    }

                    if (pagesToExtract.Count == 0)
                    {
                        MessageBox.Show("未选中任何需要翻译的页面。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // Show progress bar and disable Cancel button since extraction is synchronous
                ReadyLabel.Visibility = Visibility.Collapsed;
                ProgressGrid.Visibility = Visibility.Visible;
                TranslationProgressBar.Value = 0;
                ProgressTextBlock.Text = "0%";
                CancelBtn.IsEnabled = false;

                // 1. Load the Glossary Manager with the latest files
                GlossaryManager.Glossary.Clear();
                foreach (var path in SettingsManager.Current.GlossaryPaths)
                {
                    GlossaryManager.LoadGlossary(path);
                }

                string sourceLangStr = ((ComboBoxItem)SourceLangComboBox.SelectedItem).Content.ToString();
                ISOCode.Language sourceLangCode = MapLanguage(sourceLangStr);

                // Create sync progress reporter
                var progressIndicator = new SyncProgress(percent =>
                {
                    TranslationProgressBar.Value = percent;
                    ProgressTextBlock.Text = $"{percent}%";
                    DoEvents();
                });

                // 2. Perform Extraction
                _extractedItems.Clear();
                using (new LockingStep())
                {
                    foreach (var proj in projectsToProcess)
                    {
                        var items = EplanTextExtractor.ExtractTexts(proj, entireProject, pagesToExtract, sourceLangCode, progressIndicator);
                        _extractedItems.AddRange(items);
                    }
                }

                // 3. Match glossary terms immediately on extraction
                foreach (var item in _extractedItems)
                {
                    string matchedTerm = GlossaryManager.Lookup(item.SourceText);
                    if (!string.IsNullOrEmpty(matchedTerm))
                    {
                        item.TranslatedText = matchedTerm;
                        item.Status = "Glossary";
                        item.EngineUsed = "Glossary";
                    }
                }

                // 4. Match EPLAN system dictionary terms if enabled
                if (SettingsManager.Current.UseNativeEplanDictionary)
                {
                    try
                    {
                        string targetLangStr = ((ComboBoxItem)TargetLangComboBox.SelectedItem).Content.ToString();
                        ISOCode.Language targetLang = MapLanguage(targetLangStr);

                        using (new LockingStep())
                        {
                            using (var db = new MDTranslationDatabase())
                            {
                                db.Open(true); // Open Read-Only
                                foreach (var item in _extractedItems)
                                {
                                    // Skip if already matched by glossary
                                    if (item.Status == "Glossary") continue;

                                    MultiLangString mls = new MultiLangString();
                                    mls.AddString(sourceLangCode, item.SourceText);
                                    string comment = "";
                                    if (db.FindKeyword(sourceLangCode, ref mls, ref comment))
                                    {
                                        string existingTranslation = mls.GetString(targetLang);
                                        if (!string.IsNullOrEmpty(existingTranslation))
                                        {
                                            item.TranslatedText = existingTranslation;
                                            item.Status = "Glossary";
                                            item.EngineUsed = "EPLAN Dictionary";
                                        }
                                    }
                                }
                                db.Close();
                            }
                        }
                        Log("INFO", "成功检索 EPLAN 原生系统字典，匹配了部分已有翻译条目。");
                    }
                    catch (Exception ex)
                    {
                        Log("WARNING", "检索 EPLAN 原生系统字典失败: " + ex.Message);
                    }
                }

                CountLabel.Text = $"提取条目: {_extractedItems.Count}";
                ProofreadingGrid.LoadData(_extractedItems);

                Log("SUCCESS", $"图纸提取完成! 已成功识别并检索出 {_extractedItems.Count} 个候选独立条目");

                if (_extractedItems.Count > 0)
                {
                    TranslateBtn.IsEnabled = true;
                    WriteBackBtn.IsEnabled = false;
                    MessageBox.Show($"成功提取 {_extractedItems.Count} 条图纸文本。", "提取完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    TranslateBtn.IsEnabled = false;
                    WriteBackBtn.IsEnabled = false;
                    MessageBox.Show("所选范围内未发现符合翻译要求的文本。", "提取完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"图纸文本提取失败: {ex.Message}");
                MessageBox.Show("文本提取失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressGrid.Visibility = Visibility.Collapsed;
                CancelBtn.IsEnabled = true; // Restore Cancel button state
                ReadyLabel.Visibility = Visibility.Visible;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private async void TranslateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedItems == null || _extractedItems.Count == 0) return;
            await RunTranslationProcess(_extractedItems);
        }

        private async Task RunTranslationProcess(List<TranslationItem> itemsToTranslate, bool skipBackup = false)
        {
            if (itemsToTranslate == null || itemsToTranslate.Count == 0) return;

            // Force garbage collection to release any residual EPLAN API COM handles before backup
            GC.Collect();
            GC.WaitForPendingFinalizers();

            try
            {
                Log("INFO", "启动智能 AI 翻译引擎...");
                
                if (!skipBackup)
                {
                    // 1. Safety Check: Trigger Backup for all projects involved
                    ReadyLabel.Text = "正在创建项目安全备份...";
                    ReadyLabel.Foreground = System.Windows.Media.Brushes.Black;
                    ReadyLabel.Visibility = Visibility.Visible;

                    var projectsToBackup = itemsToTranslate
                        .Select(item => item.ProjectFullName)
                        .Where(path => !string.IsNullOrEmpty(path))
                        .Distinct()
                        .ToList();

                    bool backupSuccess = true;
                    foreach (var projPath in projectsToBackup)
                    {
                        if (!BackupService.CreateBackup(projPath))
                        {
                            backupSuccess = false;
                        }
                    }

                    if (!backupSuccess)
                    {
                        MessageBoxResult result = MessageBox.Show(
                            "部分 EPLAN 项目无法创建安全备份（.zw1）。是否要在无完全备份保护的情况下继续翻译？",
                            "备份警告",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning
                        );
                        if (result == MessageBoxResult.No)
                        {
                            ReadyLabel.Text = "就绪";
                            ReadyLabel.Foreground = System.Windows.Media.Brushes.Black;
                            ReadyLabel.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                }

                // Initialize cancellation and progress
                _cts = new CancellationTokenSource();
                ProgressGrid.Visibility = Visibility.Visible;
                ReadyLabel.Visibility = Visibility.Collapsed;
                TranslationProgressBar.Value = 0;
                ProgressTextBlock.Text = "0%";

                ExtractBtn.IsEnabled = false;
                TranslateBtn.IsEnabled = false;
                WriteBackBtn.IsEnabled = false;

                string sourceLang = ((ComboBoxItem)SourceLangComboBox.SelectedItem).Content.ToString();
                string targetLang = ((ComboBoxItem)TargetLangComboBox.SelectedItem).Content.ToString();

                var progressIndicator = new Progress<int>(percent =>
                {
                    TranslationProgressBar.Value = percent;
                    ProgressTextBlock.Text = $"{percent}%";
                });

                // 2. Pre-lookup in EPLAN system dictionary if enabled
                if (SettingsManager.Current.UseNativeEplanDictionary)
                {
                    try
                    {
                        ISOCode.Language sourceLangCode = MapLanguage(sourceLang);
                        ISOCode.Language targetLangCode = MapLanguage(targetLang);

                        using (new LockingStep())
                        {
                            using (var db = new MDTranslationDatabase())
                            {
                                db.Open(true); // Open Read-Only
                                foreach (var item in itemsToTranslate)
                                {
                                    // Skip if already translated by glossary or user
                                    if (item.Status == "Glossary" || item.Status == "Translated") continue;

                                    MultiLangString mls = new MultiLangString();
                                    mls.AddString(sourceLangCode, item.SourceText);
                                    string comment = "";
                                    if (db.FindKeyword(sourceLangCode, ref mls, ref comment))
                                    {
                                        string existingTranslation = mls.GetString(targetLangCode);
                                        if (!string.IsNullOrEmpty(existingTranslation))
                                        {
                                            item.TranslatedText = existingTranslation;
                                            item.Status = "Glossary";
                                            item.EngineUsed = "EPLAN Dictionary";
                                        }
                                    }
                                }
                                db.Close();
                            }
                        }
                        Log("INFO", "翻译前成功检索 EPLAN 原生系统字典，匹配了部分已有翻译条目。");
                    }
                    catch (Exception ex)
                    {
                        Log("WARNING", "翻译前检索 EPLAN 原生系统字典失败: " + ex.Message);
                    }
                }

                // Call async translator (REST APIs run on background thread)
                await Task.Run(async () =>
                {
                    await TranslationService.TranslateAsync(itemsToTranslate, sourceLang, targetLang, progressIndicator, _cts.Token);
                });

                Log("SUCCESS", "AI 翻译批处理完成!");
                MessageBox.Show("智能翻译处理完成，请检查校对视图中的译文并微调，确认无误后点击 [写入图纸]。", "翻译完成", MessageBoxButton.OK, MessageBoxImage.Information);
                WriteBackBtn.IsEnabled = true;
            }
            catch (System.OperationCanceledException)
            {
                Log("WARNING", "翻译进程已被用户取消。");
                MessageBox.Show("翻译进程已被用户取消。", "已取消", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Log("ERROR", $"翻译发生错误: {ex.Message}");
                MessageBox.Show("翻译发生错误: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressGrid.Visibility = Visibility.Collapsed;
                ReadyLabel.Text = "就绪";
                ReadyLabel.Foreground = System.Windows.Media.Brushes.Black;
                ReadyLabel.Visibility = Visibility.Visible;

                ExtractBtn.IsEnabled = true;
                TranslateBtn.IsEnabled = true;

                // Force grid refresh
                ProofreadingGrid.LoadData(_extractedItems);

                // Release EPLAN references
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private void WriteBackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedItems == null || _extractedItems.Count == 0) return;

            try
            {
                Log("INFO", "开始将翻译文本回写到 EPLAN 图纸...");
                string sourceLangStr = ((ComboBoxItem)SourceLangComboBox.SelectedItem).Content.ToString();
                ISOCode.Language sourceLang = MapLanguage(sourceLangStr);
                string targetLangStr = ((ComboBoxItem)TargetLangComboBox.SelectedItem).Content.ToString();
                ISOCode.Language targetLang = MapLanguage(targetLangStr);

                // 1. Read EPLAN System dictionary and compare with translations
                var conflictItems = new List<TranslationItem>();
                var dictionaryTranslations = new Dictionary<string, string>(); // SourceText -> EPLAN existing translation

                try
                {
                    using (new LockingStep())
                    {
                        using (var db = new MDTranslationDatabase())
                        {
                            db.Open(true); // Open Read-Only
                            foreach (var item in _extractedItems)
                            {
                                if (item.Status != "Translated" && item.Status != "Glossary") continue;
                                if (string.IsNullOrEmpty(item.TranslatedText)) continue;

                                if (dictionaryTranslations.ContainsKey(item.SourceText)) continue;

                                MultiLangString mls = new MultiLangString();
                                mls.AddString(sourceLang, item.SourceText);
                                string comment = "";
                                if (db.FindKeyword(sourceLang, ref mls, ref comment))
                                {
                                    string existingTranslation = mls.GetString(targetLang);
                                    if (!string.IsNullOrEmpty(existingTranslation))
                                    {
                                        dictionaryTranslations[item.SourceText] = existingTranslation;
                                        if (existingTranslation != item.TranslatedText)
                                        {
                                            conflictItems.Add(item);
                                        }
                                    }
                                }
                            }
                            db.Close();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Dictionary could be offline or not selected; warn the user but proceed to draw directly
                    Log("WARNING", "自动读取 EPLAN 系统翻译词典库失败: " + ex.Message);
                    MessageBox.Show("自动读取 EPLAN 系统翻译词典库失败:\n" + ex.Message + "\n\n系统将跳过词库校对，直接回写到图纸。", "词典读取提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                bool overwrite = true;
                if (conflictItems.Count > 0)
                {
                    Log("WARNING", $"检测到有 {conflictItems.Count} 个词条在 EPLAN 软件的系统翻译词典中存在冲突。");
                    // Found conflicts! Prompt the user
                    string msg = $"检测到有 {conflictItems.Count} 个词条在 EPLAN 软件的系统翻译词典中已存在，但新翻译的译文与词典内容不一致：\n\n" +
                                 $"• 点击【是 (Yes)】批量覆盖：使用新翻译覆盖 EPLAN 软件词典库，并写入图纸。\n" +
                                 $"• 点击【否 (No)】批量跳过：保留 EPLAN 软件词典中原有的译文，并使用原有译文更新图纸。\n" +
                                 $"• 点击【取消 (Cancel)】中止回写操作。\n\n" +
                                 $"冲突示例：\n" +
                                 $"原文: \"{conflictItems[0].SourceText}\"\n" +
                                 $"系统词典: \"{dictionaryTranslations[conflictItems[0].SourceText]}\"\n" +
                                 $"智能译文: \"{conflictItems[0].TranslatedText}\"";

                    MessageBoxResult result = MessageBox.Show(msg, "翻译词条冲突提示", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Cancel)
                    {
                        Log("INFO", "用户取消了回写图纸操作。");
                        return;
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        overwrite = false;
                        // Replace translated text with EPLAN dictionary translation for consistencies
                        foreach (var item in _extractedItems)
                        {
                            if (dictionaryTranslations.TryGetValue(item.SourceText, out string existingTrans))
                            {
                                item.TranslatedText = existingTrans;
                                item.Status = "Glossary"; // Use Eplan dictionary matched translation
                            }
                        }
                        // Refresh data grid
                        ProofreadingGrid.LoadData(_extractedItems);
                    }
                }

                // 2. Perform write back to EPLAN drawings
                var itemsByProject = _extractedItems
                    .GroupBy(item => item.ProjectFullName)
                    .Where(g => !string.IsNullOrEmpty(g.Key))
                    .ToList();

                if (itemsByProject.Count == 0) return;

                bool allSuccess = true;
                List<string> allErrors = new List<string>();

                using (new LockingStep())
                {
                    foreach (var group in itemsByProject)
                    {
                        string projPath = group.Key;
                        Project proj = new ProjectManager().OpenProjects.FirstOrDefault(p => p.ProjectFullName == projPath);
                        if (proj != null && proj.IsValid)
                        {
                            List<string> projectErrors;
                            bool success = EplanTextExtractor.WriteTexts(proj, group.ToList(), sourceLang, targetLang, out projectErrors);
                            if (!success) allSuccess = false;
                            if (projectErrors != null && projectErrors.Count > 0)
                            {
                                allErrors.AddRange(projectErrors);
                            }
                        }
                        else
                        {
                            allSuccess = false;
                            allErrors.Add($"项目未打开或无效: {projPath}");
                        }
                    }
                }

                // 3. Write back to EPLAN master translation dictionary (if enabled and user selected overwrite)
                if (overwrite && SettingsManager.Current.UseNativeEplanDictionary)
                {
                    try
                    {
                        using (new LockingStep())
                        {
                            using (var db = new MDTranslationDatabase())
                            {
                                db.Open(false); // Open Read-Write
                                foreach (var item in _extractedItems)
                                {
                                    if (item.Status != "Translated" && item.Status != "Glossary") continue;
                                    if (string.IsNullOrEmpty(item.TranslatedText)) continue;

                                    MultiLangString mls = new MultiLangString();
                                    mls.AddString(sourceLang, item.SourceText);
                                    string comment = "";
                                    if (db.FindKeyword(sourceLang, ref mls, ref comment))
                                    {
                                        mls.AddString(targetLang, item.TranslatedText);
                                        db.EditKeyword(item.SourceText, sourceLang, mls, db.Languages, comment);
                                    }
                                    else
                                    {
                                        mls.AddString(sourceLang, item.SourceText);
                                        mls.AddString(targetLang, item.TranslatedText);
                                        db.AddKeyword(mls, db.Languages);
                                    }
                                }
                                db.Close();
                            }
                        }
                        Log("SUCCESS", "同步更新 EPLAN 软件内置原生 SQL 设备词库词典库完成。");
                    }
                    catch (Exception ex)
                    {
                        Log("WARNING", "自动回写到 EPLAN 词典库失败: " + ex.Message);
                        MessageBox.Show("自动回写到 EPLAN 词典库失败:\n" + ex.Message, "词典更新警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                if (allErrors.Count == 0 && allSuccess)
                {
                    Log("SUCCESS", "所有翻译文本已成功回写至 EPLAN 图纸！");
                    MessageBox.Show("所有翻译文本已成功回写至 EPLAN 图纸！", "回写成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    WriteBackBtn.IsEnabled = false;
                }
                else
                {
                    Log("ERROR", $"部分或全部项目回写图纸失败，共 {allErrors.Count} 个错误。");
                    string logFile = null;
                    if (itemsByProject.Count > 0)
                    {
                        try
                        {
                            string projDir = System.IO.Path.GetDirectoryName(itemsByProject.First().Key);
                            logFile = System.IO.Path.Combine(projDir, "Translation_Errors.log");
                            System.IO.File.WriteAllLines(logFile, allErrors, System.Text.Encoding.UTF8);
                        }
                        catch { }
                    }

                    string errorSummary = string.Join("\n", allErrors.Take(5));
                    if (allErrors.Count > 5)
                    {
                        errorSummary += $"\n... 以及其他 {allErrors.Count - 5} 个错误。";
                    }

                    string message = $"部分或全部项目回写图纸失败。\n\n具体错误概要：\n{errorSummary}";
                    if (!string.IsNullOrEmpty(logFile))
                    {
                        message += $"\n\n完整错误日志已保存至：\n{logFile}";
                    }
                    MessageBox.Show(message, "写入失败/警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"回写发生错误: {ex.Message}");
                MessageBox.Show("回写发生错误: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private void UndoBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log("INFO", "用户触发一键撤销恢复操作...");
                MessageBoxResult result = MessageBox.Show(
                    "确定要撤销本次翻译并恢复图纸至翻译前的状态吗？",
                    "确认撤销",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    bool success = BackupService.RestoreLastBackup();
                    if (success)
                    {
                        Log("SUCCESS", "项目已成功恢复至翻译前状态。");
                        MessageBox.Show("项目已成功恢复至翻译前状态。", "撤销完成", MessageBoxButton.OK, MessageBoxImage.Information);
                        _extractedItems.Clear();
                        ProofreadingGrid.LoadData(_extractedItems);
                        CountLabel.Text = "提取条目: 0";
                        TranslateBtn.IsEnabled = false;
                        WriteBackBtn.IsEnabled = false;
                    }
                    else
                    {
                        Log("ERROR", "恢复失败：未找到可用的最近翻译备份，或备份项目被锁定。");
                        MessageBox.Show("恢复失败：未找到可用的最近翻译备份，或备份项目在恢复时被锁定。", "撤销失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"撤销操作失败: {ex.Message}");
                MessageBox.Show("撤销操作失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string MapLanguageShortcut(string langName)
        {
            if (string.IsNullOrEmpty(langName)) return "en_US";
            string clean = langName.Trim();
            int parenIdx = clean.IndexOf('(');
            if (parenIdx >= 0)
            {
                clean = clean.Substring(0, parenIdx).Trim();
            }
            return clean;
        }

        // ==================== TAB 2: TRANSLATION SETTINGS ====================

        private void LoadSettings()
        {
            var config = SettingsManager.Current;

            // Set ComboBox selection for Engine
            if (EngineComboBox != null)
            {
                foreach (ComboBoxItem item in EngineComboBox.Items)
                {
                    if (item.Content.ToString() == config.SelectedEngine)
                    {
                        item.IsSelected = true;
                        break;
                    }
                }
            }

            // Set keys and config
            if (DeepseekKeyBox != null) DeepseekKeyBox.Text = config.DeepseekApiKey;
            if (DeepseekUrlBox != null) DeepseekUrlBox.Text = config.DeepseekEndpoint;
            if (DeepseekModelBox != null) DeepseekModelBox.Text = config.DeepseekModel;

            if (ZhipuKeyBox != null) ZhipuKeyBox.Text = config.ZhipuApiKey;
            if (ZhipuUrlBox != null) ZhipuUrlBox.Text = config.ZhipuEndpoint;
            if (ZhipuModelBox != null) ZhipuModelBox.Text = config.ZhipuModel;

            if (DeeplKeyBox != null) DeeplKeyBox.Text = config.DeeplApiKey;
            if (GoogleKeyBox != null) GoogleKeyBox.Text = config.GoogleApiKey;

            if (NativeDictCheckBox != null)
            {
                NativeDictCheckBox.IsChecked = config.UseNativeEplanDictionary;
            }

            // Set Regex rules
            if (RegexRulesBox != null)
            {
                RegexRulesBox.Text = string.Join(Environment.NewLine, config.RegexProtectionRules);
            }

            _regexRules.Clear();
            foreach (var rule in config.RegexProtectionRules)
            {
                if (string.IsNullOrWhiteSpace(rule)) continue;
                if (FilterService.ParseRule(rule, out string pattern, out string description, out bool isActive))
                {
                    _regexRules.Add(new RegexRuleItem
                    {
                        Pattern = pattern,
                        Description = string.IsNullOrEmpty(description) ? "系统默认保护规则" : description,
                        IsActive = isActive
                    });
                }
            }

            if (ActiveRulesCountLabel != null)
            {
                ActiveRulesCountLabel.Text = $"{_regexRules.Count(r => r.IsActive)} 条激活";
            }

            // Load glossary paths
            _tempGlossaryPaths = new List<string>(config.GlossaryPaths);
            UpdateGlossaryList();

            // Re-load the glossaries in memory
            try
            {
                GlossaryManager.Glossary.Clear();
                
                // Ensure manual glossary file is in paths
                string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TranslationEplAddIn.Tool");
                if (!Directory.Exists(appDataFolder))
                {
                    Directory.CreateDirectory(appDataFolder);
                }
                string manualGlossaryPath = Path.Combine(appDataFolder, "manual_glossary.csv");
                if (!File.Exists(manualGlossaryPath))
                {
                    File.WriteAllText(manualGlossaryPath, "", Encoding.UTF8);
                }
                if (!_tempGlossaryPaths.Contains(manualGlossaryPath))
                {
                    _tempGlossaryPaths.Add(manualGlossaryPath);
                    config.GlossaryPaths.Add(manualGlossaryPath);
                    SettingsManager.Save();
                }

                foreach (var path in _tempGlossaryPaths)
                {
                    GlossaryManager.LoadGlossary(path);
                }
                UpdateGlossaryTermsList();
            }
            catch (Exception ex)
            {
                Log("ERROR", $"加载词库文件失败: {ex.Message}");
            }

            UpdateSandboxOutput();
            UpdateLogFilterButtonStyles();
        }

        private void UpdateGlossaryList()
        {
            if (GlossaryListBox != null)
            {
                GlossaryListBox.Items.Clear();
                foreach (var path in _tempGlossaryPaths)
                {
                    GlossaryListBox.Items.Add(Path.GetFileName(path) + " (" + path + ")");
                }
            }
        }

        private void SaveSettings()
        {
            var config = SettingsManager.Current;

            // Save engine
            if (EngineComboBox != null && EngineComboBox.SelectedItem != null)
            {
                config.SelectedEngine = ((ComboBoxItem)EngineComboBox.SelectedItem).Content.ToString();
            }

            // Save keys and config
            if (DeepseekKeyBox != null) config.DeepseekApiKey = DeepseekKeyBox.Text;
            if (DeepseekUrlBox != null) config.DeepseekEndpoint = DeepseekUrlBox.Text;
            if (DeepseekModelBox != null) config.DeepseekModel = DeepseekModelBox.Text;

            if (ZhipuKeyBox != null) config.ZhipuApiKey = ZhipuKeyBox.Text;
            if (ZhipuUrlBox != null) config.ZhipuEndpoint = ZhipuUrlBox.Text;
            if (ZhipuModelBox != null) config.ZhipuModel = ZhipuModelBox.Text;

            if (DeeplKeyBox != null) config.DeeplApiKey = DeeplKeyBox.Text;
            if (GoogleKeyBox != null) config.GoogleApiKey = GoogleKeyBox.Text;

            // Save strategies
            config.AppendMode = false;
            if (NativeDictCheckBox != null)
            {
                config.UseNativeEplanDictionary = NativeDictCheckBox.IsChecked ?? false;
            }

            // Save Regex rules
            config.RegexProtectionRules = _regexRules
                .Select(r => $"{r.Pattern}|{r.Description}|{(r.IsActive ? "true" : "false")}")
                .ToList();
            if (RegexRulesBox != null)
            {
                RegexRulesBox.Text = string.Join(Environment.NewLine, config.RegexProtectionRules);
            }

            // Save glossary
            config.GlossaryPaths = _tempGlossaryPaths;

            // Persist to disk
            SettingsManager.Save();
            
            // Re-load the glossaries in memory
            try
            {
                GlossaryManager.Glossary.Clear();
                foreach (var path in config.GlossaryPaths)
                {
                    GlossaryManager.LoadGlossary(path);
                }
                UpdateGlossaryTermsList();
            }
            catch (Exception ex)
            {
                Log("ERROR", $"保存设置后重新加载词库失败: {ex.Message}");
            }

            UpdateEngineLabel();
        }

        private void EngineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EngineComboBox == null) return;
            if (EngineComboBox.SelectedItem == null) return;
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
                    MessageBox.Show("API 连接测试成功！连接正常。", "测试成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("API 连接测试失败，请检查配置。", "测试失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "测试失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = originalContent;
            }
        }


        private void AddGlossary_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Terminology Files (*.csv;*.xlsx;*.tmx;*.tbx)|*.csv;*.xlsx;*.tmx;*.tbx|CSV files (*.csv)|*.csv|Excel files (*.xlsx)|*.xlsx|TMX files (*.tmx)|*.tmx|TBX files (*.tbx)|*.tbx";
            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                AddGlossaryPath(filePath);
            }
        }

        private void RemoveGlossary_Click(object sender, RoutedEventArgs e)
        {
            int selectedIndex = GlossaryListBox.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < _tempGlossaryPaths.Count)
            {
                string path = _tempGlossaryPaths[selectedIndex];
                _tempGlossaryPaths.RemoveAt(selectedIndex);
                UpdateGlossaryList();
                
                // Reload glossaries to reflect removal in active memory
                GlossaryManager.Glossary.Clear();
                foreach (var p in _tempGlossaryPaths)
                {
                    GlossaryManager.LoadGlossary(p);
                }
                UpdateGlossaryTermsList(GlossaryFilterBox.Text);
                Log("INFO", $"已移除词库文件: {Path.GetFileName(path)}");
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveSettings();
                MessageBox.Show("配置已成功保存并应用！", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存配置发生错误: " + ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== TAB 3: EXPORT & LOG REPORT ====================

        private void UpdateExportStats()
        {
            // Stats cards removed from UI
        }

        private void BrowseExportPathBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "CSV Report (*.csv)|*.csv";
            saveFileDialog.DefaultExt = ".csv";

            if (saveFileDialog.ShowDialog() == true)
            {
                ExportPathBox.Text = saveFileDialog.FileName;
            }
        }

        private void DoExportBtn_Click(object sender, RoutedEventArgs e)
        {
            string filePath = ExportPathBox.Text;
            if (string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show("请先选择导出路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_extractedItems == null || _extractedItems.Count == 0)
            {
                MessageBox.Show("没有可导出的翻译数据，请在 [翻译项目] 选项卡中先提取并翻译文本。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                ExportToCsv(filePath, _extractedItems);
                MessageBox.Show("翻译报告已成功导出！", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出日志报告失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToCsv(string filePath, List<TranslationItem> items)
        {
            using (StreamWriter sw = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                sw.WriteLine("源文本,译文,状态,翻译引擎,页面名称,页面标识,对象标识");
                foreach (var item in items)
                {
                    string src = EscapeCsv(item.SourceText);
                    string trans = EscapeCsv(item.TranslatedText);
                    string status = EscapeCsv(item.Status);
                    string engine = EscapeCsv(item.EngineUsed);
                    string page = EscapeCsv(item.PageName);
                    string pageId = EscapeCsv(item.PageId);
                    string objId = EscapeCsv(item.ObjectId);
                    sw.WriteLine($"{src},{trans},{status},{engine},{page},{pageId},{objId}");
                }
            }
        }

        private string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "\"\"";
            val = val.Replace("\"", "\"\"");
            if (val.Contains(",") || val.Contains("\n") || val.Contains("\r") || val.Contains("\""))
            {
                return $"\"{val}\"";
            }
            return val;
        }

        private void ExportToHtml(string filePath, List<TranslationItem> items)
        {
            int total = items.Count;
            int translated = items.Count(i => i.Status == "Translated");
            int glossary = items.Count(i => i.Status == "Glossary");
            int error = items.Count(i => i.Status == "Error");
            int pending = items.Count(i => i.Status == "Pending");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset='utf-8'>");
            sb.AppendLine("<title>EPLAN 翻译日志报告</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; background-color: #f5f5f5; color: #333; margin: 20px; }");
            sb.AppendLine("h1 { color: #007acc; border-bottom: 2px solid #007acc; padding-bottom: 10px; }");
            sb.AppendLine(".summary-box { display: flex; gap: 15px; margin-bottom: 20px; }");
            sb.AppendLine(".stat-card { background: white; padding: 15px 25px; border-radius: 6px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); flex: 1; text-align: center; }");
            sb.AppendLine(".stat-card h3 { margin: 0; font-size: 14px; color: #666; }");
            sb.AppendLine(".stat-card p { margin: 5px 0 0 0; font-size: 24px; font-weight: bold; color: #007acc; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; background: white; border-radius: 6px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
            sb.AppendLine("th, td { padding: 12px 15px; text-align: left; border-bottom: 1px solid #ddd; }");
            sb.AppendLine("th { background-color: #007acc; color: white; }");
            sb.AppendLine("tr:hover { background-color: #f9f9f9; }");
            sb.AppendLine(".status-translated { color: #1d6f42; font-weight: bold; }");
            sb.AppendLine(".status-glossary { color: #8a2be2; font-weight: bold; }");
            sb.AppendLine(".status-error { color: #a12626; font-weight: bold; }");
            sb.AppendLine(".status-pending { color: #666; }");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<h1>EPLAN 翻译日志报告</h1>");
            sb.AppendLine("<div class='summary-box'>");
            sb.AppendLine($"<div class='stat-card'><h3>总条目</h3><p>{total}</p></div>");
            sb.AppendLine($"<div class='stat-card'><h3>已翻译</h3><p style='color:#1d6f42'>{translated}</p></div>");
            sb.AppendLine($"<div class='stat-card'><h3>词典匹配</h3><p style='color:#8a2be2'>{glossary}</p></div>");
            sb.AppendLine($"<div class='stat-card'><h3>待处理</h3><p style='color:#666'>{pending}</p></div>");
            sb.AppendLine($"<div class='stat-card'><h3>失败</h3><p style='color:#a12626'>{error}</p></div>");
            sb.AppendLine("</div>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>源文本</th><th>译文</th><th>状态</th><th>引擎</th><th>页面</th><th>对象ID</th></tr></thead>");
            sb.AppendLine("<tbody>");
            foreach (var item in items)
            {
                string statusClass = "status-pending";
                if (item.Status == "Translated") statusClass = "status-translated";
                else if (item.Status == "Glossary") statusClass = "status-glossary";
                else if (item.Status == "Error") statusClass = "status-error";

                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{System.Security.SecurityElement.Escape(item.SourceText)}</td>");
                sb.AppendLine($"<td>{System.Security.SecurityElement.Escape(item.TranslatedText ?? "")}</td>");
                sb.AppendLine($"<td class='{statusClass}'>{item.Status}</td>");
                sb.AppendLine($"<td>{item.EngineUsed ?? ""}</td>");
                sb.AppendLine($"<td>{System.Security.SecurityElement.Escape(item.PageName)}</td>");
                sb.AppendLine($"<td>{item.ObjectId}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody>");
            sb.AppendLine("</table>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        // ==================== INTERACTIVE REGEX & GLOSSARY & SANDBOX ====================

        private void RegexItem_StateChanged(object sender, RoutedEventArgs e)
        {
            if (ActiveRulesCountLabel != null && _regexRules != null)
            {
                ActiveRulesCountLabel.Text = $"{_regexRules.Count(r => r.IsActive)} 条激活";
                UpdateSandboxOutput();
            }
        }

        private void DeleteRegexRule_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button?.DataContext as RegexRuleItem;
            if (item != null)
            {
                _regexRules.Remove(item);
                if (ActiveRulesCountLabel != null)
                {
                    ActiveRulesCountLabel.Text = $"{_regexRules.Count(r => r.IsActive)} 条激活";
                }
                UpdateSandboxOutput();
                Log("INFO", $"已删除正则表达式规则: {item.Pattern}");
            }
        }

        private void AddRegexRule_Click(object sender, RoutedEventArgs e)
        {
            string pattern = NewRegexPatternBox.Text.Trim();
            string desc = NewRegexDescBox.Text.Trim();

            if (string.IsNullOrEmpty(pattern))
            {
                MessageBox.Show("正则表达式模式不能为空。", "输入校验", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                new System.Text.RegularExpressions.Regex(pattern);
            }
            catch (ArgumentException)
            {
                MessageBox.Show("输入的正则表达式格式无效，请检查语法。", "语法错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrEmpty(desc))
            {
                desc = "用户自定义规则";
            }

            var newItem = new RegexRuleItem { Pattern = pattern, Description = desc, IsActive = true };
            _regexRules.Add(newItem);
            
            NewRegexPatternBox.Text = string.Empty;
            NewRegexDescBox.Text = string.Empty;

            if (ActiveRulesCountLabel != null)
            {
                ActiveRulesCountLabel.Text = $"{_regexRules.Count(r => r.IsActive)} 条激活";
            }
            UpdateSandboxOutput();
            Log("SUCCESS", $"成功添加防污染正则表达式隔离规则: {pattern}");
        }

        private void SandboxInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSandboxOutput();
        }

        private void UpdateSandboxOutput()
        {
            if (SandboxInputBox == null || SandboxOutputBox == null || SandboxCountLabel == null) return;

            string input = SandboxInputBox.Text;
            if (string.IsNullOrEmpty(input))
            {
                SandboxOutputBox.Text = string.Empty;
                SandboxCountLabel.Text = "0 处处理标记";
                return;
            }

            var activeRuleStrings = _regexRules
                .Where(r => r.IsActive)
                .Select(r => $"{r.Pattern}|{r.Description}|true")
                .ToList();

            var session = FilterService.Protect(input, activeRuleStrings);
            SandboxOutputBox.Text = session.TokenizedText;
            SandboxCountLabel.Text = $"{session.Tokens.Count} 处处理标记";
        }

        private void DragDropArea_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "Terminology Files (*.csv;*.xlsx;*.tmx;*.tbx)|*.csv;*.xlsx;*.tmx;*.tbx|CSV files (*.csv)|*.csv|Excel files (*.xlsx)|*.xlsx|TMX files (*.tmx)|*.tmx|TBX files (*.tbx)|*.tbx";
                if (openFileDialog.ShowDialog() == true)
                {
                    string filePath = openFileDialog.FileName;
                    AddGlossaryPath(filePath);
                }
            }
        }

        private void AddGlossaryPath(string filePath)
        {
            if (!_tempGlossaryPaths.Contains(filePath))
            {
                _tempGlossaryPaths.Add(filePath);
                UpdateGlossaryList();
                
                try
                {
                    GlossaryManager.LoadGlossary(filePath);
                    UpdateGlossaryTermsList(GlossaryFilterBox.Text);
                    Log("INFO", $"成功导入并解析术语词库文件: {Path.GetFileName(filePath)}");
                }
                catch (Exception ex)
                {
                    Log("ERROR", $"加载词库文件失败 {filePath}: {ex.Message}");
                }
            }
        }

        private void GlossaryFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateGlossaryTermsList(GlossaryFilterBox.Text.Trim());
        }

        private void UpdateGlossaryTermsList(string filter = "")
        {
            _glossaryTerms.Clear();
            foreach (var kvp in GlossaryManager.Glossary)
            {
                string src = kvp.Key;
                string tgt = kvp.Value;
                
                if (!string.IsNullOrEmpty(filter))
                {
                    if (!src.Contains(filter) && !tgt.Contains(filter))
                    {
                        continue;
                    }
                }

                string category = "通用";
                
                _glossaryTerms.Add(new GlossaryDisplayItem
                {
                    Source = src,
                    Target = tgt,
                    Category = category
                });
            }

            if (GlossaryCountLabel != null)
            {
                GlossaryCountLabel.Text = $"{_glossaryTerms.Count} 条记录";
            }
        }

        private void DeleteGlossaryTerm_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button?.DataContext as GlossaryDisplayItem;
            if (item == null) return;

            if (GlossaryManager.Glossary.ContainsKey(item.Source))
            {
                GlossaryManager.Glossary.Remove(item.Source);
            }

            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TranslationEplAddIn.Tool");
            string manualGlossaryPath = Path.Combine(appDataFolder, "manual_glossary.csv");
            if (File.Exists(manualGlossaryPath))
            {
                try
                {
                    var lines = File.ReadAllLines(manualGlossaryPath).ToList();
                    var newLines = new List<string>();
                    foreach (var line in lines)
                    {
                        var parts = line.Split(new[] { ',', ';' }, 2);
                        if (parts.Length == 2)
                        {
                            string src = parts[0].Trim().Trim('"');
                            if (src.Equals(item.Source, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }
                        newLines.Add(line);
                    }
                    File.WriteAllLines(manualGlossaryPath, newLines, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Log("ERROR", $"从本地快捷词汇表中删除失败: {ex.Message}");
                }
            }

            UpdateGlossaryTermsList(GlossaryFilterBox.Text);
            Log("INFO", $"已成功移除术语对照: {item.Source} -> {item.Target}");
        }

        private void AddGlossaryTerm_Click(object sender, RoutedEventArgs e)
        {
            string source = NewGlossarySourceBox.Text.Trim();
            string target = NewGlossaryTargetBox.Text.Trim();
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            {
                MessageBox.Show("原文与译文均不能为空。", "输入校验", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TranslationEplAddIn.Tool");
            if (!Directory.Exists(appDataFolder))
            {
                Directory.CreateDirectory(appDataFolder);
            }
            string manualGlossaryPath = Path.Combine(appDataFolder, "manual_glossary.csv");

            try
            {
                string escSource = source.Replace("\"", "\"\"");
                string escTarget = target.Replace("\"", "\"\"");
                if (escSource.Contains(",") || escSource.Contains("\n") || escSource.Contains("\r")) escSource = $"\"{escSource}\"";
                if (escTarget.Contains(",") || escTarget.Contains("\n") || escTarget.Contains("\r")) escTarget = $"\"{escTarget}\"";

                File.AppendAllLines(manualGlossaryPath, new[] { $"{escSource},{escTarget}" }, Encoding.UTF8);
                
                GlossaryManager.Glossary[source] = target;

                Log("SUCCESS", $"手动快捷录入术语对照成功: {source} -> {target}");
                
                NewGlossarySourceBox.Text = string.Empty;
                NewGlossaryTargetBox.Text = string.Empty;

                UpdateGlossaryTermsList(GlossaryFilterBox.Text);
            }
            catch (Exception ex)
            {
                Log("ERROR", $"手动录入术语保存失败: {ex.Message}");
                MessageBox.Show($"手动录入术语保存失败: {ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== INTERACTIVE SYSLOG ====================

        private List<LogEntry> _allLogEntries = new List<LogEntry>();

        private void Log(string level, string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Log(level, message));
                return;
            }

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                Level = level.ToUpper(),
                Message = message,
                TextBrush = GetBrushForLevel(level)
            };

            _allLogEntries.Add(entry);
            ApplyLogFilter();
        }

        private void ApplyLogFilter()
        {
            _logEntries.Clear();
            foreach (var entry in _allLogEntries)
            {
                // Level filter
                if (!_logLevelFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    if (!entry.Level.Equals(_logLevelFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                // Search query filter
                if (!string.IsNullOrEmpty(_logSearchQuery))
                {
                    if (entry.Message == null || !entry.Message.Contains(_logSearchQuery))
                    {
                        continue;
                    }
                }

                _logEntries.Add(entry);
            }

            if (LogListView != null && LogListView.Items.Count > 0)
            {
                LogListView.ScrollIntoView(LogListView.Items[LogListView.Items.Count - 1]);
            }
        }

        private void LogFilterBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            string content = btn.Content.ToString();
            if (btn == LogFilterAllBtn)
            {
                _logLevelFilter = "ALL";
            }
            else
            {
                _logLevelFilter = content.ToUpper();
            }

            UpdateLogFilterButtonStyles();
            ApplyLogFilter();
        }

        private void UpdateLogFilterButtonStyles()
        {
            if (LogFilterAllBtn == null || LogFilterInfoBtn == null || LogFilterSuccessBtn == null || 
                LogFilterWarningBtn == null || LogFilterErrorBtn == null) return;

            var normalBg = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FAF9F6"));
            var normalFg = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#626058"));
            var activeBg = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3D3D3A"));
            var activeFg = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF"));

            LogFilterAllBtn.Background = normalBg; LogFilterAllBtn.Foreground = normalFg;
            LogFilterInfoBtn.Background = normalBg; LogFilterInfoBtn.Foreground = normalFg;
            LogFilterSuccessBtn.Background = normalBg; LogFilterSuccessBtn.Foreground = normalFg;
            LogFilterWarningBtn.Background = normalBg; LogFilterWarningBtn.Foreground = normalFg;
            LogFilterErrorBtn.Background = normalBg; LogFilterErrorBtn.Foreground = normalFg;

            if (_logLevelFilter == "ALL") { LogFilterAllBtn.Background = activeBg; LogFilterAllBtn.Foreground = activeFg; }
            else if (_logLevelFilter == "INFO") { LogFilterInfoBtn.Background = activeBg; LogFilterInfoBtn.Foreground = activeFg; }
            else if (_logLevelFilter == "SUCCESS") { LogFilterSuccessBtn.Background = activeBg; LogFilterSuccessBtn.Foreground = activeFg; }
            else if (_logLevelFilter == "WARNING") { LogFilterWarningBtn.Background = activeBg; LogFilterWarningBtn.Foreground = activeFg; }
            else if (_logLevelFilter == "ERROR") { LogFilterErrorBtn.Background = activeBg; LogFilterErrorBtn.Foreground = activeFg; }
        }

        private void LogSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _logSearchQuery = LogSearchTextBox.Text.Trim();
            ApplyLogFilter();
        }

        private void ClearLogBtn_Click(object sender, RoutedEventArgs e)
        {
            _allLogEntries.Clear();
            ApplyLogFilter();
            Log("INFO", "运行日志流已清空。");
        }

        private static System.Windows.Media.Brush GetBrushForLevel(string level)
        {
            string hexColor = "#727068";
            switch (level.ToUpper())
            {
                case "INFO":
                    hexColor = "#0EA5E9";
                    break;
                case "SUCCESS":
                    hexColor = "#1D6F42";
                    break;
                case "WARNING":
                    hexColor = "#D97706";
                    break;
                case "ERROR":
                    hexColor = "#A12626";
                    break;
            }
            return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor));
        }

        // ==================== HELPERS ====================

        private ISOCode.Language MapLanguage(string langName)
        {
            if (string.IsNullOrEmpty(langName)) return ISOCode.Language.L_en_US;
            string clean = langName.Trim();
            int parenIdx = clean.IndexOf('(');
            if (parenIdx >= 0)
            {
                clean = clean.Substring(0, parenIdx).Trim();
            }

            switch (clean.ToLower())
            {
                case "cs_cz": return ISOCode.Language.L_cs_CZ;
                case "da_dk": return ISOCode.Language.L_da_DK;
                case "de_de": return ISOCode.Language.L_de_DE;
                case "en_us": return ISOCode.Language.L_en_US;
                case "es_es": return ISOCode.Language.L_es_ES;
                case "fr_fr": return ISOCode.Language.L_fr_FR;
                case "hu_hu": return ISOCode.Language.L_hu_HU;
                case "it_it": return ISOCode.Language.L_it_IT;
                case "ja_jp": return ISOCode.Language.L_ja_JP;
                case "ko_kr": return ISOCode.Language.L_ko_KR;
                case "nl_nl": return ISOCode.Language.L_nl_NL;
                case "pl_pl": return ISOCode.Language.L_pl_PL;
                case "pt_br": return ISOCode.Language.L_pt_BR;
                case "pt_pt": return ISOCode.Language.L_pt_PT;
                case "ro_ro": return ISOCode.Language.L_ro_RO;
                case "ru_ru": return ISOCode.Language.L_ru_RU;
                case "sv_se": return ISOCode.Language.L_sv_SE;
                case "tr_tr": return ISOCode.Language.L_tr_TR;
                case "zh_cn": return ISOCode.Language.L_zh_CN;
                case "english": return ISOCode.Language.L_en_US;
                case "german": return ISOCode.Language.L_de_DE;
                case "chinese": return ISOCode.Language.L_zh_CN;
                default:
                    return ISOCode.Language.L_en_US;
            }
        }

        private static void DoEvents()
        {
            System.Windows.Threading.DispatcherFrame frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new System.Windows.Threading.DispatcherOperationCallback(ExitFrame), frame);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private static object ExitFrame(object f)
        {
            ((System.Windows.Threading.DispatcherFrame)f).Continue = false;
            return null;
        }

        private class SyncProgress : IProgress<int>
        {
            private readonly Action<int> _handler;
            public SyncProgress(Action<int> handler)
            {
                _handler = handler;
            }
            public void Report(int value)
            {
                _handler?.Invoke(value);
            }
        }
    }

    public class LogEntry
    {
        public string Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public System.Windows.Media.Brush TextBrush { get; set; }
    }
}

