using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;


namespace Brainrot.Core
{
    public sealed class BrainrotTracker
    {
        private readonly HashSet<string> _rotApps;
        private readonly HashSet<string> _focusApps;
        private readonly HashSet<string> _neutralApps;
        private readonly IUsageRepository _usageRepository;

        private Dictionary<string, int> _perAppSeconds;
        private int _rotSeconds;
        private int _focusSeconds;
        private int _neutralSeconds;

        private DateOnly _currentDate;

        private readonly string _selfProcessName = Process.GetCurrentProcess().ProcessName;

        public BrainrotTracker(IUsageRepository? usageRepository = null)
        {
            // Apps counted as "brainrot"
            _rotApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "chrome",
                "msedge",
                "discord",
                "steam",
                "Spotify",
                "TikTok",   // desktop clients, just in case
                "Instagram"
            };

            // Apps counted as "focus"
            _focusApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Code",     // VS Code
                "devenv",   // Visual Studio
                "WINWORD",
                "EXCEL",
                "POWERPNT",
                "notepad",
                "Notion"
            };

            _neutralApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _usageRepository = usageRepository ?? new LiteDbUsageRepository();

            _currentDate = DateOnly.FromDateTime(DateTime.Now);
            var snapshot = _usageRepository.GetSnapshotForDate(_currentDate);
            _rotSeconds = snapshot.RotSeconds;
            _focusSeconds = snapshot.FocusSeconds;
            _neutralSeconds = snapshot.NeutralSeconds;
            _perAppSeconds = new Dictionary<string, int>(snapshot.PerAppSeconds, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Call this once per second.
        /// </summary>
        public void Tick()
        {
            EnsureCurrentDate();

            var processName = NativeMethods.GetActiveProcessName();
            if (string.IsNullOrWhiteSpace(processName))
                return;

            // Ignore our own tracker window so it doesn't show up in stats
            if (string.Equals(processName, _selfProcessName, StringComparison.OrdinalIgnoreCase))
                return;


            if (!_perAppSeconds.TryGetValue(processName, out var seconds))
            {
                seconds = 0;
            }

            _perAppSeconds[processName] = seconds + 1;

            UsageCategory category;
            if (_rotApps.Contains(processName))
            {
                category = UsageCategory.Rot;
                _rotSeconds++;
            }
            else if (_focusApps.Contains(processName))
            {
                category = UsageCategory.Focus;
                _focusSeconds++;
            }
            else
            {
                category = UsageCategory.Neutral;
                _neutralSeconds++;
            }

            _usageRepository.AppendUsage(new UsageEntry
            {
                Timestamp = DateTime.Now,
                AppName = processName,
                Category = category,
                DurationSeconds = 1
            });
        }

        public BrainUsageSnapshot GetSnapshot()
        {
            return new BrainUsageSnapshot(
                _rotSeconds,
                _focusSeconds,
                _neutralSeconds,
                new Dictionary<string, int>(_perAppSeconds)
            );
        }

        public IReadOnlyCollection<string> RotApps => _rotApps;

        public IReadOnlyCollection<string> FocusApps => _focusApps;

        public IReadOnlyCollection<string> NeutralApps => _neutralApps;

        public IEnumerable<KeyValuePair<string, int>> GetTopApps(int count = 10)
        {
            return _perAppSeconds
                .OrderByDescending(kvp => kvp.Value)
                .Take(count);
        }

        public void SetAppCategory(string appName, UsageCategory category)
        {
            if (string.IsNullOrWhiteSpace(appName))
            {
                return;
            }

            var normalized = appName.Trim();

            // Remove from all buckets before re-adding
            _rotApps.Remove(normalized);
            _focusApps.Remove(normalized);
            _neutralApps.Remove(normalized);

            switch (category)
            {
                case UsageCategory.Rot:
                    _rotApps.Add(normalized);
                    break;
                case UsageCategory.Focus:
                    _focusApps.Add(normalized);
                    break;
                case UsageCategory.Neutral:
                    _neutralApps.Add(normalized);
                    break;
            }
        }

        private void EnsureCurrentDate()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (today == _currentDate)
                return;

            _currentDate = today;
            var snapshot = _usageRepository.GetSnapshotForDate(_currentDate);
            _rotSeconds = snapshot.RotSeconds;
            _focusSeconds = snapshot.FocusSeconds;
            _neutralSeconds = snapshot.NeutralSeconds;
            _perAppSeconds = new Dictionary<string, int>(snapshot.PerAppSeconds, StringComparer.OrdinalIgnoreCase);
        }
    }
}
