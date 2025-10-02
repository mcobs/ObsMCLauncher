using System.Windows;
using System.Windows.Controls;

namespace ObsMCLauncher.Pages
{
    public partial class ResourcesPage : Page
    {
        public ResourcesPage()
        {
            InitializeComponent();
        }

        private void ResourceTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton button && button.Tag is string tag)
            {
                // 这里可以根据不同的标签页加载不同的内容
                // 目前先使用相同的布局，后续可以为每个类型创建不同的数据
                switch (tag)
                {
                    case "Mods":
                        // 加载 MOD 列表
                        break;
                    case "Textures":
                        // 加载材质包列表
                        break;
                    case "Shaders":
                        // 加载光影列表
                        break;
                    case "Datapacks":
                        // 加载数据包列表
                        break;
                    case "Modpacks":
                        // 加载整合包列表
                        break;
                }
            }
        }
    }
}

