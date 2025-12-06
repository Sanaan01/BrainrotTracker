using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Brainrot.Core;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Text;
using Windows.UI;
using Windows.Storage;
using Process = System.Diagnostics.Process;

namespace Brainrot.UI
{
    public sealed partial class MainWindow : Window
    {
        private const string NavTagDashboard = "dashboard";
        private const string NavTagStats = "stats";
        private const string NavTagSettings = "settings";

        private BrainrotTracker? _tracker;
        private readonly DispatcherTimer _timer;
        private int _tick;
        private bool _isLoading;

        private readonly ObservableCollection<string> _rotApps;
        private readonly ObservableCollection<string> _focusApps;
        private readonly ObservableCollection<string> _neutralApps;

        private string _selectedAppName = null;
        private BrainUsageSnapshot _lastSnapshot;
        private StatsInterval _currentInterval = StatsInterval.Daily;
        private IReadOnlyList<UsageTimelineBin>? _lastTimelineBins;
        private List<TimelinePoint> _timelinePoints = new();
        private Line? _timelineHoverLine;
        private Ellipse? _timelineHoverDotFocus;
        private Ellipse? _timelineHoverDotNeutral;
        private Ellipse? _timelineHoverDotRot;
        private Border? _timelineTooltip;
        private TextBlock? _tooltipTitle;
        private TextBlock? _tooltipFocus;
        private TextBlock? _tooltipNeutral;
        private TextBlock? _tooltipRot;
        private TaskbarWidget? _taskbarWidget;
        private TrayIconHost? _trayIcon;
        private int _manualOffset = -1;
        public MainWindow()
        {
            this.InitializeComponent();

            // Custom title bar region
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarHost);

            ApplyBackdrop();

            _rotApps = new ObservableCollection<string>();
            _focusApps = new ObservableCollection<string>();
            _neutralApps = new ObservableCollection<string>();

            _isLoading = true;
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTimerTick;

            CategoryStatusText.Text = "Loading data...";
            FocusButton.IsEnabled = false;
            NeutralButton.IsEnabled = false;
            RotButton.IsEnabled = false;

            ShowLoading("Loading data...");

            if (Content is FrameworkElement root)
            {
                root.Loaded += MainWindow_Loaded;
            }
            else
            {
                // Fallback: run init immediately if content is unavailable
                _ = InitializeAsync();
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                _tracker = await Task.Run(() => new BrainrotTracker());
                var snapshot = await Task.Run(() => _tracker.GetSnapshot());
                RefreshCategoryLists();
                UpdateUi(snapshot);
                _timer.Start();
                SetupTrayAndWidget();
                ShowWidget();
            }
            catch
            {
                CategoryStatusText.Text = "Failed to load data";
            }
            finally
            {
                HideLoading();
            }
        }

        private void OnTimerTick(object sender, object e)
        {
            if (_tracker == null)
                return;

            bool refreshNow = _tracker.Tick();
            _tick++;

            // If we just discovered a brand-new app, refresh immediately so it shows up.
            if (refreshNow)
            {
                var snapshot = _tracker.GetSnapshot();
                UpdateUi(snapshot);
                return;
            }

            // Otherwise refresh UI every 5 seconds to avoid flicker
            if (_tick % 5 == 0)
            {
                var snapshot = _tracker.GetSnapshot();
                UpdateUi(snapshot);
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
                return;

            bool isDashboard = string.Equals(tag, NavTagDashboard, StringComparison.OrdinalIgnoreCase);
            bool isStats = string.Equals(tag, NavTagStats, StringComparison.OrdinalIgnoreCase);
            DashboardView.Visibility = isDashboard ? Visibility.Visible : Visibility.Collapsed;
            StatsView.Visibility = isStats ? Visibility.Visible : Visibility.Collapsed;
            SettingsView.Visibility = string.Equals(tag, NavTagSettings, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (isStats)
            {
                LoadStatsInterval(_currentInterval);
            }
        }

        private void UpdateUi(BrainUsageSnapshot snapshot)
        {
            _lastSnapshot = snapshot;

            // Keep category lists in sync with any newly discovered apps (default Neutral).
            RefreshCategoryLists();

            var state = ComputeBrainState(snapshot);
            ApplyBrainVisual(state);

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

            if (StatsView.Visibility == Visibility.Visible)
            {
                LoadStatsInterval(_currentInterval);
            }

            RenderTimeline();
        }

        private void RefreshCategoryLists()
        {
            if (_tracker == null)
                return;

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

            if (_tracker == null || _isLoading)
                return;

            _selectedAppName = appName;

            var usageCategory = _tracker.GetAppCategory(appName);
            var categoryLabel = GetCategoryLabel(appName);
            var (bgBrush, strokeBrush) = GetCategoryBrushes(usageCategory);

            SelectedAppCard.Background = bgBrush;
            SelectedAppCard.BorderBrush = strokeBrush;
            SelectedAppNameText.Text = appName;
            SelectedAppIcon.Source = ProcessIconProvider.GetIcon(appName);

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

        private void ShowLoading(string message)
        {
            _isLoading = true;
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingStatusText.Text = message;
        }

        private void HideLoading()
        {
            _isLoading = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        // ========= Category buttons =========

        private void CategoryButton_Focus(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedAppName))
            {
                _tracker?.SetAppCategory(_selectedAppName, UsageCategory.Focus);
                RefreshAfterCategorization();
            }
        }

        private void CategoryButton_Neutral(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedAppName))
            {
                _tracker?.SetAppCategory(_selectedAppName, UsageCategory.Neutral);
                RefreshAfterCategorization();
            }
        }

        private void CategoryButton_Rot(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedAppName))
            {
                _tracker?.SetAppCategory(_selectedAppName, UsageCategory.Rot);
                RefreshAfterCategorization();
            }
        }

        private void IntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_tracker == null || _isLoading)
                return;

            if (IntervalCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
            {
                _currentInterval = tag switch
                {
                    "monthly" => StatsInterval.Monthly,
                    "weekly" => StatsInterval.Weekly,
                    _ => StatsInterval.Daily
                };

                LoadStatsInterval(_currentInterval);
            }
        }

        private void RefreshAfterCategorization()
        {
            RefreshCategoryLists();
            var snapshot = _tracker?.GetSnapshot();
            if (snapshot != null)
            {
                UpdateUi(snapshot);
            }

            CategoryStatusText.Text = $"? Categorized {_selectedAppName}";
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
            if (_tracker == null)
            {
                return "Neutral";
            }

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

        private static BrainVisualState ComputeBrainState(BrainUsageSnapshot snapshot)
        {
            int total = snapshot.RotSeconds + snapshot.FocusSeconds + snapshot.NeutralSeconds;

            if (total < 10)
                return new BrainVisualState("Warming up", "??", null);

            double focusRatio = total > 0
                ? (double)snapshot.FocusSeconds / total
                : 0;

            if (focusRatio < 0.20) return new BrainVisualState("Full brainrot", "??", null);
            if (focusRatio < 0.50) return new BrainVisualState("Struggling", "?????", null);
            if (focusRatio < 0.80) return new BrainVisualState("Mixed", "??", null);
            return new BrainVisualState("Locked in", "??", "Assets/Brain/brainhappy.gif");
        }



        private void ApplyBrainVisual(BrainVisualState state)
        {
            BrainStateText.Text = state.Label;
            EmojiText.Text = state.Emoji;

            if (!string.IsNullOrWhiteSpace(state.MediaPath) &&
                state.MediaPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri($"ms-appx:///{state.MediaPath.Replace("\\", "/")}");
                    BrainHappyBrush.ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
                    BrainHappyEllipse.Visibility = Visibility.Visible;
                    EmojiText.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    BrainHappyEllipse.Visibility = Visibility.Collapsed;
                    EmojiText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                BrainHappyBrush.ImageSource = null;
                BrainHappyEllipse.Visibility = Visibility.Collapsed;
                EmojiText.Visibility = Visibility.Visible;
            }

            UpdateTaskbarWidgetSnapshot();
        }

        private void SetupTrayAndWidget()
        {
            if (_trayIcon != null)
                return;

            LoadWidgetOffset();
            _taskbarWidget = new TaskbarWidget();
            if (_manualOffset >= 0)
            {
                _taskbarWidget.SetPositionOffset(_manualOffset);
            }
            _taskbarWidget.OpenMainWindowRequested += () => DispatcherQueue.TryEnqueue(() =>
            {
                this.Activate();
            });
            _taskbarWidget.PositionChanged += offset =>
            {
                _manualOffset = Math.Max(0, offset);
                SaveWidgetOffset();
                TrayStatusText.Text = "Widget position saved.";
            };
            UpdateTaskbarWidgetSnapshot();

            _trayIcon = new TrayIconHost("Brainrot Tracker");
            _trayIcon.Click += () => DispatcherQueue.TryEnqueue(() =>
            {
                ShowWidget();
            });
            _trayIcon.MenuCommand += cmd => DispatcherQueue.TryEnqueue(async () =>
            {
                switch (cmd)
                {
                    case TrayMenuCommand.Show:
                        ShowWidget();
                        break;
                    case TrayMenuCommand.Hide:
                        HideWidget();
                        break;
                    case TrayMenuCommand.Reposition:
                        BeginWidgetReposition();
                        break;
                    case TrayMenuCommand.ManualPosition:
                        BeginWidgetReposition();
                        break;
                    case TrayMenuCommand.Quit:
                        Application.Current?.Exit();
                        break;
                }
            });
            bool added = _trayIcon.AddIcon(TrayIconHost.GetAppIconHandle());

            // Fallback: if tray icon failed to add, still show the widget and notify
            if (!added)
            {
                ShowWidget();
                TrayStatusText.Text = "Tray icon unavailable; widget shown.";
            }
            else
            {
                TrayStatusText.Text = "Tray icon added.";
            }

            this.Closed += (_, __) =>
            {
                _trayIcon?.Dispose();
                _taskbarWidget?.Dispose();
            };
        }

        private void UpdateTaskbarWidgetSnapshot()
        {
            if (_taskbarWidget == null || _lastSnapshot == null)
                return;

            var state = ComputeBrainState(_lastSnapshot);
            var focus = FormatTime(_lastSnapshot.FocusSeconds);
            var neutral = FormatTime(_lastSnapshot.NeutralSeconds);
            var rot = FormatTime(_lastSnapshot.RotSeconds);
            double focusPct = FocusVsRotBar.Value;
            _taskbarWidget.UpdateSnapshot(state.Label, state.Emoji, focus, neutral, rot, focusPct);
        }

        private void BeginWidgetReposition()
        {
            ShowWidget(forceReset: false);
            _taskbarWidget?.StartDraggingMode();
            _taskbarWidget?.BringToFront();
            TrayStatusText.Text = "Drag the taskbar widget, then release to save its spot.";
        }

        private void ShowWidget(bool forceReset = false)
        {
            UpdateTaskbarWidgetSnapshot();
            if (_taskbarWidget == null)
                return;

            if (forceReset)
                _manualOffset = -1;

            _taskbarWidget.SetPositionOffset(_manualOffset);
            _taskbarWidget.MoveToTaskbarSlot(_manualOffset, log: true);
            _taskbarWidget.ShowWidget();
            SaveWidgetOffset();
        }

        private void HideWidget()
        {
            _taskbarWidget?.HideWidget();
        }

        private void LoadWidgetOffset()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings.Values;
                if (settings.TryGetValue("WidgetOffset", out var offObj))
                {
                    _manualOffset = Convert.ToInt32(offObj);
                }
            }
            catch
            {
                _manualOffset = -1;
            }
        }

        private void SaveWidgetOffset()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings.Values;
                settings["WidgetOffset"] = _manualOffset;
            }
            catch
            {
                // ignore persistence issues
            }
        }

        private void AddTrayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_trayIcon == null)
            {
                SetupTrayAndWidget();
                return;
            }

            bool added = _trayIcon.IsAdded || _trayIcon.AddIcon(TrayIconHost.GetAppIconHandle());
            TrayStatusText.Text = added ? "Tray icon added." : "Tray icon failed to add.";
        }

        private void ShowWidgetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_taskbarWidget == null)
            {
                SetupTrayAndWidget();
            }
            ShowWidget(forceReset: true);
            TrayStatusText.Text = "Widget shown.";
        }

        private sealed class BrainVisualState
        {
            public BrainVisualState(string label, string emoji, string mediaPath)
            {
                Label = label;
                Emoji = emoji;
                MediaPath = mediaPath;
            }

            public string Label { get; }
            public string Emoji { get; }
            public string MediaPath { get; }
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

        private void LoadStatsInterval(StatsInterval interval)
        {
            if (_tracker == null)
                return;

            _currentInterval = interval;

            if (interval == StatsInterval.Daily)
            {
                var snapshot = _tracker.GetSnapshot();
                int totalSeconds = snapshot.RotSeconds + snapshot.FocusSeconds + snapshot.NeutralSeconds;
                StatsTotalText.Text = FormatTime(totalSeconds);
                StatsFocusText.Text = FormatTime(snapshot.FocusSeconds);
                StatsRotText.Text = FormatTime(snapshot.RotSeconds);

                var points = snapshot.PerAppSeconds
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(12)
                    .ToList();

                double max = points.Any() ? points.Max(p => p.Value) : 0;
                var normalized = points.Select(p => new StatsPoint
                {
                    Label = p.Key,
                    DisplayDuration = FormatTime(p.Value),
                    NormalizedValue = max > 0 ? p.Value / max : 0
                }).ToList();

                if (!normalized.Any())
                {
                    normalized.Add(new StatsPoint { Label = "No data", DisplayDuration = "0m 00s", NormalizedValue = 0 });
                }

                StatsBars.ItemsSource = normalized;

                var start = DateTime.Today;
                var end = start.AddDays(1);
                _lastTimelineBins = _tracker.GetTimelineBins(start, end, TimeSpan.FromHours(1)).ToList();
                _timelinePoints = _lastTimelineBins
                    .Select(b => new TimelinePoint
                    {
                        Label = b.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                        FocusSeconds = b.FocusSeconds,
                        NeutralSeconds = b.NeutralSeconds,
                        RotSeconds = b.RotSeconds,
                        Start = b.Start
                    })
                    .ToList();
                RenderTimeline();
                return;
            }

            // Weekly / Monthly aggregates
            int days = interval == StatsInterval.Weekly ? 7 : 30;
            var aggregates = _tracker.GetAggregates(days).ToList();

            int total = aggregates.Sum(a => a.TotalSeconds);
            int focus = aggregates.Sum(a => a.FocusSeconds);
            int rot = aggregates.Sum(a => a.RotSeconds);

            StatsTotalText.Text = FormatTime(total);
            StatsFocusText.Text = FormatTime(focus);
            StatsRotText.Text = FormatTime(rot);

            double maxAgg = aggregates.Any() ? aggregates.Max(a => a.TotalSeconds) : 0;
            var bars = aggregates.Select(a => new StatsPoint
            {
                Label = a.Date.ToString("MMM dd"),
                DisplayDuration = FormatTime(a.TotalSeconds),
                NormalizedValue = maxAgg > 0 ? (double)a.TotalSeconds / maxAgg : 0
            }).ToList();

            if (!bars.Any())
            {
                bars.Add(new StatsPoint { Label = "No data", DisplayDuration = "0m 00s", NormalizedValue = 0 });
            }

            StatsBars.ItemsSource = bars;

            _lastTimelineBins = aggregates.Select(a => new UsageTimelineBin(a.Date.ToDateTime(TimeOnly.MinValue))
            {
                FocusSeconds = a.FocusSeconds,
                RotSeconds = a.RotSeconds,
                NeutralSeconds = a.NeutralSeconds
            }).ToList();
            _timelinePoints = _lastTimelineBins
                .Select(b => new TimelinePoint
                {
                    Label = _currentInterval == StatsInterval.Weekly
                        ? b.Start.ToString("ddd", CultureInfo.InvariantCulture)
                        : b.Start.ToString("MMM dd", CultureInfo.InvariantCulture),
                    FocusSeconds = b.FocusSeconds,
                    NeutralSeconds = b.NeutralSeconds,
                    RotSeconds = b.RotSeconds,
                    Start = b.Start
                })
                .ToList();
            RenderTimeline();
        }

        private enum StatsInterval
        {
            Daily,
            Weekly,
            Monthly
        }

        private class StatsPoint
        {
            public string Label { get; set; } = string.Empty;
            public string DisplayDuration { get; set; } = string.Empty;
            public double NormalizedValue { get; set; }
        }

        private class TimelinePoint
        {
            public string Label { get; set; } = string.Empty;
            public int FocusSeconds { get; set; }
            public int NeutralSeconds { get; set; }
            public int RotSeconds { get; set; }
            public DateTime Start { get; set; }
        }

        private void TimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderTimeline();
        }

        private void RenderTimeline()
        {
            if (_timelinePoints == null || _timelinePoints.Count == 0)
            {
                TimelineCanvas.Children.Clear();
                return;
            }

            double width = TimelineCanvas.ActualWidth;
            double height = TimelineCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                width = TimelineCanvas.Width > 0 ? TimelineCanvas.Width : 600;
                height = TimelineCanvas.Height > 0 ? TimelineCanvas.Height : 240;
            }

            var maxValue = _timelinePoints.Max(b => b.FocusSeconds + b.NeutralSeconds + b.RotSeconds);
            if (maxValue <= 0)
                maxValue = 1;

            var focusSeries = _timelinePoints.Select(b => (double)b.FocusSeconds).ToList();
            var neutralSeries = _timelinePoints.Select(b => (double)b.FocusSeconds + b.NeutralSeconds).ToList();
            var rotSeries = _timelinePoints.Select(b => (double)b.FocusSeconds + b.NeutralSeconds + b.RotSeconds).ToList();

            TimelineCanvas.Children.Clear();
            _timelineHoverLine = null;
            _timelineHoverDotFocus = null;
            _timelineHoverDotNeutral = null;
            _timelineHoverDotRot = null;
            _timelineTooltip = null;
            _tooltipTitle = null;
            _tooltipFocus = null;
            _tooltipNeutral = null;
            _tooltipRot = null;

            void DrawArea(IReadOnlyList<double> upper, IReadOnlyList<double> lower, string colorHex, double opacity)
            {
                var polygon = new Polygon
                {
                    Fill = new SolidColorBrush(ColorFromHex(colorHex, opacity)),
                    StrokeThickness = 0,
                    IsHitTestVisible = false
                };

                int count = upper.Count;
                double step = count > 1 ? width / (count - 1) : width;

                for (int i = 0; i < count; i++)
                {
                    double x = i * step;
                    double yLower = height - (lower[i] / maxValue) * height;
                    polygon.Points.Add(new Windows.Foundation.Point(x, yLower));
                }

                for (int i = count - 1; i >= 0; i--)
                {
                    double x = i * step;
                    double yUpper = height - (upper[i] / maxValue) * height;
                    polygon.Points.Add(new Windows.Foundation.Point(x, yUpper));
                }

                TimelineCanvas.Children.Add(polygon);
            }

            DrawArea(focusSeries, Enumerable.Repeat(0d, focusSeries.Count).ToList(), "#4A90E2", 0.65);
            DrawArea(neutralSeries, focusSeries, "#7AA0FF", 0.45);
            DrawArea(rotSeries, neutralSeries, "#C06576", 0.50);

            DrawAxes(width, height, maxValue);

            // Hover affordances
            EnsureHoverVisuals();
        }

        private void DrawAxes(double width, double height, double maxValue)
        {
            // Y axis percentages
            foreach (var child in TimelineCanvas.Children.OfType<TextBlock>().ToList())
            {
                if (child.Tag as string == "axis")
                {
                    TimelineCanvas.Children.Remove(child);
                }
            }

            int[] percents = { 0, 25, 50, 75, 100 };
            foreach (var pct in percents)
            {
                double y = height - (pct / 100.0) * height;
                var tb = new TextBlock
                {
                    Text = $"{pct}%",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(ColorFromHex("#FFFFFF", 0.6)),
                    Tag = "axis",
                    IsHitTestVisible = false
                };
                TimelineCanvas.Children.Add(tb);
                Canvas.SetLeft(tb, -30);
                Canvas.SetTop(tb, y - 8);
            }

            // X axis labels
            int count = _timelinePoints.Count;
            if (count == 0) return;
            double step = count > 1 ? width / (count - 1) : width;
            int labelEvery = Math.Max(1, count / 8);

            for (int i = 0; i < count; i += labelEvery)
            {
                var p = _timelinePoints[i];
                var tb = new TextBlock
                {
                    Text = p.Label,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(ColorFromHex("#FFFFFF", 0.6)),
                    Tag = "axis",
                    IsHitTestVisible = false
                };
                TimelineCanvas.Children.Add(tb);
                double x = i * step;
                Canvas.SetLeft(tb, x - 10);
                Canvas.SetTop(tb, height + 4);
            }
        }

        private void EnsureHoverVisuals()
        {
            if (_timelineHoverLine == null)
            {
                _timelineHoverLine = new Line
                {
                    Stroke = new SolidColorBrush(ColorFromHex("#FFFFFF", 0.6)),
                    StrokeThickness = 1.5,
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false
                };
                TimelineCanvas.Children.Add(_timelineHoverLine);
            }

            if (_timelineHoverDotFocus == null)
            {
                _timelineHoverDotFocus = CreateDot("#4A90E2");
                _timelineHoverDotNeutral = CreateDot("#7AA0FF");
                _timelineHoverDotRot = CreateDot("#C06576");
                TimelineCanvas.Children.Add(_timelineHoverDotRot);
                TimelineCanvas.Children.Add(_timelineHoverDotNeutral);
                TimelineCanvas.Children.Add(_timelineHoverDotFocus);
            }

            if (_timelineTooltip == null)
            {
                _tooltipTitle = new TextBlock { FontWeight = FontWeights.SemiBold };
                _tooltipFocus = new TextBlock { Foreground = new SolidColorBrush(ColorFromHex("#4A90E2")) };
                _tooltipNeutral = new TextBlock { Foreground = new SolidColorBrush(ColorFromHex("#7AA0FF")) };
                _tooltipRot = new TextBlock { Foreground = new SolidColorBrush(ColorFromHex("#C06576")) };

                var stack = new StackPanel { Spacing = 4 };
                stack.Children.Add(_tooltipTitle);
                stack.Children.Add(_tooltipFocus);
                stack.Children.Add(_tooltipNeutral);
                stack.Children.Add(_tooltipRot);

                _timelineTooltip = new Border
                {
                    Background = new SolidColorBrush(ColorFromHex("#202020", 0.9)),
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(8),
                    Visibility = Visibility.Collapsed,
                    Child = stack,
                    IsHitTestVisible = false
                };
                TimelineCanvas.Children.Add(_timelineTooltip);
            }
        }

        private Ellipse CreateDot(string colorHex)
        {
            return new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(ColorFromHex(colorHex)),
                Stroke = new SolidColorBrush(ColorFromHex("#FFFFFF", 0.8)),
                StrokeThickness = 1.5,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
        }

        private void TimelineCanvas_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_timelineHoverLine != null) _timelineHoverLine.Visibility = Visibility.Collapsed;
            if (_timelineHoverDotFocus != null) _timelineHoverDotFocus.Visibility = Visibility.Collapsed;
            if (_timelineHoverDotNeutral != null) _timelineHoverDotNeutral.Visibility = Visibility.Collapsed;
            if (_timelineHoverDotRot != null) _timelineHoverDotRot.Visibility = Visibility.Collapsed;
            if (_timelineTooltip != null) _timelineTooltip.Visibility = Visibility.Collapsed;
        }

        private void TimelineCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_timelinePoints == null || _timelinePoints.Count == 0)
                return;

            var pos = e.GetCurrentPoint(TimelineCanvas).Position;
            double width = TimelineCanvas.ActualWidth;
            double height = TimelineCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
                return;

            int count = _timelinePoints.Count;
            double step = count > 1 ? width / (count - 1) : width;
            int idx = (int)Math.Round(pos.X / step);
            idx = Math.Clamp(idx, 0, count - 1);

            var point = _timelinePoints[idx];
            double maxValue = _timelinePoints.Max(b => b.FocusSeconds + b.NeutralSeconds + b.RotSeconds);
            if (maxValue <= 0) maxValue = 1;

            double x = idx * step;
            double focusY = height - (point.FocusSeconds / maxValue) * height;
            double neutralY = height - ((point.FocusSeconds + point.NeutralSeconds) / maxValue) * height;
            double rotY = height - ((point.FocusSeconds + point.NeutralSeconds + point.RotSeconds) / maxValue) * height;

            EnsureHoverVisuals();

            if (_timelineHoverLine != null)
            {
                _timelineHoverLine.X1 = x;
                _timelineHoverLine.X2 = x;
                _timelineHoverLine.Y1 = 0;
                _timelineHoverLine.Y2 = height;
                _timelineHoverLine.Visibility = Visibility.Visible;
            }

            void PlaceDot(Ellipse dot, double y)
            {
                if (dot == null) return;
                Canvas.SetLeft(dot, x - dot.Width / 2);
                Canvas.SetTop(dot, y - dot.Height / 2);
                dot.Visibility = Visibility.Visible;
            }

            PlaceDot(_timelineHoverDotFocus!, focusY);
            PlaceDot(_timelineHoverDotNeutral!, neutralY);
            PlaceDot(_timelineHoverDotRot!, rotY);

            if (_timelineTooltip != null && _tooltipTitle != null && _tooltipFocus != null && _tooltipNeutral != null && _tooltipRot != null)
            {
                _tooltipTitle.Text = point.Label;
                _tooltipFocus.Text = $"Focus: {FormatTime(point.FocusSeconds)}";
                _tooltipNeutral.Text = $"Neutral: {FormatTime(point.NeutralSeconds)}";
                _tooltipRot.Text = $"Rot: {FormatTime(point.RotSeconds)}";
                _tooltipTitle.Foreground = new SolidColorBrush(ColorFromHex("#FFFFFF"));

                double tooltipX = x + 12;
                double tooltipY = Math.Min(focusY, Math.Min(neutralY, rotY)) - 10;
                if (tooltipY < 0) tooltipY = 0;
                Canvas.SetLeft(_timelineTooltip, tooltipX);
                Canvas.SetTop(_timelineTooltip, tooltipY);
                _timelineTooltip.Visibility = Visibility.Visible;
            }
        }

        private static Color ColorFromHex(string hex, double opacity = 1.0)
        {
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);

            byte a = 255;
            if (hex.Length == 8)
            {
                a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                hex = hex.Substring(2);
            }

            byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);

            a = (byte)(a * opacity);
            return Color.FromArgb(a, r, g, b);
        }

        private async void DeleteStartButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Delete all history?",
                Content = "This action is permanent and will erase all usage history.",
                PrimaryButtonText = "Yes, delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                Background = new SolidColorBrush(ColorFromHex("#202020")),
                Foreground = new SolidColorBrush(Colors.White),
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                PerformDeleteData();
            }
        }

        private void PerformDeleteData()
        {
            if (_tracker == null)
                return;

            _tracker.ClearAllData();
            RefreshCategoryLists();
            var snapshot = _tracker.GetSnapshot();
            UpdateUi(snapshot);
        }
    }
}
