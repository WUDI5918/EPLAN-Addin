using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace TranslationEplAddIn.Tool.License.UI
{
    public partial class RegisterWindow : Window
    {
        private readonly string _publicKeyXml;
        private string _selectedLicensePath;

        public RegisterWindow(string errorMessage, string publicKeyXml = LicenseVerifier.DefaultPublicKeyXml)
        {
            InitializeComponent();
            _publicKeyXml = publicKeyXml;

            // Load machine code
            TxtMachineCode.Text = HardwareFingerprint.GetMachineCode();

            // Display error if provided
            if (!string.IsNullOrEmpty(errorMessage))
            {
                TxtError.Text = errorMessage;
                ErrorBorder.Visibility = Visibility.Visible;
            }
            else
            {
                ErrorBorder.Visibility = Visibility.Collapsed;
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
            this.DialogResult = false;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
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

        // Drag & Drop
        private void DropBorder_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1A2B35"));
                DropBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00ADB5"));
            }
        }

        private void DropBorder_DragLeave(object sender, DragEventArgs e)
        {
            DropBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#121216"));
            DropBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34343F"));
        }

        private void DropBorder_Drop(object sender, DragEventArgs e)
        {
            DropBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#121216"));
            DropBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34343F"));

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    SelectFile(files[0]);
                }
            }
        }

        private void DropBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Click to browse
            var openFileDialog = new OpenFileDialog
            {
                Filter = "授权文件 (*.lic)|*.lic|所有文件 (*.*)|*.*",
                Title = "选择 EPLAN 插件授权文件"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectFile(openFileDialog.FileName);
            }
        }

        private void SelectFile(string filePath)
        {
            if (Path.GetExtension(filePath).Equals(".lic", StringComparison.OrdinalIgnoreCase))
            {
                _selectedLicensePath = filePath;
                TxtSelectedFile.Text = Path.GetFileName(filePath);
                TxtSelectedFile.Visibility = Visibility.Visible;
                BtnActivate.IsEnabled = true;
                ErrorBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                MessageBox.Show("请选择有效的 .lic 授权文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedLicensePath) || !File.Exists(_selectedLicensePath))
            {
                MessageBox.Show("请先选择授权文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Verify the selected license file
            if (LicenseVerifier.CheckLicense(_selectedLicensePath, _publicKeyXml, out LicenseInfo licInfo, out string error))
            {
                try
                {
                    // Copy the file to a persistent, user-writable folder
                    string destPath = LicenseVerifier.GetLicensePath();
                    
                    // Avoid file-locked exception if it's already there
                    if (Path.GetFullPath(_selectedLicensePath) != Path.GetFullPath(destPath))
                    {
                        File.Copy(_selectedLicensePath, destPath, true);
                    }

                    MessageBox.Show($"激活成功！\n授权用户: {licInfo?.UserName}\n有效期至: {licInfo?.ExpireDate}", 
                                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    this.DialogResult = true;
                    this.Close();
                }
                catch (Exception ex)
                {
                    TxtError.Text = $"激活文件复制失败: {ex.Message}";
                    ErrorBorder.Visibility = Visibility.Visible;
                }
            }
            else
            {
                TxtError.Text = error;
                ErrorBorder.Visibility = Visibility.Visible;
            }
        }
    }
}
