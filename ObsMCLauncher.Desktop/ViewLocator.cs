using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;
        
        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type == null)
        {
            // 回退：从当前已加载程序集里查找（Type.GetType 对非mscorlib的类型经常返回 null）
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(name);
                if (type != null)
                    break;
            }
        }

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        // 占位页：直接返回一个简单的视图，避免必须为每个页面先创建 View 文件
        if (param is PlaceholderPageViewModel p)
        {
            return new TextBlock
            {
                Text = p.Title,
                FontSize = 24,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
