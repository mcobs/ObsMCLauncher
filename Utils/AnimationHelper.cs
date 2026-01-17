using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ObsMCLauncher.Utils
{
    /// <summary>
    /// 动画辅助类 - 提供统一的动画创建和管理
    /// </summary>
    public static class AnimationHelper
    {
        /// <summary>
        /// 默认动画时长
        /// </summary>
        public static readonly Duration DefaultDuration = new Duration(TimeSpan.FromMilliseconds(300));

        /// <summary>
        /// 快速动画时长
        /// </summary>
        public static readonly Duration FastDuration = new Duration(TimeSpan.FromMilliseconds(150));

        /// <summary>
        /// 慢速动画时长
        /// </summary>
        public static readonly Duration SlowDuration = new Duration(TimeSpan.FromMilliseconds(500));

        /// <summary>
        /// 创建淡入动画
        /// </summary>
        /// <param name="target">目标元素</param>
        /// <param name="duration">动画时长（可选，默认300ms）</param>
        /// <param name="from">起始透明度（可选，默认0）</param>
        /// <param name="to">结束透明度（可选，默认1）</param>
        /// <param name="easingMode">缓动模式（可选，默认EaseOut）</param>
        /// <param name="onCompleted">完成回调（可选）</param>
        /// <returns>Storyboard</returns>
        public static Storyboard CreateFadeInAnimation(
            UIElement target,
            Duration? duration = null,
            double? from = null,
            double? to = null,
            EasingMode easingMode = EasingMode.EaseOut,
            EventHandler? onCompleted = null)
        {
            var anim = new DoubleAnimation
            {
                From = from ?? 0.0,
                To = to ?? 1.0,
                Duration = duration ?? DefaultDuration,
                EasingFunction = new CubicEase { EasingMode = easingMode }
            };

            if (onCompleted != null)
            {
                anim.Completed += onCompleted;
            }

            var storyboard = new Storyboard();
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(anim);

            return storyboard;
        }

        /// <summary>
        /// 创建淡出动画
        /// </summary>
        /// <param name="target">目标元素</param>
        /// <param name="duration">动画时长（可选，默认300ms）</param>
        /// <param name="from">起始透明度（可选，默认1）</param>
        /// <param name="to">结束透明度（可选，默认0）</param>
        /// <param name="easingMode">缓动模式（可选，默认EaseIn）</param>
        /// <param name="onCompleted">完成回调（可选）</param>
        /// <returns>Storyboard</returns>
        public static Storyboard CreateFadeOutAnimation(
            UIElement target,
            Duration? duration = null,
            double? from = null,
            double? to = null,
            EasingMode easingMode = EasingMode.EaseIn,
            EventHandler? onCompleted = null)
        {
            var anim = new DoubleAnimation
            {
                From = from ?? 1.0,
                To = to ?? 0.0,
                Duration = duration ?? DefaultDuration,
                EasingFunction = new CubicEase { EasingMode = easingMode }
            };

            if (onCompleted != null)
            {
                anim.Completed += onCompleted;
            }

            var storyboard = new Storyboard();
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(anim);

            return storyboard;
        }

        /// <summary>
        /// 创建滑动动画（垂直方向）
        /// </summary>
        /// <param name="target">目标元素</param>
        /// <param name="from">起始位置（可选）</param>
        /// <param name="to">结束位置（可选）</param>
        /// <param name="duration">动画时长（可选，默认300ms）</param>
        /// <param name="easingMode">缓动模式（可选，默认EaseOut）</param>
        /// <param name="onCompleted">完成回调（可选）</param>
        /// <returns>Storyboard</returns>
        public static Storyboard CreateSlideAnimation(
            UIElement target,
            Thickness? from = null,
            Thickness? to = null,
            Duration? duration = null,
            EasingMode easingMode = EasingMode.EaseOut,
            EventHandler? onCompleted = null)
        {
            var fromMargin = from ?? new Thickness(0, -20, 0, 0);
            var toMargin = to ?? new Thickness(0);

            var animX = new ThicknessAnimation
            {
                From = fromMargin,
                To = toMargin,
                Duration = duration ?? DefaultDuration,
                EasingFunction = new CubicEase { EasingMode = easingMode }
            };

            var storyboard = new Storyboard();
            Storyboard.SetTarget(animX, target);
            Storyboard.SetTargetProperty(animX, new PropertyPath(FrameworkElement.MarginProperty));
            storyboard.Children.Add(animX);

            if (onCompleted != null)
            {
                storyboard.Completed += onCompleted;
            }

            return storyboard;
        }

        /// <summary>
        /// 创建缩放动画
        /// </summary>
        /// <param name="target">目标元素</param>
        /// <param name="from">起始缩放（可选，默认0.9）</param>
        /// <param name="to">结束缩放（可选，默认1.0）</param>
        /// <param name="duration">动画时长（可选，默认300ms）</param>
        /// <param name="easingMode">缓动模式（可选，默认EaseOut）</param>
        /// <param name="onCompleted">完成回调（可选）</param>
        /// <returns>Storyboard</returns>
        public static Storyboard CreateScaleAnimation(
            UIElement target,
            double? from = null,
            double? to = null,
            Duration? duration = null,
            EasingMode easingMode = EasingMode.EaseOut,
            EventHandler? onCompleted = null)
        {
            // 确保有 RenderTransform
            if (target.RenderTransform == null || !(target.RenderTransform is ScaleTransform))
            {
                target.RenderTransform = new ScaleTransform(1.0, 1.0);
                target.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var scaleTransform = target.RenderTransform as ScaleTransform;
            if (scaleTransform == null)
            {
                scaleTransform = new ScaleTransform(1.0, 1.0);
                target.RenderTransform = scaleTransform;
                target.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var fromScale = from ?? 0.9;
            var toScale = to ?? 1.0;

            var animX = new DoubleAnimation
            {
                From = fromScale,
                To = toScale,
                Duration = duration ?? DefaultDuration,
                EasingFunction = new BackEase { EasingMode = easingMode, Amplitude = 0.3 }
            };

            var animY = new DoubleAnimation
            {
                From = fromScale,
                To = toScale,
                Duration = duration ?? DefaultDuration,
                EasingFunction = new BackEase { EasingMode = easingMode, Amplitude = 0.3 }
            };

            var storyboard = new Storyboard();
            Storyboard.SetTarget(animX, scaleTransform);
            Storyboard.SetTargetProperty(animX, new PropertyPath(ScaleTransform.ScaleXProperty));
            Storyboard.SetTarget(animY, scaleTransform);
            Storyboard.SetTargetProperty(animY, new PropertyPath(ScaleTransform.ScaleYProperty));
            storyboard.Children.Add(animX);
            storyboard.Children.Add(animY);

            if (onCompleted != null)
            {
                storyboard.Completed += onCompleted;
            }

            return storyboard;
        }

        /// <summary>
        /// 创建组合动画（淡入+缩放）
        /// </summary>
        /// <param name="target">目标元素</param>
        /// <param name="duration">动画时长（可选，默认300ms）</param>
        /// <param name="onCompleted">完成回调（可选）</param>
        /// <returns>Storyboard</returns>
        public static Storyboard CreateFadeInWithScaleAnimation(
            UIElement target,
            Duration? duration = null,
            EventHandler? onCompleted = null)
        {
            var storyboard = new Storyboard();

            // 淡入动画
            var fadeAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = duration ?? DefaultDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeAnim, target);
            Storyboard.SetTargetProperty(fadeAnim, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(fadeAnim);

            // 缩放动画
            if (target.RenderTransform == null || !(target.RenderTransform is ScaleTransform))
            {
                target.RenderTransform = new ScaleTransform(0.9, 0.9);
                target.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var scaleTransform = target.RenderTransform as ScaleTransform;
            if (scaleTransform == null)
            {
                scaleTransform = new ScaleTransform(0.9, 0.9);
                target.RenderTransform = scaleTransform;
                target.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var scaleXAnim = new DoubleAnimation
            {
                From = 0.9,
                To = 1.0,
                Duration = duration ?? DefaultDuration,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };
            var scaleYAnim = new DoubleAnimation
            {
                From = 0.9,
                To = 1.0,
                Duration = duration ?? DefaultDuration,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };

            Storyboard.SetTarget(scaleXAnim, scaleTransform);
            Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath(ScaleTransform.ScaleXProperty));
            Storyboard.SetTarget(scaleYAnim, scaleTransform);
            Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath(ScaleTransform.ScaleYProperty));
            storyboard.Children.Add(scaleXAnim);
            storyboard.Children.Add(scaleYAnim);

            if (onCompleted != null)
            {
                storyboard.Completed += onCompleted;
            }

            return storyboard;
        }

        /// <summary>
        /// 创建旋转加载动画
        /// </summary>
        /// <param name="target">目标元素</param>
        /// <param name="duration">单次旋转时长（可选，默认1秒）</param>
        /// <param name="repeatBehavior">重复行为（可选，默认Forever）</param>
        /// <returns>Storyboard</returns>
        public static Storyboard CreateRotateAnimation(
            UIElement target,
            Duration? duration = null,
            RepeatBehavior? repeatBehavior = null)
        {
            if (target.RenderTransform == null || !(target.RenderTransform is RotateTransform))
            {
                target.RenderTransform = new RotateTransform();
                target.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var rotateTransform = target.RenderTransform as RotateTransform;
            if (rotateTransform == null)
            {
                rotateTransform = new RotateTransform();
                target.RenderTransform = rotateTransform;
                target.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var anim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = duration ?? new Duration(TimeSpan.FromSeconds(1)),
                RepeatBehavior = repeatBehavior ?? RepeatBehavior.Forever
            };

            var storyboard = new Storyboard();
            Storyboard.SetTarget(anim, rotateTransform);
            Storyboard.SetTargetProperty(anim, new PropertyPath(RotateTransform.AngleProperty));
            storyboard.Children.Add(anim);

            return storyboard;
        }

        /// <summary>
        /// 创建脉冲动画（透明度变化）
        /// </summary>
        /// <param name="target">目标元素</param>
        /// <param name="from">起始透明度（可选，默认0.5）</param>
        /// <param name="to">结束透明度（可选，默认1.0）</param>
        /// <param name="duration">单次脉冲时长（可选，默认1秒）</param>
        /// <param name="repeatBehavior">重复行为（可选，默认Forever）</param>
        /// <returns>Storyboard</returns>
        public static Storyboard CreatePulseAnimation(
            UIElement target,
            double? from = null,
            double? to = null,
            Duration? duration = null,
            RepeatBehavior? repeatBehavior = null)
        {
            var anim = new DoubleAnimation
            {
                From = from ?? 0.5,
                To = to ?? 1.0,
                Duration = duration ?? new Duration(TimeSpan.FromSeconds(1)),
                AutoReverse = true,
                RepeatBehavior = repeatBehavior ?? RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            var storyboard = new Storyboard();
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(anim);

            return storyboard;
        }

        /// <summary>
        /// 创建进度条动画（从0到指定值）
        /// </summary>
        /// <param name="target">目标元素（ProgressBar）</param>
        /// <param name="to">目标值（0-100）</param>
        /// <param name="duration">动画时长（可选，默认500ms）</param>
        /// <param name="onCompleted">完成回调（可选）</param>
        /// <returns>Storyboard</returns>
        public static Storyboard CreateProgressAnimation(
            DependencyObject target,
            double to,
            Duration? duration = null,
            EventHandler? onCompleted = null)
        {
            var anim = new DoubleAnimation
            {
                From = 0,
                To = to,
                Duration = duration ?? SlowDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            if (onCompleted != null)
            {
                anim.Completed += onCompleted;
            }

            var storyboard = new Storyboard();
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, new PropertyPath("Value"));
            storyboard.Children.Add(anim);

            return storyboard;
        }

        /// <summary>
        /// 执行动画序列（按顺序执行多个动画）
        /// </summary>
        /// <param name="animations">动画列表</param>
        /// <param name="onAllCompleted">所有动画完成回调（可选）</param>
        public static void ExecuteAnimationSequence(
            List<Storyboard> animations,
            EventHandler? onAllCompleted = null)
        {
            if (animations == null || animations.Count == 0)
            {
                onAllCompleted?.Invoke(null, EventArgs.Empty);
                return;
            }

            int currentIndex = 0;

            EventHandler? onAnimationCompleted = null;
            onAnimationCompleted = (sender, e) =>
            {
                currentIndex++;
                if (currentIndex < animations.Count)
                {
                    animations[currentIndex].Completed += onAnimationCompleted;
                    animations[currentIndex].Begin();
                }
                else
                {
                    onAllCompleted?.Invoke(null, EventArgs.Empty);
                }
            };

            animations[0].Completed += onAnimationCompleted;
            animations[0].Begin();
        }

        /// <summary>
        /// 执行并行动画（同时执行多个动画）
        /// </summary>
        /// <param name="animations">动画列表</param>
        /// <param name="onAllCompleted">所有动画完成回调（可选）</param>
        public static void ExecuteParallelAnimations(
            List<Storyboard> animations,
            EventHandler? onAllCompleted = null)
        {
            if (animations == null || animations.Count == 0)
            {
                onAllCompleted?.Invoke(null, EventArgs.Empty);
                return;
            }

            int completedCount = 0;
            int totalCount = animations.Count;

            EventHandler onAnimationCompleted = (sender, e) =>
            {
                completedCount++;
                if (completedCount >= totalCount)
                {
                    onAllCompleted?.Invoke(null, EventArgs.Empty);
                }
            };

            foreach (var animation in animations)
            {
                animation.Completed += onAnimationCompleted;
                animation.Begin();
            }
        }
    }
}

