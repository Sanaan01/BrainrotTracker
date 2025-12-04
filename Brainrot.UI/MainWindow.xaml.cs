using System;
using System.Collections.ObjectModel;
using System.Linq;
using Brainrot.Core;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Brainrot.UI
{
    public sealed partial class MainWindow : Window
    {
        private readonly BrainrotTracker _tracker;
        private readonly DispatcherTimer _timer;
        private int _tick;

        private readonly ObservableCollection<string> _rotApps;
        private readonly ObservableCollection<string> _focusApps;
        private readonly ObservableCollection<string> _neutralApps;

        private string _selectedAppName = null;
        private BrainUsageSnapshot _lastSnapshot;

        public MainWindow()
        {
            this.InitializeComponent();

            // Custom title bar region
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarHost);

            ApplyBackdrop();

            _tracker = new BrainrotTracker();

            _rotApps = new ObservableCollection<string>();
            _focusApps = new ObservableCollection<string>();
            _neutralApps = new ObservableCollection<string>();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();

            var snapshot = _tracker.GetSnapshot();
            RefreshCategoryLists();
            UpdateUi(snapshot);
        }

        private void OnTimerTick(object sender, object e)
        {
            _tracker.Tick();
            _tick++;

            // Refresh UI every 5 seconds to avoid flicker
            if (_tick % 5 == 0)
            {
                var snapshot = _tracker.GetSnapshot();
                UpdateUi(snapshot);
            }
        }

        private void UpdateUi(BrainUsageSnapshot snapshot)
        {
            _lastSnapshot = snapshot;

            var state = ComputeBrainState(snapshot);
            BrainStateText.Text = state.Label;
            EmojiText.Text = state.Emoji;

            RotText.Text = FormatTime(snapshot.RotSeconds);
            FocusText.Text = FormatTime(snapshot.FocusSeconds);
            NeutralText.Text = FormatTime(snapshot.NeutralSeconds);
            TotalTimeText.Text = FormatTime(snapshot.RotSeconds + snapshot.FocusSeconds + snapshot.NeutralSeconds);

            int rot = snapshot.RotSeconds;
            int focus = snapshot.FocusSeconds;
            int totalFocusRot = rot + focus;
            double focusFraction = totalFocusRot > 0
                ? (double)focus / totalFocusRot
                : 0.5;

            FocusVsRotBar.Value = focusFraction * 100;
            FocusVsRotLabel.Text = $"{Math.Round(focusFraction * 100)}% focus";

            var rows = snapshot.PerAppSeconds
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .Select(kvp => new AppUsageRow
                {
                    AppName = kvp.Key,
                    Duration = FormatTime(kvp.Value),
                    Category = GetCategoryLabel(kvp.Key)
                })
                .ToList();

            AppsList.ItemsSource = rows;
        }

        private void RefreshCategoryLists()
        {
            ResetCollection(_rotApps, _tracker.RotApps);
            ResetCollection(_focusApps, _tracker.FocusApps);
            ResetCollection(_neutralApps, _tracker.NeutralApps);

            RotAppsList.ItemsSource = _rotApps;
            FocusAppsList.ItemsSource = _focusApps;
            NeutralAppsList.ItemsSource = _neutralApps;
        }

        private static void ResetCollection(
            ObservableCollection<string> target,
            System.Collections.Generic.IEnumerable<string> items)
        {
            target.Clear();
            foreach (var item in items.OrderBy(i => i))
            {
                target.Add(item);
            }
        }

        // ========= Selection / Quick Categorize =========

        // Centralised: select an app and show it in Quick Categorize
        private void SelectApp(string appName)
        {
            if (string.IsNullOrEmpty(appName))
                return;

            _selectedAppName = appName;

            var usageCategory = _tracker.GetAppCategory(appName);
            var categoryLabel = GetCategoryLabel(appName);
            var (bgBrush, strokeBrush) = GetCategoryBrushes(usageCategory);

            SelectedAppCard.Background = bgBrush;
            SelectedAppCard.BorderBrush = strokeBrush;
            SelectedAppNameText.Text = appName;

            if (_lastSnapshot != null &&
                _lastSnapshot.PerAppSeconds.TryGetValue(appName, out var seconds))
            {
                SelectedAppDurationText.Text = FormatTime(seconds);
            }
            else
            {
                SelectedAppDurationText.Text = "No usage yet";
            }

            SelectedAppCard.Visibility = Visibility.Visible;

            CategoryStatusText.Text = $"Selected: {appName} ({categoryLabel})";

            FocusButton.IsEnabled = true;
            NeutralButton.IsEnabled = true;
            RotButton.IsEnabled = true;
        }

        // Tapped on any pill in Category breakdown
        private void CategoryBreakdownItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is string appName)
            {
                SelectApp(appName);
            }
        }

        // ========= Hover / Glow =========

        // hover glow for category breakdown pills
        private void CategoryBreakdownItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                if (border.Tag == null)
                {
                    border.Tag = border.Background;
                }

                if (border.Background is SolidColorBrush scb)
                {
                    border.Background = new SolidColorBrush(LightenColor(scb.Color, 0.15));
                }

                border.Opacity = 0.98;
            }
        }

        private void CategoryBreakdownItem_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                if (border.Tag is Brush original)
                {
                    border.Background = original;
                }

                border.Opacity = 1.0;
            }
        }

        // hover glow for Focus / Neutral / Rot buttons
        private void QuickCategoryButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button button)
            {
                if (button.Tag == null)
                {
                    button.Tag = button.Background;
                }

                if (button.Background is SolidColorBrush scb)
                {
                    button.Background = new SolidColorBrush(LightenColor(scb.Color, 0.15));
                }

                button.Opacity = 0.98;
            }
        }

        private void QuickCategoryButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button button)
            {
                if (button.Tag is Brush original)
                {
                    button.Background = original;
                }

                button.Opacity = 1.0;
            }
        }

        private static Color LightenColor(Color color, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            byte r = (byte)Math.Min(255, color.R + 255 * amount);
            byte g = (byte)Math.Min(255, color.G + 255 * amount);
            byte b = (byte)Math.Min(255, color.B + 255 * amount);
            return Color.FromArgb(color.A, r, g, b);
        }

        // ========= Category buttons =========

        private void CategoryButton_Focus(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedAppName))
            {
                _tracker.SetAppCategory(_selectedAppName, UsageCategory.Focus);
                RefreshAfterCategorization();
            }
        }

        private void CategoryButton_Neutral(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedAppName))
            {
                _tracker.SetAppCategory(_selectedAppName, UsageCategory.Neutral);
                RefreshAfterCategorization();
            }
        }

        private void CategoryButton_Rot(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedAppName))
            {
                _tracker.SetAppCategory(_selectedAppName, UsageCategory.Rot);
                RefreshAfterCategorization();
            }
        }

        private void RefreshAfterCategorization()
        {
            RefreshCategoryLists();
            var snapshot = _tracker.GetSnapshot();
            UpdateUi(snapshot);

            CategoryStatusText.Text = $"✓ Categorized {_selectedAppName}";
            _selectedAppName = null;

            FocusButton.IsEnabled = false;
            NeutralButton.IsEnabled = false;
            RotButton.IsEnabled = false;
            SelectedAppCard.Visibility = Visibility.Collapsed;
        }

        // ========= Helpers =========

        private static string FormatTime(int seconds)
        {
            int m = seconds / 60;
            int s = seconds % 60;
            return $"{m}m {s:00}s";
        }

        private string GetCategoryLabel(string appName)
        {
            return _tracker.GetAppCategory(appName) switch
            {
                UsageCategory.Rot => "Rot",
                UsageCategory.Focus => "Focus",
                _ => "Neutral"
            };
        }

        private (Brush Background, Brush Stroke) GetCategoryBrushes(UsageCategory category)
        {
            return category switch
            {
                UsageCategory.Focus => (
                    (Brush)Application.Current.Resources["FocusPillBackgroundBrush"],
                    (Brush)Application.Current.Resources["FocusPillStrokeBrush"]
                ),
                UsageCategory.Rot => (
                    (Brush)Application.Current.Resources["RotPillBackgroundBrush"],
                    (Brush)Application.Current.Resources["RotPillStrokeBrush"]
                ),
                _ => (
                    (Brush)Application.Current.Resources["NeutralPillBackgroundBrush"],
                    (Brush)Application.Current.Resources["NeutralPillStrokeBrush"]
                )
            };
        }

        private static (string Label, string Emoji) ComputeBrainState(BrainUsageSnapshot snapshot)
        {
            int total = snapshot.RotSeconds + snapshot.FocusSeconds + snapshot.NeutralSeconds;

            if (total < 10)
                return ("Warming up", "❓");

            double focusRatio = total > 0
                ? (double)snapshot.FocusSeconds / total
                : 0;

            if (focusRatio < 0.20) return ("Full brainrot", "💀");
            if (focusRatio < 0.50) return ("Struggling", "😵");
            if (focusRatio < 0.80) return ("Mixed", "😐");
            return ("Locked in", "🧠");
        }

        private void ApplyBackdrop()
        {
            // Use Mica if available, fall back to acrylic
            try
            {
                if (MicaController.IsSupported())
                {
                    SystemBackdrop = new MicaBackdrop
                    {
                        Kind = MicaKind.BaseAlt
                    };
                }
                else
                {
                    SystemBackdrop = new DesktopAcrylicBackdrop();
                }
            }
            catch
            {
                // If system backdrop fails for some reason, just keep default solid background
            }
        }

        // Model used to populate the ListView
        private class AppUsageRow
        {
            public string AppName { get; set; } = string.Empty;
            public string Duration { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
        }
    }
}
