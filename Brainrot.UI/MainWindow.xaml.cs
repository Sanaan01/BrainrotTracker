using System;
using System.Collections.ObjectModel;
using System.Linq;
using Brainrot.Core;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

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

        public MainWindow()
        {
            this.InitializeComponent();

            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop();
            }

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
            var state = ComputeBrainState(snapshot);
            BrainStateText.Text = state.Label;
            EmojiText.Text = state.Emoji;

            RotText.Text = FormatTime(snapshot.RotSeconds);
            FocusText.Text = FormatTime(snapshot.FocusSeconds);
            NeutralText.Text = FormatTime(snapshot.NeutralSeconds);

            int rot = snapshot.RotSeconds;
            int focus = snapshot.FocusSeconds;
            int totalFocusRot = rot + focus;

            double focusFraction =
                totalFocusRot > 0 ? (double)focus / totalFocusRot : 0.5;

            FocusVsRotBar.Value = focusFraction * 100;
            FocusVsRotLabel.Text = $"{Math.Round(focusFraction * 100)}% focus";

            var rows = snapshot.PerAppSeconds
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .Select(kvp =>
                    new AppUsageRow
                    {
                        AppName = kvp.Key,
                        Duration = FormatTime(kvp.Value),
                        Category = string.Empty // category not shown yet
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

        private static void ResetCollection(ObservableCollection<string> target, System.Collections.Generic.IEnumerable<string> items)
        {
            target.Clear();

            foreach (var item in items.OrderBy(i => i))
            {
                target.Add(item);
            }
        }

        private void AddToRot_Click(object sender, RoutedEventArgs e)
        {
            AddAppToCategory(UsageCategory.Rot);
        }

        private void AddToFocus_Click(object sender, RoutedEventArgs e)
        {
            AddAppToCategory(UsageCategory.Focus);
        }

        private void AddToNeutral_Click(object sender, RoutedEventArgs e)
        {
            AddAppToCategory(UsageCategory.Neutral);
        }

        private void AddAppToCategory(UsageCategory category)
        {
            var appName = AppNameInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(appName))
            {
                return;
            }

            _tracker.SetAppCategory(appName, category);
            AppNameInput.Text = string.Empty;
            RefreshCategoryLists();
            UpdateUi(_tracker.GetSnapshot());
        }

        private static string FormatTime(int seconds)
        {
            int m = seconds / 60;
            int s = seconds % 60;
            return $"{m}m {s:00}s";
        }

        private static (string Label, string Emoji) ComputeBrainState(BrainUsageSnapshot snapshot)
        {
            int total = snapshot.RotSeconds + snapshot.FocusSeconds + snapshot.NeutralSeconds;

            if (total < 10)
                return ("Warming up", "❓");

            double focusRatio =
                total > 0 ? (double)snapshot.FocusSeconds / total : 0;

            if (focusRatio < 0.20) return ("Full brainrot", "💀");
            if (focusRatio < 0.50) return ("Struggling", "😵");
            if (focusRatio < 0.80) return ("Mixed", "😐");
            return ("Locked in", "🧠");
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
