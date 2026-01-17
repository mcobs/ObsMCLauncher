using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace ObsMCLauncher.Utils
{
    public static class ThemeTransitionManager
    {
        private static FrameworkElement? _overlay;
        private static bool _isTransitioning;

        public static void Initialize(FrameworkElement overlay)
        {
            _overlay = overlay;
        }

        public static async Task RunAsync(Action applyThemeAction)
        {
            if (_overlay == null)
            {
                applyThemeAction();
                return;
            }

            if (_isTransitioning)
            {
                applyThemeAction();
                return;
            }

            try
            {
                _isTransitioning = true;

                await _overlay.Dispatcher.InvokeAsync(() =>
                {
                    _overlay.Visibility = Visibility.Visible;
                    _overlay.Opacity = 0;
                });

                await FadeAsync(0, 1, 150);

                await _overlay.Dispatcher.InvokeAsync(() =>
                {
                    applyThemeAction();
                });

                await FadeAsync(1, 0, 150);

                await _overlay.Dispatcher.InvokeAsync(() =>
                {
                    _overlay.Visibility = Visibility.Collapsed;
                });
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        private static Task FadeAsync(double from, double to, int ms)
        {
            if (_overlay == null) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<object?>();

            _overlay.Dispatcher.Invoke(() =>
            {
                var anim = new DoubleAnimation
                {
                    From = from,
                    To = to,
                    Duration = TimeSpan.FromMilliseconds(ms),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                anim.Completed += (s, e) => tcs.TrySetResult(null);
                _overlay.BeginAnimation(UIElement.OpacityProperty, anim, HandoffBehavior.SnapshotAndReplace);
            });

            return tcs.Task;
        }
    }
}

