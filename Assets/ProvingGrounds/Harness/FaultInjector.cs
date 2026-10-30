using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds
{
    /// <summary>The exception a fault throws. A test can tell it apart from a real failure.</summary>
    public sealed class InjectedFault : Exception
    {
        public readonly string Site;

        public InjectedFault(string site) : base($"Injected fault at {site}")
        {
            Site = site;
        }
    }

    /// <summary>
    /// Makes any hook, task, worker or callback throw on demand. Probes pass through named sites on
    /// every hook, and any other code can call <see cref="Trip"/> itself, a callback subscriber for one:
    /// <c>gas.Callbacks.OnFrameComplete += _ =&gt; Faults.Trip("frame complete")</c>.
    ///
    /// A fault that fires is expected in the log exactly once, so a framework that swallows it fails the
    /// test, and so does one that reports it twice. By default that's as an exception. Arm with
    /// <c>loggedAs: LogType.Error</c> where the framework reports it through LogError, and with
    /// <c>loggedAs: null</c> when the exception should reach a caller instead, which then catches it.
    /// </summary>
    public sealed class FaultInjector
    {
        public const int Always = -1;

        private sealed class Plan
        {
            public int FirstHit;
            public int Remaining;
            public LogType? LoggedAs;
        }

        private readonly Dictionary<string, Plan> _plans = new();
        private readonly Dictionary<string, int> _hits = new();
        private readonly Dictionary<string, int> _fired = new();

        /// <summary>Throws from <paramref name="site"/>'s hit number <paramref name="onHit"/>, <paramref name="times"/> times over.</summary>
        public void Arm(string site, int onHit = 1, int times = 1, LogType? loggedAs = LogType.Exception)
        {
            _plans[site] = new Plan { FirstHit = onHit, Remaining = times, LoggedAs = loggedAs };
        }

        public void Disarm(string site) => _plans.Remove(site);

        public void DisarmAll() => _plans.Clear();

        /// <summary>Times <paramref name="site"/> has been passed through, armed or not.</summary>
        public int Hits(string site) => _hits.TryGetValue(site, out int hits) ? hits : 0;

        /// <summary>Times <paramref name="site"/> has thrown.</summary>
        public int Fired(string site) => _fired.TryGetValue(site, out int fired) ? fired : 0;

        public void Trip(string site)
        {
            _hits[site] = Hits(site) + 1;

            if (!_plans.TryGetValue(site, out var plan) || Hits(site) < plan.FirstHit || plan.Remaining == 0) return;

            if (plan.Remaining != Always) plan.Remaining--;
            _fired[site] = Fired(site) + 1;

            var fault = new InjectedFault(site);
            if (plan.LoggedAs.HasValue) LogAssert.Expect(plan.LoggedAs.Value, new Regex(Regex.Escape(fault.Message)));
            throw fault;
        }
    }
}
