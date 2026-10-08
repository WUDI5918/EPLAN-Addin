using System;
using System.IO;
using Eplan.EplApi.ApplicationFramework;
using System.Windows;

namespace TranslationEplAddIn.Tool
{
    internal static class LicenseHelper
    {
        public static bool ValidateLicense()
        {
            try
            {
                string licPath = License.LicenseVerifier.GetLicensePath();
                if (License.LicenseVerifier.CheckLicense(licPath, out _, out string error))
                {
                    return true;
                }

                // 未通过授权，弹出激活窗口
                var regWin = new License.UI.RegisterWindow(error);
                var mainWindowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (mainWindowHandle != IntPtr.Zero)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(regWin);
                    helper.Owner = mainWindowHandle;
                }

                return regWin.ShowDialog() == true;
            }
            catch (Exception ex)
            {
                UI.CustomMessageBoxWindow.Show("授权验证发生异常: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
    }

    // Action to show the Main Translation Panel
    public class ShowTranslatePanelAction : IEplAction
    {
        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            if (!LicenseHelper.ValidateLicense())
            {
                return true; // 拦截执行，优雅退出
            }

            try
            {
                // We'll show the MainWindow as a modeless WPF window
                UI.MainWindow mainWindow = new UI.MainWindow();
                var mainWindowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (mainWindowHandle != IntPtr.Zero)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(mainWindow);
                    helper.Owner = mainWindowHandle;
                }
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                UI.CustomMessageBoxWindow.Show("Failed to open Translation Panel: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return true;
        }

        public void GetActionProperties(ref ActionProperties actionProperties)
        {
        }

        public bool OnRegister(ref string strName, ref int nOrdinal)
        {
            strName = "ShowTranslatePanel";
            nOrdinal = 20;
            return true;
        }
    }

    // Action to show the Settings Window
    public class ShowSettingsAction : IEplAction
    {
        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            if (!LicenseHelper.ValidateLicense())
            {
                return true; // 拦截执行，优雅退出
            }

            try
            {
                UI.MainWindow settingsWindow = new UI.MainWindow(1);
                var mainWindowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (mainWindowHandle != IntPtr.Zero)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(settingsWindow);
                    helper.Owner = mainWindowHandle;
                }
                settingsWindow.Show();
            }
            catch (Exception ex)
            {
                UI.CustomMessageBoxWindow.Show("Failed to open Settings: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return true;
        }

        public void GetActionProperties(ref ActionProperties actionProperties)
        {
        }

        public bool OnRegister(ref string strName, ref int nOrdinal)
        {
            strName = "ShowSettings";
            nOrdinal = 20;
            return true;
        }
    }

    // Action to revert the last translation
    public class RevertTranslationAction : IEplAction
    {
        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            if (!LicenseHelper.ValidateLicense())
            {
                return true; // 拦截执行，优雅退出
            }

            try
            {
                MessageBoxResult result = UI.CustomMessageBoxWindow.Show(
                    "Are you sure you want to revert the last translation from the backup?",
                    "Confirm Revert",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    bool success = BackupService.RestoreLastBackup();
                    if (success)
                    {
                        UI.CustomMessageBoxWindow.Show("Translation reverted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        UI.CustomMessageBoxWindow.Show("Failed to revert translation. No valid backup found or EPLAN failed to restore.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                UI.CustomMessageBoxWindow.Show("Error during revert: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return true;
        }

        public void GetActionProperties(ref ActionProperties actionProperties)
        {
        }

        public bool OnRegister(ref string strName, ref int nOrdinal)
        {
            strName = "RevertTranslation";
            nOrdinal = 20;
            return true;
        }
    }

    // Action to show the Activation Window manually
    public class ShowActivationWindowAction : IEplAction
    {
        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            try
            {
                var regWin = new License.UI.RegisterWindow(null);
                var mainWindowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (mainWindowHandle != IntPtr.Zero)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(regWin);
                    helper.Owner = mainWindowHandle;
                }
                regWin.ShowDialog();
            }
            catch (Exception ex)
            {
                UI.CustomMessageBoxWindow.Show("Failed to open Activation window: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return true;
        }

        public void GetActionProperties(ref ActionProperties actionProperties)
        {
        }

        public bool OnRegister(ref string strName, ref int nOrdinal)
        {
            strName = "ShowActivationWindow";
            nOrdinal = 20;
            return true;
        }
    }

    // Action to show the About Window
    public class ShowAboutAction : IEplAction
    {
        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            try
            {
                var aboutWin = new License.UI.AboutWindow();
                var mainWindowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (mainWindowHandle != IntPtr.Zero)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(aboutWin);
                    helper.Owner = mainWindowHandle;
                }
                aboutWin.ShowDialog();
            }
            catch (Exception ex)
            {
                UI.CustomMessageBoxWindow.Show("Failed to open About window: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return true;
        }

        public void GetActionProperties(ref ActionProperties actionProperties)
        {
        }

        public bool OnRegister(ref string strName, ref int nOrdinal)
        {
            strName = "ShowAboutWindow";
            nOrdinal = 20;
            return true;
        }
    }

    // Action to copy the machine code directly to clipboard
    public class CopyMachineCodeAction : IEplAction
    {
        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            try
            {
                string machineCode = License.HardwareFingerprint.GetMachineCode();
                Clipboard.SetText(machineCode);
                
                string msg = $"您的本机机器码：\n\n【 {machineCode} 】\n\n已成功复制到系统剪贴板！\n请直接粘贴（Ctrl+V）并发送给研发人员获取授权文件。";
                UI.CustomMessageBoxWindow.Show(msg, "复制机器码成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UI.CustomMessageBoxWindow.Show("复制机器码失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return true;
        }

        public void GetActionProperties(ref ActionProperties actionProperties)
        {
        }

        public bool OnRegister(ref string strName, ref int nOrdinal)
        {
            strName = "CopyMachineCode";
            nOrdinal = 20;
            return true;
        }
    }
}
