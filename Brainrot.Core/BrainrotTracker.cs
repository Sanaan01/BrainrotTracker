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

        private readonly Dictionary<string, int> _perAppSeconds = new();
        private int _rotSeconds;
        private int _focusSeconds;
        private int _neutralSeconds;
        
        private readonly string _selfProcessName = Process.GetCurrentProcess().ProcessName;

        public BrainrotTracker()
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
        }

        /// <summary>
        /// Call this once per second.
        /// </summary>
        public void Tick()
        {
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

            if (_rotApps.Contains(processName))
                _rotSeconds++;
            else if (_focusApps.Contains(processName))
                _focusSeconds++;
            else
                _neutralSeconds++;
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

        public IEnumerable<KeyValuePair<string, int>> GetTopApps(int count = 10)
        {
            return _perAppSeconds
                .OrderByDescending(kvp => kvp.Value)
                .Take(count);
        }
    }
}
