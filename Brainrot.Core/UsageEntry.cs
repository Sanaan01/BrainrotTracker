using System;

namespace Brainrot.Core
{
    public sealed class UsageEntry
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string AppName { get; set; } = string.Empty;
        public UsageCategory Category { get; set; }
        public int DurationSeconds { get; set; }
    }
}
