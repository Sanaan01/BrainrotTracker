using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using System;
using System.Linq;
using System.Numerics;

namespace Brainrot.UI
{
    public sealed partial class TaskbarWidgetCompact : UserControl
    {
        private bool _isDragging;
        private Point _dragStart;
        private bool _isExpanded;
        private bool _dragEnabled;
        private bool _isHovered;
        
        private Compositor? _compositor;
        private SpringVector3NaturalMotionAnimation? _scaleAnimation;

        public event Action<Point>? DragDelta;
        public event Action? DragCompleted;
        public event Action? Clicked;

        public TaskbarWidgetCompact()
        {
            this.InitializeComponent();
            SetupAnimations();
        }
        
        private void SetupAnimations()
        {
            _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
            
            _scaleAnimation = _compositor.CreateSpringVector3Animation();
            _scaleAnimation.DampingRatio = 0.7f;
            _scaleAnimation.Period = TimeSpan.FromMilliseconds(50);
            _scaleAnimation.Target = "Scale";
        }

        public void Update(string label, string emoji, double focusPercent)
        {
            StateLabel.Text = label;
            FocusPercent.Text = $"{(int)focusPercent}% focus";
        }

        public void SetExpanded(bool expanded)
        {
            _isExpanded = expanded;
            AnimateHover(expanded || _isHovered);
        }

        public void SetDragEnabled(bool enabled)
        {
            _dragEnabled = enabled;
            if (!enabled)
            {
                _isDragging = false;
            }
        }
        
        public void SetDragModeVisual(bool isDragMode)
        {
        }
        
        private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isHovered = true;
            if (!_dragEnabled)
            {
                AnimateHover(true);
            }
        }
        
        private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isHovered = false;
            if (!_dragEnabled && !_isExpanded)
            {
                AnimateHover(false);
            }
        }
        
        private void AnimateHover(bool show)
        {
            if (_compositor == null) return;
            
            var visual = ElementCompositionPreview.GetElementVisual(HoverBorder);
            var animation = _compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(1f, show ? 1f : 0f);
            animation.Duration = TimeSpan.FromMilliseconds(150);
            visual.StartAnimation("Opacity", animation);
        }

        private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(RootBorder).Properties;
            if (props.IsLeftButtonPressed)
            {
                _isDragging = false;
                _dragStart = e.GetCurrentPoint(null).Position;
                if (_dragEnabled)
                {
                    RootBorder.CapturePointer(e.Pointer);
                }
                e.Handled = true;
            }
        }

        private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragEnabled)
                return;
                
            // Check if we have capture
            var captures = RootBorder.PointerCaptures;
            if (captures == null || !captures.Contains(e.Pointer))
                return;

            var current = e.GetCurrentPoint(null).Position;
            double dx = current.X - _dragStart.X;
            double dy = 0; // restrict drag to horizontal axis

            // Start drag if moved more than 2 pixels
            if (!_isDragging && Math.Abs(dx) > 2)
            {
                _isDragging = true;
            }

            if (_isDragging)
            {
                DragDelta?.Invoke(new Point(dx, dy));
                _dragStart = current;
                e.Handled = true;
            }
        }

        private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragEnabled)
            {
                Clicked?.Invoke();
                e.Handled = true;
                return;
            }

            bool wasDragging = _isDragging;
            _isDragging = false;
            RootBorder.ReleasePointerCapture(e.Pointer);

            if (wasDragging)
            {
                DragCompleted?.Invoke();
            }
            else
            {
                Clicked?.Invoke();
            }
            e.Handled = true;
        }
        
        private void Root_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                DragCompleted?.Invoke();
            }
        }

        private static Color ColorFromHex(string hex)
        {
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);

            byte a = 255;
            int offset = 0;
            if (hex.Length == 8)
            {
                a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                offset = 2;
            }

            byte r = byte.Parse(hex.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber);
            return Color.FromArgb(a, r, g, b);
        }
    }
}
