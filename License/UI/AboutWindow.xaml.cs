using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace TranslationEplAddIn.Tool.License.UI
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            // Load machine code
            string machineCode = HardwareFingerprint.GetMachineCode();
            TxtMachineCode.Text = machineCode;

            // Load license information
            try
            {
                string licPath = LicenseVerifier.GetLicensePath();

                if (LicenseVerifier.CheckLicense(licPath, out LicenseInfo licInfo, out string error))
                {
                    TxtLicenseStatus.Text = $"已激活 (授权给: {licInfo.UserName})";
                    TxtLicenseStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00ADB5"));
                    TxtLicenseExpire.Text = licInfo.ExpireDate;
                }
                else
                {
                    TxtLicenseStatus.Text = "未激活 (" + error + ")";
                    TxtLicenseStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF2E63"));
                    TxtLicenseExpire.Text = "-";
                }
            }
            catch (Exception ex)
            {
                TxtLicenseStatus.Text = "检查授权异常: " + ex.Message;
                TxtLicenseStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF2E63"));
                TxtLicenseExpire.Text = "-";
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    this.DragMove();
                }
                catch (InvalidOperationException)
                {
                    // Prevent exception when DragMove is called after button release
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void CopyWeChat_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText("MyDriver_1988");
                MessageBox.Show("微信号 (MyDriver_1988) 已成功复制到剪贴板！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyMachineCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtMachineCode.Text);
                MessageBox.Show("机器码已成功复制到剪贴板！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
