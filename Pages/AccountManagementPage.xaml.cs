using System.Windows;
using System.Windows.Controls;

namespace ObsMCLauncher.Pages
{
    public partial class AccountManagementPage : Page
    {
        public AccountManagementPage()
        {
            InitializeComponent();
        }

        private void AddMicrosoftAccount_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "微软账户登录功能将在后续版本实现。\n\n" +
                "届时将支持：\n" +
                "• OAuth 2.0 安全登录\n" +
                "• 自动获取正版皮肤\n" +
                "• 多账户管理", 
                "微软账户登录", 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }

        private void AddOfflineAccount_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "离线账户创建功能将在后续版本实现。\n\n" +
                "届时将支持：\n" +
                "• 自定义用户名\n" +
                "• 离线皮肤设置\n" +
                "• UUID 生成", 
                "离线账户创建", 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }
    }
}

