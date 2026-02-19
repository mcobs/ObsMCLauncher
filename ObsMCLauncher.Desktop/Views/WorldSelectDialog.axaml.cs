using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace ObsMCLauncher.Desktop.Views;

public partial class WorldSelectDialog : Window
{
    public WorldSelectDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, System.EventArgs e)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            Easing = new CircularEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0.0),
                        new Setter(RenderTransformProperty, new ScaleTransform(0.9, 0.9))
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1.0),
                        new Setter(RenderTransformProperty, new ScaleTransform(1, 1))
                    }
                }
            }
        };

        RenderTransformOrigin = RelativePoint.Center;
        await animation.RunAsync(this);
    }
}
