using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;


namespace Brainrot.Core
{
    public sealed class BrainrotTracker
    {
        private const int AutoAddThresholdSeconds = 5;

        private readonly HashSet<string> _rotApps;
        private readonly HashSet<string> _focusApps;
        private readonly HashSet<string> _neutralApps;
        private readonly HashSet<string> _ignoredApps;
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
            _ignoredApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ApplicationFrameHost",
                "ShellExperienceHost",
                "RuntimeBroker",
                "SearchHost",
                "dllhost",
                "sihost",
                "ctfmon",
                "TextInputHost",
                "SystemSettings",
                "Idle",
                "explorer" // omit shell by default to avoid noise
            };

            _usageRepository = usageRepository ?? new LiteDbUsageRepository();

            _currentDate = DateOnly.FromDateTime(DateTime.Now);
            var snapshot = _usageRepository.GetSnapshotForDate(_currentDate);
            _rotSeconds = snapshot.RotSeconds;
            _focusSeconds = snapshot.FocusSeconds;
            _neutralSeconds = snapshot.NeutralSeconds;
            _perAppSeconds = new Dictionary<string, int>(snapshot.PerAppSeconds, StringComparer.OrdinalIgnoreCase);

            // Restore persisted category choices
            var persisted = _usageRepository.LoadCategories();
            foreach (var kvp in persisted)
            {
                var name = kvp.Key;
                switch (kvp.Value)
                {
                    case UsageCategory.Rot:
                        _rotApps.Add(name);
                        break;
                    case UsageCategory.Focus:
                        _focusApps.Add(name);
                        break;
                    case UsageCategory.Neutral:
                        _neutralApps.Add(name);
                        break;
                }
            }
        }

        /// <summary>
        /// Call this once per second. Returns true if we surfaced a new app into the category lists.
        /// </summary>
        public bool Tick()
        {
            EnsureCurrentDate();

            var processName = NativeMethods.GetActiveProcessName();
            if (string.IsNullOrWhiteSpace(processName))
                return false;

            // Ignore our own tracker window so it doesn't show up in stats
            if (string.Equals(processName, _selfProcessName, StringComparison.OrdinalIgnoreCase))
                return false;

            // Ignore noisy system/host processes
            if (_ignoredApps.Contains(processName))
                return false;

            var isNewInUsage = !_perAppSeconds.TryGetValue(processName, out var seconds);
            if (isNewInUsage)
                seconds = 0;

            _perAppSeconds[processName] = seconds + 1;

            bool addedToCategories = false;
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

                // Automatically track apps not in any list as Neutral so they appear immediately.
                if (!_neutralApps.Contains(processName)
                    && !_rotApps.Contains(processName)
                    && !_focusApps.Contains(processName)
                    && _perAppSeconds[processName] >= AutoAddThresholdSeconds)
                {
                    _neutralApps.Add(processName);
                    _usageRepository.SaveCategory(processName, UsageCategory.Neutral);
                    addedToCategories = true;
                }
            }

            _usageRepository.AppendUsage(new UsageEntry
            {
                Timestamp = DateTime.Now,
                AppName = processName,
                Category = category,
                DurationSeconds = 1
            });

            // Trigger immediate UI refresh if we just surfaced this app in the category lists.
            return addedToCategories;
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

        public IEnumerable<UsageAggregate> GetAggregates(int daysBack)
        {
            var end = DateTime.Today.AddDays(1);
            var start = end.AddDays(-daysBack);
            return _usageRepository.GetAggregates(start, end);
        }

        public IEnumerable<UsageTimelineBin> GetTimelineBins(DateTime startInclusive, DateTime endExclusive, TimeSpan binSize)
        {
            return _usageRepository.GetTimelineBins(startInclusive, endExclusive, binSize);
        }

        public void ClearAllData()
        {
            _usageRepository.DeleteAllData();

            _perAppSeconds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _rotSeconds = 0;
            _focusSeconds = 0;
            _neutralSeconds = 0;
            _currentDate = DateOnly.FromDateTime(DateTime.Now);

            // Persist current category lists back into the cleared store
            PersistCategories();
        }

        private void PersistCategories()
        {
            foreach (var app in _rotApps)
            {
                _usageRepository.SaveCategory(app, UsageCategory.Rot);
            }
            foreach (var app in _focusApps)
            {
                _usageRepository.SaveCategory(app, UsageCategory.Focus);
            }
            foreach (var app in _neutralApps)
            {
                _usageRepository.SaveCategory(app, UsageCategory.Neutral);
            }
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

        public UsageCategory GetAppCategory(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName))
            {
                return UsageCategory.Neutral;
            }

            if (_rotApps.Contains(appName))
            {
                return UsageCategory.Rot;
            }

            if (_focusApps.Contains(appName))
            {
                return UsageCategory.Focus;
            }

            if (_neutralApps.Contains(appName))
            {
                return UsageCategory.Neutral;
            }

            return UsageCategory.Neutral;
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
                    _usageRepository.SaveCategory(normalized, UsageCategory.Rot);
                    break;
                case UsageCategory.Focus:
                    _focusApps.Add(normalized);
                    _usageRepository.SaveCategory(normalized, UsageCategory.Focus);
                    break;
                case UsageCategory.Neutral:
                    _neutralApps.Add(normalized);
                    _usageRepository.SaveCategory(normalized, UsageCategory.Neutral);
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
