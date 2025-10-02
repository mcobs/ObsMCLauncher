using System.Windows;
using System.Windows.Controls;

namespace ObsMCLauncher.Pages
{
    public partial class VersionDetailPage : Page
    {
        private string currentVersion;

        public VersionDetailPage(string version)
        {
            InitializeComponent();
            currentVersion = version;
            
            // 设置版本标题
            VersionTitle.Text = $"Minecraft {version}";
            VersionNumber.Text = version;
            
            // 更新选中的加载器显示
            UpdateSelectedLoaderText();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // 返回版本列表
            NavigationService?.GoBack();
        }

        private void LoaderRadio_Checked(object sender, RoutedEventArgs e)
        {
            // 禁用所有版本选择框
            if (ForgeVersionComboBox != null) ForgeVersionComboBox.IsEnabled = false;
            if (FabricVersionComboBox != null) FabricVersionComboBox.IsEnabled = false;
            if (OptiFineVersionComboBox != null) OptiFineVersionComboBox.IsEnabled = false;
            if (QuiltVersionComboBox != null) QuiltVersionComboBox.IsEnabled = false;

            // 根据选中的加载器启用对应的版本选择框
            if (sender == ForgeRadio && ForgeVersionComboBox != null)
            {
                ForgeVersionComboBox.IsEnabled = true;
            }
            else if (sender == FabricRadio && FabricVersionComboBox != null)
            {
                FabricVersionComboBox.IsEnabled = true;
            }
            else if (sender == OptiFineRadio && OptiFineVersionComboBox != null)
            {
                OptiFineVersionComboBox.IsEnabled = true;
            }
            else if (sender == QuiltRadio && QuiltVersionComboBox != null)
            {
                QuiltVersionComboBox.IsEnabled = true;
            }

            // 更新选中的加载器显示
            UpdateSelectedLoaderText();
        }

        private void UpdateSelectedLoaderText()
        {
            if (SelectedLoaderText == null) return;

            if (VanillaRadio?.IsChecked == true)
            {
                SelectedLoaderText.Text = "已选择：原版";
            }
            else if (ForgeRadio?.IsChecked == true)
            {
                var version = (ForgeVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                SelectedLoaderText.Text = $"已选择：Forge {version}";
            }
            else if (FabricRadio?.IsChecked == true)
            {
                var version = (FabricVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                SelectedLoaderText.Text = $"已选择：Fabric {version}";
            }
            else if (OptiFineRadio?.IsChecked == true)
            {
                var version = (OptiFineVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                SelectedLoaderText.Text = $"已选择：OptiFine {version}";
            }
            else if (QuiltRadio?.IsChecked == true)
            {
                var version = (QuiltVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "默认";
                SelectedLoaderText.Text = $"已选择：Quilt {version}";
            }
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            // 这里实现下载和安装逻辑
            string loaderType = "原版";
            string loaderVersion = "";

            if (ForgeRadio?.IsChecked == true)
            {
                loaderType = "Forge";
                loaderVersion = (ForgeVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            }
            else if (FabricRadio?.IsChecked == true)
            {
                loaderType = "Fabric";
                loaderVersion = (FabricVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            }
            else if (OptiFineRadio?.IsChecked == true)
            {
                loaderType = "OptiFine";
                loaderVersion = (OptiFineVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            }
            else if (QuiltRadio?.IsChecked == true)
            {
                loaderType = "Quilt";
                loaderVersion = (QuiltVersionComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            }

            MessageBox.Show(
                $"准备安装：\n\nMinecraft 版本：{currentVersion}\n加载器：{loaderType}\n加载器版本：{loaderVersion}",
                "安装确认",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            // 实际的下载和安装逻辑将在后续实现
        }
    }
}

