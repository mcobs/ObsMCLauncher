using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.ViewModels;

/// <summary>
/// 向导最后一步：完成页。纯文字致谢 + 版本号，"完成"按钮结束流程并写入 <c>WelcomeCompleted</c>。
/// 按钮走基类的 <c>NextCommand</c>（最后一步时 <see cref="WelcomeViewModel.GoNext"/> 即完成）。
/// </summary>
public partial class WelcomeFinishPageViewModel : WelcomeStepViewModel
{
    public WelcomeFinishPageViewModel(WelcomeViewModel owner)
        : base(owner, "准备就绪", "设置已完成。这些选项之后都可以在「设置」里随时修改。")
    {
    }

    /// <summary>显示用版本号</summary>
    public string Version { get; } = $"v{VersionInfo.DisplayVersion}";

    /// <summary>最后一步的按钮语义是"完成"</summary>
    public override string NextToolTip => "完成";
}
