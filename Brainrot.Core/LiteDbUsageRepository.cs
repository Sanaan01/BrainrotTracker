using System;
using System.Collections.Generic;
using System.IO;
using LiteDB;

namespace Brainrot.Core
{
    public interface IUsageRepository
    {
        void AppendUsage(UsageEntry entry);
        BrainUsageSnapshot GetSnapshotForDate(DateOnly date);
    }

    public sealed class LiteDbUsageRepository : IUsageRepository, IDisposable
    {
        private const string CollectionName = "usage_entries";
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<UsageEntry> _collection;
        private bool _disposed;

        public LiteDbUsageRepository(string? databasePath = null)
        {
            var dbPath = databasePath ?? GetDefaultDatabasePath();
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            _database = new LiteDatabase(dbPath);
            _collection = _database.GetCollection<UsageEntry>(CollectionName);
            _collection.EnsureIndex(x => x.Timestamp);
            _collection.EnsureIndex(x => x.AppName);
            _collection.EnsureIndex(x => x.Category);
        }

        public void AppendUsage(UsageEntry entry)
        {
            _collection.Insert(entry);
        }

        public BrainUsageSnapshot GetSnapshotForDate(DateOnly date)
        {
            var start = date.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);

            var perApp = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int rot = 0, focus = 0, neutral = 0;

            var entries = _collection.Query()
                .Where(x => x.Timestamp >= start && x.Timestamp < end)
                .ToEnumerable();

            foreach (var entry in entries)
            {
                switch (entry.Category)
                {
                    case UsageCategory.Rot:
                        rot += entry.DurationSeconds;
                        break;
                    case UsageCategory.Focus:
                        focus += entry.DurationSeconds;
                        break;
                    case UsageCategory.Neutral:
                        neutral += entry.DurationSeconds;
                        break;
                }

                if (!perApp.TryGetValue(entry.AppName, out var seconds))
                {
                    seconds = 0;
                }

                perApp[entry.AppName] = seconds + entry.DurationSeconds;
            }

            return new BrainUsageSnapshot(rot, focus, neutral, perApp);
        }

        private static string GetDefaultDatabasePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(appData, "BrainrotTracker");
            return Path.Combine(folder, "usage.db");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _database.Dispose();
            _disposed = true;
        }
    }
}
