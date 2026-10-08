using System;
using System.IO;
using System.Linq;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Gui;

namespace TranslationEplAddIn.Tool
{
    public class TranslationAddIn : IEplAddIn
    {
        private const string TabName = "EPLAN 翻译助手";

        public bool OnRegister(ref bool bLoadOnStart)
        {
            bLoadOnStart = true;
            return true;
        }

        public bool OnUnregister()
        {
            return true;
        }

        public bool OnInit()
        {
            return true;
        }

        public bool OnInitGui()
        {
            try
            {
                CreateRibbonUI();
            }
            catch (Exception ex)
            {
                // Fallback or log if Ribbon API is not supported in legacy versions
                Console.WriteLine("Failed to create Ribbon UI: " + ex.Message);
            }
            return true;
        }

        public bool OnExit()
        {
            try
            {
                RemoveRibbonUI();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to clean up Ribbon UI");
            }
            return true;
        }

        private string GetIconPath(string filename, string svgContent)
        {
            try
            {
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TranslationEplAddIn", "resources");
                if (!Directory.Exists(appDataDir))
                {
                    Directory.CreateDirectory(appDataDir);
                }
                string filePath = Path.Combine(appDataDir, filename);
                File.WriteAllText(filePath, svgContent);
                return filePath;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to create Ribbon UI");
                return string.Empty;
            }
        }

        private void CreateRibbonUI()
        {
            RibbonBar ribbonBar = new RibbonBar();
            
            // Check if Tab already exists to prevent duplicate creation
            RibbonTab existingTab = ribbonBar.Tabs.FirstOrDefault(t => t.Name == TabName);
            if (existingTab == null)
            {
                RibbonTab tab = ribbonBar.AddTab(TabName);
                
                // Group 1: 翻译面板
                RibbonCommandGroup mainGroup = tab.AddCommandGroup("翻译面板");
                string mainIconPath = GetIconPath("main_translate.svg", GetMainPanelSvg());
                RibbonIcon mainIcon = !string.IsNullOrEmpty(mainIconPath) ? new RibbonIcon(mainIconPath) : null;
                if (mainIcon != null)
                {
                    mainGroup.AddCommand("翻译主面板", "ShowTranslatePanel", "打开智能翻译主面板", "智能翻译主面板", mainIcon);
                }
                else
                {
                    mainGroup.AddCommand("翻译主面板", "ShowTranslatePanel", "打开智能翻译主面板", "智能翻译主面板");
                }

                // Group 2: 授权激活
                RibbonCommandGroup activateGroup = tab.AddCommandGroup("授权激活");
                string activateIconPath = GetIconPath("activate.svg", GetActivateSvg());
                RibbonIcon activateIcon = !string.IsNullOrEmpty(activateIconPath) ? new RibbonIcon(activateIconPath) : null;
                if (activateIcon != null)
                {
                    activateGroup.AddCommand("授权激活", "ShowActivationWindow", "点击进行软件授权激活与延期", "授权激活", activateIcon);
                }
                else
                {
                    activateGroup.AddCommand("授权激活", "ShowActivationWindow", "点击进行软件授权激活与延期", "授权激活");
                }
                
                // Group 3: 关于
                RibbonCommandGroup aboutGroup = tab.AddCommandGroup("关于");
                string aboutIconPath = GetIconPath("about.svg", GetAboutSvg());
                RibbonIcon aboutIcon = !string.IsNullOrEmpty(aboutIconPath) ? new RibbonIcon(aboutIconPath) : null;
                if (aboutIcon != null)
                {
                    aboutGroup.AddCommand("关于", "ShowAboutWindow", "查看软件版本及激活信息", "关于", aboutIcon);
                }
                else
                {
                    aboutGroup.AddCommand("关于", "ShowAboutWindow", "查看软件版本及激活信息", "关于");
                }
            }
        }

        private string GetMainPanelSvg()
        {
            // Modern translation composite icon (Chinese character '文' and letter 'A') in Premium Blue (#008BE5)
            return @"<svg viewBox=""0 0 24 24"" fill=""none"" xmlns=""http://www.w3.org/2000/svg""><path d=""M5 8h10M10 5v3M14 8c0 4-2.5 7-6 8M6.5 11c1.5 1.5 3.5 3 5.5 4"" stroke=""#008BE5"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""/><path d=""M12 20l4-9 4 9M13.5 17h5"" stroke=""#008BE5"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""/></svg>";
        }

        private string GetActivateSvg()
        {
            // Modern diagonal key icon in Gold (#FFB900)
            return @"<svg viewBox=""0 0 24 24"" fill=""none"" xmlns=""http://www.w3.org/2000/svg""><circle cx=""7"" cy=""17"" r=""4"" stroke=""#FFB900"" stroke-width=""2""/><path d=""M10 14l9-9M16 8l2 2M18 6l2-2"" stroke=""#FFB900"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""/></svg>";
        }

        private string GetAboutSvg()
        {
            // Modern info-circle icon in Violet (#8F59F2)
            return @"<svg viewBox=""0 0 24 24"" fill=""none"" xmlns=""http://www.w3.org/2000/svg""><circle cx=""12"" cy=""12"" r=""9"" stroke=""#8F59F2"" stroke-width=""2""/><path d=""M12 16v-4M12 8h.01"" stroke=""#8F59F2"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""/></svg>";
        }

        private void RemoveRibbonUI()
        {
            RibbonBar ribbonBar = new RibbonBar();
            RibbonTab tab = ribbonBar.Tabs.FirstOrDefault(t => t.Name == TabName);
            if (tab != null)
            {
                tab.Remove();
            }
        }
    }
}