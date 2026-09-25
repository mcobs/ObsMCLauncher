using System.Threading.Tasks;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;

namespace ObsMCLauncher.Desktop.Controls;

/// <summary>
/// FluentAvalonia 2.4.1 <see cref="ContentDialog"/> 的「复用实例」补丁。
///
/// <para><b>问题</b>：FA 在关闭对话框时会把 <c>IsHitTestVisible</c> 置为 <c>false</c>
/// （<c>ContentDialog.cs</c> 的 <c>FinalCloseDialog()</c>，注释说是为了防止关闭动画期间被重复点击），
/// 但重新打开时只恢复了 <c>IsVisible</c>，<b>没有把命中测试恢复回来</b>。
/// 于是——在 XAML 里声明、多次开合复用同一个实例的对话框，**第二次打开就再也点不动了**：
/// 外观完全正常（能看见、有动画、有焦点），但鼠标点击会穿透到弹窗后面的页面，
/// 表现为「获取不到焦点、按钮点不了」。每次 new 一个实例的弹窗不受影响。</para>
///
/// <para><b>用法</b>：凡是复用实例的 ContentDialog，显示一律走
/// <see cref="ShowReusedAsync(ContentDialog)"/>，不要直接调 <c>ShowAsync()</c>。</para>
///
/// <para>实测（headless 探针 <c>.temp/probe/DialogHitTestProbe</c>）：
/// 第 1 次打开 <c>IsHitTestVisible=True</c>，点击生效；关闭后变 <c>False</c>；
/// 第 2、3 次打开仍为 <c>False</c>，命中测试打在弹窗后面的 Panel 上，点击完全无响应。</para>
/// </summary>
public static class ContentDialogReuse
{
    /// <summary>
    /// 显示一个**复用的** ContentDialog：先把 FA 关窗口时留下的
    /// <c>IsHitTestVisible = false</c> 恢复掉，再调用标准的无参 <c>ShowAsync()</c>
    /// （宿主窗口由 FA 从 ApplicationLifetime 的活动窗口里取）。
    /// </summary>
    public static Task<ContentDialogResult> ShowReusedAsync(this ContentDialog dialog)
    {
        dialog.IsHitTestVisible = true;
        return dialog.ShowAsync();
    }
}
