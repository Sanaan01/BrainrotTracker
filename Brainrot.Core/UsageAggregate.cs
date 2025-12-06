using System;

namespace Brainrot.Core
{
    public sealed class UsageAggregate
    {
        public UsageAggregate(DateOnly date)
        {
            Date = date;
        }

        public DateOnly Date { get; }
        public int RotSeconds { get; set; }
        public int FocusSeconds { get; set; }
        public int NeutralSeconds { get; set; }
        public int TotalSeconds => RotSeconds + FocusSeconds + NeutralSeconds;
    }
}
