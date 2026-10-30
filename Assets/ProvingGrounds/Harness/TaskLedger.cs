using System.Collections.Generic;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>
    /// Records what probe tasks go through, for the rule that every Prepare has exactly one Clean and
    /// that nothing is cleaned while it runs.
    /// </summary>
    public sealed class TaskLedger
    {
        private sealed class Entry
        {
            public bool Prepared;
            public int Running;
            public int Prepares;
            public int Cleans;
        }

        private readonly Dictionary<string, Entry> _entries = new();
        private readonly List<string> _violations = new();

        private Entry Get(string task)
        {
            if (!_entries.TryGetValue(task, out var entry)) _entries[task] = entry = new Entry();
            return entry;
        }

        public int Prepares(string task) => Get(task).Prepares;
        public int Cleans(string task) => Get(task).Cleans;

        public void Prepared(string task)
        {
            var entry = Get(task);
            if (entry.Prepared) _violations.Add($"{task} was prepared again before it was cleaned");
            entry.Prepared = true;
            entry.Prepares++;
        }

        public void Started(string task) => Get(task).Running++;

        public void Finished(string task) => Get(task).Running--;

        public void Cleaned(string task)
        {
            var entry = Get(task);
            if (!entry.Prepared) _violations.Add($"{task} was cleaned without a Prepare");
            if (entry.Running > 0) _violations.Add($"{task} was cleaned while it ran");
            entry.Prepared = false;
            entry.Cleans++;
        }

        /// <summary>Everything broken so far, including tasks still waiting for their Clean.</summary>
        public List<string> Violations()
        {
            var violations = new List<string>(_violations);
            foreach (var (task, entry) in _entries)
            {
                if (entry.Prepared) violations.Add($"{task} was prepared and never cleaned");
            }
            return violations;
        }
    }
}
