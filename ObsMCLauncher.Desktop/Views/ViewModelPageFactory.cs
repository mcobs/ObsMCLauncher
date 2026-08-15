using System;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;

namespace ObsMCLauncher.Desktop.Views;

/// <summary>
/// 根据 ViewModel 创建对应 View（与 ViewLocator 相同的命名规则：
/// 将类型名中的 "ViewModel" 替换为 "View"）。
/// Frame 会按 ViewModel 实例缓存页面，切回时复用同一 View 实例。
/// MainWindow 与 WelcomeWindow 共用。
/// </summary>
public sealed class ViewModelPageFactory : INavigationPageFactory
{
    public Control GetPage(Type sourcePageType) => Resolve(sourcePageType, null);

    public Control GetPageFromObject(object target) => Resolve(target.GetType(), target);

    private static Control Resolve(Type vmType, object? dataContext)
    {
        var name = vmType.FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(name);
                if (type != null) break;
            }
        }

        if (type == null)
        {
            return new TextBlock
            {
                Text = "Not Found: " + name,
                FontSize = 24,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
        }

        var control = (Control)Activator.CreateInstance(type)!;
        if (dataContext != null)
        {
            control.DataContext = dataContext;
        }

        return control;
    }
}
