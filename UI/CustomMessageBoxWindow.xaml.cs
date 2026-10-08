using System;
using System.Windows;
using System.Windows.Input;

namespace TranslationEplAddIn.Tool.UI
{
    public partial class CustomMessageBoxWindow : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public CustomMessageBoxWindow(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            InitializeComponent();

            TxtMessage.Text = messageBoxText;
            TxtTitle.Text = string.IsNullOrEmpty(caption) ? "提示" : caption;

            ConfigureIcon(icon);
            ConfigureButtons(button);
        }

        private void ConfigureIcon(MessageBoxImage icon)
        {
            IconInfo.Visibility = Visibility.Collapsed;
            IconWarning.Visibility = Visibility.Collapsed;
            IconError.Visibility = Visibility.Collapsed;
            IconQuestion.Visibility = Visibility.Collapsed;

            switch (icon)
            {
                case MessageBoxImage.Information:
                    IconInfo.Visibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Warning:
                    IconWarning.Visibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Error:
                    IconError.Visibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Question:
                    IconQuestion.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void ConfigureButtons(MessageBoxButton button)
        {
            BtnYes.Visibility = Visibility.Collapsed;
            BtnNo.Visibility = Visibility.Collapsed;
            BtnOk.Visibility = Visibility.Collapsed;
            BtnCancel.Visibility = Visibility.Collapsed;

            switch (button)
            {
                case MessageBoxButton.OK:
                    BtnOk.Visibility = Visibility.Visible;
                    BtnOk.IsDefault = true;
                    break;
                case MessageBoxButton.OKCancel:
                    BtnOk.Visibility = Visibility.Visible;
                    BtnCancel.Visibility = Visibility.Visible;
                    BtnOk.IsDefault = true;
                    break;
                case MessageBoxButton.YesNo:
                    BtnYes.Visibility = Visibility.Visible;
                    BtnNo.Visibility = Visibility.Visible;
                    BtnYes.IsDefault = true;
                    break;
                case MessageBoxButton.YesNoCancel:
                    BtnYes.Visibility = Visibility.Visible;
                    BtnNo.Visibility = Visibility.Visible;
                    BtnCancel.Visibility = Visibility.Visible;
                    BtnYes.IsDefault = true;
                    break;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Yes;
            Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.No;
            Close();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }

        public static MessageBoxResult Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            MessageBoxResult result = MessageBoxResult.None;

            // Run in the dispatcher if called from another thread
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    result = ShowInternal(messageBoxText, caption, button, icon);
                }));
            }
            else
            {
                result = ShowInternal(messageBoxText, caption, button, icon);
            }

            return result;
        }

        private static MessageBoxResult ShowInternal(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            CustomMessageBoxWindow dialog = new CustomMessageBoxWindow(messageBoxText, caption, button, icon);

            // Set parent owner
            if (Application.Current != null)
            {
                Window owner = null;
                foreach (Window win in Application.Current.Windows)
                {
                    if (win.IsActive)
                    {
                        owner = win;
                        break;
                    }
                }
                if (owner == null)
                {
                    owner = Application.Current.MainWindow;
                }

                if (owner != null && owner != dialog)
                {
                    dialog.Owner = owner;
                }
            }

            dialog.ShowDialog();
            return dialog.Result;
        }
    }

    // Custom MessageBox class to seamlessly intercept standard MessageBox.Show calls within the namespace
    public static class MessageBox
    {
        public static MessageBoxResult Show(string messageBoxText)
        {
            return CustomMessageBoxWindow.Show(messageBoxText);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption)
        {
            return CustomMessageBoxWindow.Show(messageBoxText, caption);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
        {
            return CustomMessageBoxWindow.Show(messageBoxText, caption, button);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            return CustomMessageBoxWindow.Show(messageBoxText, caption, button, icon);
        }
    }
}
