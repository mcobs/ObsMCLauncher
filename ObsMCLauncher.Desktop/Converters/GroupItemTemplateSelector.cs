using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Converters;

/// <summary>
/// 已安装版本列表的模板分发器：分组头与版本项使用不同模板
/// </summary>
public class GroupItemTemplateSelector : IDataTemplate
{
    public IDataTemplate? GroupHeaderTemplate { get; set; }

    public IDataTemplate? VersionTemplate { get; set; }

    public Control? Build(object? param)
    {
        return param is GroupSectionHeader
            ? GroupHeaderTemplate?.Build(param)
            : VersionTemplate?.Build(param);
    }

    public bool Match(object? data) => true;
}
