using System;
using Avalonia.Controls;
using ObsMCLauncher.Desktop.ViewModels;

namespace ObsMCLauncher.Desktop.Views;

public partial class InstanceView : UserControl
{
    private InstanceViewModel? _currentVm;

    public InstanceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
            _currentVm.GroupManagerRequested -= OnGroupManagerRequested;

        _currentVm = DataContext as InstanceViewModel;

        if (_currentVm != null)
            _currentVm.GroupManagerRequested += OnGroupManagerRequested;
    }

    private async void OnGroupManagerRequested()
    {
        await GroupManagerDialog.ShowAsync();
    }
}
