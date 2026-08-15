using System;
using Avalonia.Controls;

namespace ObsMCLauncher.Desktop.Views;

public partial class WelcomePageView : UserControl
{
    public WelcomePageView()
    {
        InitializeComponent();
    }

    private void Intro_OnAnimationEnd(object? sender, EventArgs e)
    {
        ContentRoot.Classes.Add("anim");
    }
}
