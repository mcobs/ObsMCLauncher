using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace ObsMCLauncher.Utils
{
    public class GridLengthAnimation : AnimationTimeline
    {
        public override Type TargetPropertyType => typeof(GridLength);

        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation));

        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation));

        public IEasingFunction? EasingFunction
        {
            get => (IEasingFunction?)GetValue(EasingFunctionProperty);
            set => SetValue(EasingFunctionProperty, value);
        }

        public static readonly DependencyProperty EasingFunctionProperty =
            DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(GridLengthAnimation));

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        {
            var fromValue = From.Value;
            var toValue = To.Value;

            var progress = animationClock.CurrentProgress ?? 1.0;
            if (EasingFunction != null)
            {
                progress = EasingFunction.Ease(progress);
            }

            var currentValue = ((toValue - fromValue) * progress) + fromValue;
            return new GridLength(currentValue, GridUnitType.Pixel);
        }

        protected override Freezable CreateInstanceCore()
        {
            return new GridLengthAnimation();
        }
    }
}

