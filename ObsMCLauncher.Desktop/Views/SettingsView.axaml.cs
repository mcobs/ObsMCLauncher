using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using ObsMCLauncher.Desktop.ViewModels;
using ObsMCLauncher.Desktop.Views.SettingsPages;

namespace ObsMCLauncher.Desktop.Views;

public partial class SettingsView : UserControl
{
    private static readonly Dictionary<string, Type> PageMap = new()
    {
        ["Home"] = typeof(SettingsHomePage),
        ["Game"] = typeof(SettingsGamePage),
        ["Appearance"] = typeof(SettingsAppearancePage),
        ["Download"] = typeof(SettingsDownloadPage),
        ["General"] = typeof(SettingsGeneralPage),
    };

    private bool _initialized;

    public SettingsView()
    {
        InitializeComponent();
        SettingsFrame.NavigationPageFactory = new CachedSettingsPageFactory();
        SettingsNav.SelectionChanged += OnSelectionChanged;
        SettingsNav.Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        var tab = DataContext is SettingsViewModel vm ? vm.SelectedSettingsTab : 0;
        if (tab < 0 || tab >= SettingsNav.MenuItems.Count)
        {
            tab = 0;
        }

        SettingsNav.SelectedItem = SettingsNav.MenuItems[tab];
        FixSelectionIndicatorGhosts();
    }

    private void OnSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItemContainer is NavigationViewItem item &&
            item.Tag is string tag &&
            PageMap.TryGetValue(tag, out var pageType))
        {
            if (SettingsFrame.CurrentSourcePageType != pageType)
            {
                SettingsFrame.Navigate(pageType);
            }

            if (DataContext is SettingsViewModel vm)
            {
                vm.SelectedSettingsTab = SettingsNav.MenuItems.IndexOf(item);
            }

            Dispatcher.UIThread.Post(FixSelectionIndicatorGhosts);
        }
    }

    // FluentAvalonia 2.4.1 中，取消选中的项其 SelectionIndicator 透明度不会复位，
    // 导致切换时旧选项的绿色指示条残留（拖影）。这里手动同步指示条透明度。
    private void FixSelectionIndicatorGhosts()
    {
        var selected = SettingsNav.SelectedItem;

        foreach (var presenter in SettingsNav.GetVisualDescendants().OfType<NavigationViewItemPresenter>())
        {
            var hostItem = presenter.FindAncestorOfType<NavigationViewItem>();
            if (hostItem == null) continue;

            var indicator = presenter.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Name == "SelectionIndicator");
            if (indicator != null)
            {
                indicator.Opacity = ReferenceEquals(hostItem, selected) ? 1 : 0;
            }
        }
    }

    // 缓存设置子页面实例：离开设置界面再返回时保留页面状态（滚动位置、展开状态等）。
    // 复用时若旧 Frame 仍持有该页面，需先解除挂载，否则页面会继续挂在已销毁的
    // 旧 Frame 上，导致切回设置界面后内容区空白。
    private sealed class CachedSettingsPageFactory : INavigationPageFactory
    {
        private static readonly Dictionary<Type, Control> PageCache = new();

        public Control GetPage(Type sourcePageType)
        {
            if (!PageCache.TryGetValue(sourcePageType, out var page))
            {
                page = (Control)Activator.CreateInstance(sourcePageType)!;
                PageCache[sourcePageType] = page;
            }
            else
            {
                DetachFromOldHost(page);
            }

            return page;
        }

        public Control GetPageFromObject(object target)
            => target is Control control ? control : GetPage(target.GetType());

        private static void DetachFromOldHost(Control page)
        {
            switch (page.Parent)
            {
                case ContentControl oldHost when ReferenceEquals(oldHost.Content, page):
                    oldHost.Content = null;
                    break;
                case ContentPresenter oldPresenter when ReferenceEquals(oldPresenter.Content, page):
                    oldPresenter.Content = null;
                    break;
                case Panel oldPanel when oldPanel.Children.Contains(page):
                    oldPanel.Children.Remove(page);
                    break;
            }
        }
    }
}
