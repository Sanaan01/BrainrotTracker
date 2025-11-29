using System.Collections.Generic;

namespace Brainrot.Core
{
    public sealed class BrainUsageSnapshot
    {
        public int RotSeconds { get; }
        public int FocusSeconds { get; }
        public int NeutralSeconds { get; }

        public IReadOnlyDictionary<string, int> PerAppSeconds { get; }

        public BrainUsageSnapshot(
            int rotSeconds,
            int focusSeconds,
            int neutralSeconds,
            Dictionary<string, int> perAppSeconds)
        {
            RotSeconds = rotSeconds;
            FocusSeconds = focusSeconds;
            NeutralSeconds = neutralSeconds;
            PerAppSeconds = perAppSeconds;
        }
    }
}
