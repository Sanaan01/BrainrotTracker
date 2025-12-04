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
        IReadOnlyDictionary<string, UsageCategory> LoadCategories();
        void SaveCategory(string appName, UsageCategory category);
    }

    public sealed class LiteDbUsageRepository : IUsageRepository, IDisposable
    {
        private const string UsageCollectionName = "usage_entries";
        private const string CategoryCollectionName = "app_categories";
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<UsageEntry> _usageCollection;
        private readonly ILiteCollection<AppCategoryRecord> _categoryCollection;
        private bool _disposed;

        public LiteDbUsageRepository(string? databasePath = null)
        {
            var dbPath = databasePath ?? GetDefaultDatabasePath();
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            _database = new LiteDatabase(dbPath);
            _usageCollection = _database.GetCollection<UsageEntry>(UsageCollectionName);
            _usageCollection.EnsureIndex(x => x.Timestamp);
            _usageCollection.EnsureIndex(x => x.AppName);
            _usageCollection.EnsureIndex(x => x.Category);

            _categoryCollection = _database.GetCollection<AppCategoryRecord>(CategoryCollectionName);
            _categoryCollection.EnsureIndex(x => x.AppName, unique: true);
        }

        public void AppendUsage(UsageEntry entry)
        {
            _usageCollection.Insert(entry);
        }

        public BrainUsageSnapshot GetSnapshotForDate(DateOnly date)
        {
            var start = date.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);

            var perApp = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int rot = 0, focus = 0, neutral = 0;

            var entries = _usageCollection.Query()
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

        public IReadOnlyDictionary<string, UsageCategory> LoadCategories()
        {
            var dict = new Dictionary<string, UsageCategory>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in _categoryCollection.FindAll())
            {
                if (string.IsNullOrWhiteSpace(record.AppName))
                {
                    continue;
                }

                dict[record.AppName] = record.Category;
            }

            return dict;
        }

        public void SaveCategory(string appName, UsageCategory category)
        {
            if (string.IsNullOrWhiteSpace(appName))
            {
                return;
            }

            var normalized = appName.Trim();
            var record = new AppCategoryRecord
            {
                AppName = normalized,
                Category = category
            };

            _categoryCollection.Upsert(record);
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

    internal sealed class AppCategoryRecord
    {
        public string AppName { get; set; } = string.Empty;
        public UsageCategory Category { get; set; }
    }
}
