using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Brainrot.Core;

namespace Brainrot.UI
{
    public sealed partial class MainWindow : Window
    {
        private readonly BrainrotTracker _tracker;
        private readonly DispatcherTimer _timer;
        private int _tick;

        public MainWindow()
        {
            this.InitializeComponent();

            _tracker = new BrainrotTracker();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
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
                        Category = "" // category not shown yet
                    })
                .ToList();

            AppsList.ItemsSource = rows;
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
            public string AppName { get; set; } = "";
            public string Duration { get; set; } = "";
            public string Category { get; set; } = "";
        }
    }
}
