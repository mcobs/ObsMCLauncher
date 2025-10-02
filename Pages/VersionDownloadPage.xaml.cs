using System.Windows;
using System.Windows.Controls;

namespace ObsMCLauncher.Pages
{
    public partial class VersionDownloadPage : Page
    {
        public VersionDownloadPage()
        {
            InitializeComponent();
        }

        private void VersionItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string versionTag)
            {
                // 导航到版本详情配置页面
                NavigationService?.Navigate(new VersionDetailPage(versionTag));
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 实现搜索功能（后续实现）
        }

        private void VersionTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 实现版本类型筛选（后续实现）
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // 实现刷新列表功能（后续实现）
        }
    }
}
