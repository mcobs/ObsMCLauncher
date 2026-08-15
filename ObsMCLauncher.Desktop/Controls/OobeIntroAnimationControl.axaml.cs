using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace ObsMCLauncher.Desktop.Controls;

/// <summary>
/// 欢迎页 OOBE 开场动画：方块错峰上滑入场，方块 3D 翻走的同时对应字母从 -90 度翻入，
/// 其中一个方块拉伸变蓝作为品牌标记停留，最后字母间距收拢、整体缩放回落。
/// 参考 ClassIsland 的 OobeIntroAnimationControl 实现。
/// </summary>
public partial class OobeIntroAnimationControl : UserControl
{
    public event EventHandler? AnimationEnd;

    public OobeIntroAnimationControl()
    {
        InitializeComponent();
    }

    private async void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        await PlayAnimationPhase1();
    }

    private async Task PlayAnimationPhase1()
    {
        Classes.Add("anim");
        var children1 = Rects.Children.ToList();
        var children2 = Texts.Children.ToList();
        var durationMs = 800;
        var count = Math.Min(children1.Count, children2.Count);

        // 品牌方块在 M 位置（拉伸后盖住 M、C 两个位置），C 位置的占位方块不显示
        const int brandIndex = 3;
        const int hiddenIndex = 4;

        // 预计算每个字母的错峰延迟
        var delays = new double[count];
        for (int i = 0; i < count; i++)
        {
            delays[i] = Math.Sin((1.0 * (i + 2) / (count + 2)) * (Math.PI / 2)) * durationMs / count;
        }

        // 关键：样式 Setter 里的 Transform 是所有元素共享的同一个实例，
        // 多个字母动画驱动同一个 AngleX 会互相顶掉（同属性同时只允许一个动画），
        // 导致字母闪没。这里必须给每个方块/字母创建独立的 Transform。
        foreach (var c in children1.OfType<Border>())
        {
            c.RenderTransform = new TransformGroup
            {
                Children =
                {
                    new TranslateTransform { Y = 50 },
                    new ScaleTransform(),
                    new Rotate3DTransform()
                }
            };
        }
        foreach (var c in children2.OfType<TextBlock>())
        {
            c.RenderTransform = new Rotate3DTransform();
        }

        // 品牌方块压轴翻走：对齐最后一个方块的翻走时刻，
        // 避免中途提前翻开露出空位（M、C 字母照常翻入，渲染在方块上层）
        var startOf = new double[count];
        for (int i = 1; i < count; i++)
        {
            startOf[i] = startOf[i - 1] + delays[i - 1];
        }
        var lastEntranceMs = delays[count - 1] * 9;
        var brandTotalMs = (startOf[count - 1] - startOf[brandIndex]) + lastEntranceMs * 1.5 + 750;
        var brandFlipMs = lastEntranceMs * 0.5;

        Task? last = null;
        for (int i = 0; i < count; i++)
        {
            var delay = delays[i];
            var c1 = children1[i];
            var c2 = children2[i];
            var anim1 = i == brandIndex
                ? BuildBrandAnimation(delay * 9, brandTotalMs, brandFlipMs)
                : BuildAnimation1(delay * 9, i == hiddenIndex);
            var anim2 = BuildAnimation2(delay * 9);
            _ = anim1.RunAsync(c1);
            var t = anim2.RunAsync(c2);
            c1.Classes.Add("anim");
            c2.Classes.Add("anim");
            if (i == count - 3)
            {
                last = t;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(delay));
        }

        if (last != null)
        {
            await last;
        }

        Texts.Classes.Add("anim");
        AnimationEnd?.Invoke(this, EventArgs.Empty);
        return;

        // 方块：上滑入场 -> 停留 -> 3D 翻走消失（willHide 的方块不现身）
        Animation BuildAnimation1(double timeMs, bool willHide)
        {
            var animation = new Animation
            {
                FillMode = FillMode.Both,
                Duration = TimeSpan.FromMilliseconds(timeMs * 1.5 + 750),
                Children =
                {
                    new KeyFrame
                    {
                        Setters =
                        {
                            new Setter(OpacityProperty, 0.0),
                            new Setter(TranslateTransform.YProperty, 50.0)
                        },
                        KeyTime = TimeSpan.FromMilliseconds(0)
                    },
                    new KeyFrame
                    {
                        KeyTime = TimeSpan.FromMilliseconds(timeMs),
                        Setters =
                        {
                            new Setter(OpacityProperty, 1.0),
                            new Setter(TranslateTransform.YProperty, 0.0)
                        },
                        KeySpline = KeySpline.Parse("0.25, 1, 0.5, 1", CultureInfo.CurrentUICulture)
                    },
                    new KeyFrame
                    {
                        KeyTime = TimeSpan.FromMilliseconds(timeMs + 1),
                        Setters =
                        {
                            new Setter(OpacityProperty, willHide ? 0.0 : 1.0)
                        }
                    },
                    new KeyFrame
                    {
                        KeyTime = TimeSpan.FromMilliseconds(timeMs + 750),
                        Setters =
                        {
                            new Setter(OpacityProperty, willHide ? 0.0 : 1.0),
                            new Setter(Rotate3DTransform.AngleXProperty, 0.0)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(OpacityProperty, 0.0),
                            new Setter(Rotate3DTransform.AngleXProperty, 90.0)
                        },
                        KeySpline = KeySpline.Parse("0.32, 0, 0.67, 0", CultureInfo.CurrentUICulture)
                    }
                }
            };
            return animation;
        }

        // 品牌方块：普通节奏入场，但一直停留到压轴（totalMs 对齐最后一个方块的翻走结束时刻）才翻走
        Animation BuildBrandAnimation(double entranceMs, double totalMs, double flipMs)
        {
            var animation = new Animation
            {
                FillMode = FillMode.Both,
                Duration = TimeSpan.FromMilliseconds(totalMs),
                Children =
                {
                    new KeyFrame
                    {
                        Setters =
                        {
                            new Setter(OpacityProperty, 0.0),
                            new Setter(TranslateTransform.YProperty, 50.0)
                        },
                        KeyTime = TimeSpan.FromMilliseconds(0)
                    },
                    new KeyFrame
                    {
                        KeyTime = TimeSpan.FromMilliseconds(entranceMs),
                        Setters =
                        {
                            new Setter(OpacityProperty, 1.0),
                            new Setter(TranslateTransform.YProperty, 0.0)
                        },
                        KeySpline = KeySpline.Parse("0.25, 1, 0.5, 1", CultureInfo.CurrentUICulture)
                    },
                    new KeyFrame
                    {
                        KeyTime = TimeSpan.FromMilliseconds(entranceMs + 1),
                        Setters =
                        {
                            new Setter(OpacityProperty, 1.0)
                        }
                    },
                    new KeyFrame
                    {
                        KeyTime = TimeSpan.FromMilliseconds(totalMs - flipMs),
                        Setters =
                        {
                            new Setter(OpacityProperty, 1.0),
                            new Setter(Rotate3DTransform.AngleXProperty, 0.0)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(OpacityProperty, 0.0),
                            new Setter(Rotate3DTransform.AngleXProperty, 90.0)
                        },
                        KeySpline = KeySpline.Parse("0.32, 0, 0.67, 0", CultureInfo.CurrentUICulture)
                    }
                }
            };
            return animation;
        }

        // 字母：等方块翻走后从 -90 度翻入现身
        Animation BuildAnimation2(double timeMs)
        {
            var animation = new Animation
            {
                FillMode = FillMode.Both,
                Duration = TimeSpan.FromMilliseconds(timeMs * 2 + 750),
                Children =
                {
                    new KeyFrame
                    {
                        KeyTime = TimeSpan.FromMilliseconds(timeMs * 1.5 + 750),
                        Setters =
                        {
                            new Setter(Rotate3DTransform.AngleXProperty, -90.0),
                            new Setter(OpacityProperty, 0.0)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(Rotate3DTransform.AngleXProperty, 0.0),
                            new Setter(OpacityProperty, 1.0)
                        },
                        KeySpline = KeySpline.Parse("0.33, 1, 0.68, 1", CultureInfo.CurrentUICulture)
                    }
                }
            };
            return animation;
        }
    }
}
