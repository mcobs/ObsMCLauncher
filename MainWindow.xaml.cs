using System.Windows;
using System.Windows.Controls;
using ObsMCLauncher.Pages;

namespace ObsMCLauncher
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // 默认导航到主页
            MainFrame.Navigate(new HomePage());
        }

        private void NavigationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton button && button.Tag is string tag)
            {
                switch (tag)
                {
                    case "Home":
                        MainFrame.Navigate(new HomePage());
                        break;
                    case "Version":
                        MainFrame.Navigate(new VersionDownloadPage());
                        break;
                    case "Resources":
                        MainFrame.Navigate(new ResourcesPage());
                        break;
                    case "Settings":
                        MainFrame.Navigate(new SettingsPage());
                        break;
                }
            }
        }

        // 窗口控制按钮事件
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaximizeIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.WindowMaximize;
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.WindowRestore;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

