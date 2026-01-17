using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    public class LaunchHistoryEntry
    {
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string VersionId { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public int? ExitCode { get; set; }
        public bool? Success { get; set; }
    }

    public class HomeLaunchHistoryService
    {
        private static HomeLaunchHistoryService? _instance;
        public static HomeLaunchHistoryService Instance => _instance ??= new HomeLaunchHistoryService();

        private readonly string _historyFilePath;
        private readonly List<LaunchHistoryEntry> _entries = new();
        private LaunchHistoryEntry? _currentEntry;
        private const int MaxEntries = 20;

        private HomeLaunchHistoryService()
        {
            var configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OMCL", "config");
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            _historyFilePath = Path.Combine(configDir, "launch_history.json");
            Load();
        }

        public IReadOnlyList<LaunchHistoryEntry> GetRecentEntries(int count = 5)
        {
            return _entries
                .OrderByDescending(e => e.StartTime)
                .Take(count)
                .ToList();
        }

        public void RecordLaunchStart(string versionId, string accountName)
        {
            _currentEntry = new LaunchHistoryEntry
            {
                StartTime = DateTime.Now,
                VersionId = versionId,
                AccountName = accountName
            };

            _entries.Insert(0, _currentEntry);

            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }

            Save();
        }

        public void RecordLaunchEnd(int exitCode)
        {
            if (_currentEntry == null)
            {
                // 尝试找到最近一条未结束的记录
                _currentEntry = _entries.FirstOrDefault(e => e.EndTime == null);
                if (_currentEntry == null)
                {
                    return;
                }
            }

            _currentEntry.EndTime = DateTime.Now;
            _currentEntry.ExitCode = exitCode;
            _currentEntry.Success = exitCode == 0;

            Save();
            _currentEntry = null;
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_historyFilePath))
                {
                    return;
                }

                var json = File.ReadAllText(_historyFilePath);
                var loaded = JsonSerializer.Deserialize<List<LaunchHistoryEntry>>(json);
                if (loaded != null)
                {
                    _entries.Clear();
                    _entries.AddRange(loaded);
                }
            }
            catch
            {
                // ignore
            }
        }

        private void Save()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                File.WriteAllText(_historyFilePath, JsonSerializer.Serialize(_entries, options));
            }
            catch
            {
                // ignore
            }
        }
    }
}

