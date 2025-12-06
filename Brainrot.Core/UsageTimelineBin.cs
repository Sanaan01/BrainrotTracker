using System;

namespace Brainrot.Core
{
    public sealed class UsageTimelineBin
    {
        public UsageTimelineBin(DateTime start)
        {
            Start = start;
        }

        public DateTime Start { get; }
        public int FocusSeconds { get; set; }
        public int RotSeconds { get; set; }
        public int NeutralSeconds { get; set; }
    }
}
