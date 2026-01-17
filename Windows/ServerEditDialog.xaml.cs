using System;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Windows
{
    public partial class ServerEditDialog : Window
    {
        public ServerInfo? ServerInfo { get; private set; }
        public string DialogTitle => ServerInfo == null ? "添加服务器" : "编辑服务器";

        public ServerEditDialog(ServerInfo? server = null)
        {
            InitializeComponent();
            DataContext = this;
            
            if (server != null)
            {
                // 编辑模式：复制服务器信息
                NameTextBox.Text = server.Name;
                AddressTextBox.Text = server.Address;
                PortTextBox.Text = server.Port.ToString();
                DescriptionTextBox.Text = server.Description ?? "";
                GroupTextBox.Text = server.Group ?? "";
                IconPathTextBox.Text = server.IconPath ?? "";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            // 验证输入
            if (string.IsNullOrWhiteSpace(NameTextBox.Text))
            {
                MessageBox.Show("请输入服务器名称", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(AddressTextBox.Text))
            {
                MessageBox.Show("请输入服务器地址", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                AddressTextBox.Focus();
                return;
            }

            // 验证端口
            if (!int.TryParse(PortTextBox.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("请输入有效的端口号 (1-65535)", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                PortTextBox.Focus();
                return;
            }

            // 创建服务器信息
            ServerInfo = new ServerInfo
            {
                Name = NameTextBox.Text.Trim(),
                Address = AddressTextBox.Text.Trim(),
                Port = port,
                Description = string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? null : DescriptionTextBox.Text.Trim(),
                Group = string.IsNullOrWhiteSpace(GroupTextBox.Text) ? null : GroupTextBox.Text.Trim(),
                IconPath = string.IsNullOrWhiteSpace(IconPathTextBox.Text) ? null : IconPathTextBox.Text.Trim()
            };

            DialogResult = true;
            Close();
        }

        private void BrowseIconButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择服务器图标",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.ico|所有文件|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                IconPathTextBox.Text = dialog.FileName;
            }
        }
    }
}

