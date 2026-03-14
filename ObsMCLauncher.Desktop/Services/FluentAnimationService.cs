using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace ObsMCLauncher.Desktop.Services;

public class FluentAnimationService
{
    private static readonly Easing _splineEase = new SplineEasing(
        0.1, 0.9,
        0.2, 1.0
    );

    private static readonly Easing _easeOutBack = new CustomEasing
    {
        EasingFunc = t =>
        {
            const double c1 = 1.70158;
            const double c3 = c1 + 1;
            return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
        }
    };

    private static readonly Easing _easeOutCubic = new CubicEaseOut();

    public static async Task AnimateEnterAsync(Border element, TimeSpan duration = default)
    {
        if (duration == default)
            duration = TimeSpan.FromMilliseconds(350);

        element.Opacity = 0;
        element.RenderTransform = new TransformGroup
        {
            Children = new Transforms
            {
                new ScaleTransform { ScaleX = 0.9, ScaleY = 0.9 },
                new TranslateTransform { Y = 20 }
            }
        };
        element.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Absolute);

        var opacityAnimation = new Animation
        {
            Duration = duration,
            Easing = _splineEase,
            FillMode = FillMode.Forward,
            IterationCount = new IterationCount(1),
            Children =
            {
                new KeyFrame()
                {
                    Setters = { new Setter { Property = Border.OpacityProperty, Value = 0.0 } },
                    Cue = new Cue(0.0)
                },
                new KeyFrame()
                {
                    Setters = { new Setter { Property = Border.OpacityProperty, Value = 1.0 } },
                    Cue = new Cue(1.0)
                }
            }
        };

        var scaleAnimation = new Animation
        {
            Duration = duration,
            Easing = _easeOutBack,
            FillMode = FillMode.Forward,
            IterationCount = new IterationCount(1),
            Children =
            {
                new KeyFrame()
                {
                    Setters =
                    {
                        new Setter
                        {
                            Property = ScaleTransform.ScaleXProperty,
                            Value = 0.9
                        },
                        new Setter
                        {
                            Property = ScaleTransform.ScaleYProperty,
                            Value = 0.9
                        }
                    },
                    Cue = new Cue(0.0)
                },
                new KeyFrame()
                {
                    Setters =
                    {
                        new Setter
                        {
                            Property = ScaleTransform.ScaleXProperty,
                            Value = 1.0
                        },
                        new Setter
                        {
                            Property = ScaleTransform.ScaleYProperty,
                            Value = 1.0
                        }
                    },
                    Cue = new Cue(1.0)
                }
            }
        };

        var translateAnimation = new Animation
        {
            Duration = duration,
            Easing = _easeOutCubic,
            FillMode = FillMode.Forward,
            IterationCount = new IterationCount(1),
            Children =
            {
                new KeyFrame()
                {
                    Setters =
                    {
                        new Setter
                        {
                            Property = TranslateTransform.YProperty,
                            Value = 20.0
                        }
                    },
                    Cue = new Cue(0.0)
                },
                new KeyFrame()
                {
                    Setters =
                    {
                        new Setter
                        {
                            Property = TranslateTransform.YProperty,
                            Value = 0.0
                        }
                    },
                    Cue = new Cue(1.0)
                }
            }
        };

        var transformGroup = (TransformGroup)element.RenderTransform;
        var scaleTransform = (ScaleTransform)transformGroup.Children[0];
        var translateTransform = (TranslateTransform)transformGroup.Children[1];

        await Task.WhenAll(
            opacityAnimation.RunAsync(element),
            scaleAnimation.RunAsync(scaleTransform),
            translateAnimation.RunAsync(translateTransform)
        );
    }

    public static async Task AnimateExitAsync(Border element, TimeSpan duration = default)
    {
        if (duration == default)
            duration = TimeSpan.FromMilliseconds(200);

        var opacityAnimation = new Animation
        {
            Duration = duration,
            Easing = new CubicEaseIn(),
            FillMode = FillMode.Forward,
            IterationCount = new IterationCount(1),
            Children =
            {
                new KeyFrame()
                {
                    Setters = { new Setter { Property = Border.OpacityProperty, Value = 1.0 } },
                    Cue = new Cue(0.0)
                },
                new KeyFrame()
                {
                    Setters = { new Setter { Property = Border.OpacityProperty, Value = 0.0 } },
                    Cue = new Cue(1.0)
                }
            }
        };

        var scaleAnimation = new Animation
        {
            Duration = duration,
            Easing = new CubicEaseIn(),
            FillMode = FillMode.Forward,
            IterationCount = new IterationCount(1),
            Children =
            {
                new KeyFrame()
                {
                    Setters =
                    {
                        new Setter
                        {
                            Property = ScaleTransform.ScaleXProperty,
                            Value = 1.0
                        },
                        new Setter
                        {
                            Property = ScaleTransform.ScaleYProperty,
                            Value = 1.0
                        }
                    },
                    Cue = new Cue(0.0)
                },
                new KeyFrame()
                {
                    Setters =
                    {
                        new Setter
                        {
                            Property = ScaleTransform.ScaleXProperty,
                            Value = 0.95
                        },
                        new Setter
                        {
                            Property = ScaleTransform.ScaleYProperty,
                            Value = 0.95
                        }
                    },
                    Cue = new Cue(1.0)
                }
            }
        };

        var translateAnimation = new Animation
        {
            Duration = duration,
            Easing = new CubicEaseIn(),
            FillMode = FillMode.Forward,
            IterationCount = new IterationCount(1),
            Children =
            {
                new KeyFrame()
                {
                    Setters =
                    {
                        new Setter
                        {
                            Property = TranslateTransform.YProperty,
                            Value = 0.0
                        }
                    },
                    Cue = new Cue(0.0)
                },
                new KeyFrame()
                {
                    Setters =
                    {
                        new Setter
                        {
                            Property = TranslateTransform.YProperty,
                            Value = 20.0
                        }
                    },
                    Cue = new Cue(1.0)
                }
            }
        };

        var transformGroup = (TransformGroup)element.RenderTransform;
        var scaleTransform = (ScaleTransform)transformGroup.Children[0];
        var translateTransform = (TranslateTransform)transformGroup.Children[1];

        await Task.WhenAll(
            opacityAnimation.RunAsync(element),
            scaleAnimation.RunAsync(scaleTransform),
            translateAnimation.RunAsync(translateTransform)
        );
    }

    public static void ApplyHoverEffect(Border element, double scale = 1.02)
    {
        element.PointerEntered += (s, e) =>
        {
            var scaleTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };
            element.RenderTransform = scaleTransform;
            element.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Absolute);
        };

        element.PointerExited += (s, e) =>
        {
            var scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            element.RenderTransform = scaleTransform;
        };
    }
}

public class CustomEasing : Easing
{
    public Func<double, double> EasingFunc { get; init; } = t => t;

    public override double Ease(double time) => EasingFunc(time);
}
