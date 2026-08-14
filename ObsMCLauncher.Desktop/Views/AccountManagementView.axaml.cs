using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using ObsMCLauncher.Core.Utils;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class AccountManagementView : UserControl
{
    private AccountManagementViewModel? _vm;
    private bool _yggdrasilDialogShown;

    public AccountManagementView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.ScrollToAccountRequested -= OnScrollToAccountRequested;
        }

        _vm = DataContext as AccountManagementViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _vm.ScrollToAccountRequested += OnScrollToAccountRequested;

            // DataContext 就绪时视图可能已加载（如页面缓存复用），补一次自动刷新
            if (IsLoaded)
            {
                _ = _vm.AutoRefreshExpiredTokensAsync();
            }
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // 页面打开时自动刷新已过期/状态未知的登录令牌
        _ = _vm?.AutoRefreshExpiredTokensAsync();
    }

    private void OnScrollToAccountRequested(AccountItemViewModel item)
    {
        AccountList.ScrollIntoView(item);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AccountManagementViewModel.IsYggdrasilLoginDialogOpen))
            return;

        if (_vm!.IsYggdrasilLoginDialogOpen)
        {
            _ = ShowYggdrasilLoginDialogAsync();
        }
        else if (_yggdrasilDialogShown)
        {
            try
            {
                YggdrasilDialog.Hide();
            }
            catch (Exception ex)
            {
                DebugLogger.Error("AccountPage", $"Yggdrasil dialog hide error: {ex.Message}");
            }
        }
    }

    private async Task ShowYggdrasilLoginDialogAsync()
    {
        if (_vm == null || _yggdrasilDialogShown)
            return;

        // 对话框内容在弹出层中渲染会丢失 DataContext 继承（回退到 MainWindow 的 DataContext），
        // 故在显示前显式绑定到当前 AccountManagementViewModel。
        YggdrasilDialog.DataContext = _vm;

        _yggdrasilDialogShown = true;
        try
        {
            var result = await YggdrasilDialog.ShowAsync();

            // 用户通过 ESC / 关闭按钮关闭对话框时，同步回 ViewModel 状态；
            // 登录完成由 ViewModel 主动置 false 并触发 Hide()，此处不会重复处理。
            if (result == ContentDialogResult.None && _vm.IsYggdrasilLoginDialogOpen)
            {
                _vm.IsYggdrasilLoginDialogOpen = false;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("AccountPage", $"Yggdrasil dialog error: {ex.Message}");
        }
        finally
        {
            _yggdrasilDialogShown = false;
        }
    }
}
