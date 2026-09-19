using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ObsMCLauncher.Desktop.Services;

namespace ObsMCLauncher.Desktop.Controls;

/// <summary>
/// 自绘标题栏：窗口图标 + 标题 + 最小化/最大化/关闭三个系统按钮。
/// 支持标题栏拖动与双击最大化，交互对齐 Windows 原生。
///
/// 窗口需要在构造函数里调用 <see cref="WindowChrome.Apply"/> 打开客户端区扩展，
/// 这里只负责显隐（系统标题栏模式下整条收起，所在 Grid 行塌陷为 0）与按钮行为。
/// </summary>
public partial class WindowTitleBar : UserControl
{
    // Segoe Fluent 图标里最小化/最大化/还原/关闭的字形，按 10x10 网格手工还原
    private const string MaximizeGlyphData = "M0.5,0.5 H9.5 V9.5 H0.5 Z";
    private const string RestoreGlyphData = "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z";

    private Window? _host;

    public WindowTitleBar()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _host = TopLevel.GetTopLevel(this) as Window;
        if (_host == null) return;

        _host.PropertyChanged += OnHostPropertyChanged;
        WindowChrome.Changed += OnChromeChanged;

        SyncVisibility();
        UpdateWindowButtons();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        WindowChrome.Changed -= OnChromeChanged;

        if (_host != null)
        {
            _host.PropertyChanged -= OnHostPropertyChanged;
            _host = null;
        }
    }

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            UpdateMaximizeGlyph();
        }
        else if (e.Property == Window.TitleProperty)
        {
            TitleText.Text = _host?.Title;
        }
        else if (e.Property == Window.CanResizeProperty)
        {
            UpdateWindowButtons();
        }
    }

    private void OnChromeChanged(object? sender, EventArgs e) => SyncVisibility();

    private void SyncVisibility() => IsVisible = !WindowChrome.UseSystemTitleBar;

    private void UpdateWindowButtons()
    {
        if (_host == null) return;

        TitleText.Text = _host.Title;

        // 不可调整大小的窗口不提供最大化，和原生标题栏一致
        MaximizeButton.IsVisible = _host.CanResize;

        UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
    {
        if (_host == null) return;

        var maximized = _host.WindowState == WindowState.Maximized;
        MaximizeGlyph.Data = Geometry.Parse(maximized ? RestoreGlyphData : MaximizeGlyphData);
        ToolTip.SetTip(MaximizeButton, maximized ? "向下还原" : "最大化");
    }

    private void BarRoot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_host == null || !e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        _host.BeginMoveDrag(e);
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_host != null) _host.WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        if (_host == null || !_host.CanResize) return;

        _host.WindowState = _host.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => _host?.Close();
}