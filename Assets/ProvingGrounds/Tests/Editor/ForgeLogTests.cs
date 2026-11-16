using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// ForgeLog as all the runtime's logging goes through it (EP-27, the user's answer of 2026-11-16): an exception keeps
    /// its stack in the console and is heard as an error of its system, and a level of None silences it.
    /// </summary>
    public class ForgeLogTests
    {
        private readonly List<ForgeLogEntry> heard = new();
        private ForgeLog.ELevel? level;

        [SetUp]
        public void Listen()
        {
            heard.Clear();
            level = ForgeLog.HasOwnLevel(ForgeLog.ESystem.Sequences) ? ForgeLog.LevelOf(ForgeLog.ESystem.Sequences) : null;
            ForgeLog.Logged += Hear;
        }

        [TearDown]
        public void StopListening()
        {
            ForgeLog.Logged -= Hear;
            ForgeLog.SetLevel(ForgeLog.ESystem.Sequences, level);
        }

        private void Hear(ForgeLogEntry entry) => heard.Add(entry);

        /// <summary>
        /// The runtime logged an exception with Unity's LogException in a dozen places, ProcessControl's among them, which no
        /// level held back and no listener heard (EP-27). ForgeLog.Exception logs it as Unity's own does, its stack in the
        /// console, and is heard as an error of its system; a level of None silences it.
        /// </summary>
        [Test]
        public void EP_27_An_exception_logs_through_ForgeLog()
        {
            ForgeLog.SetLevel(ForgeLog.ESystem.Sequences, ForgeLog.ELevel.Info);
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: thrown on purpose"));
            ForgeLog.Exception(ForgeLog.ESystem.Sequences, new InvalidOperationException("thrown on purpose"));
            Assert.That(heard, Has.Exactly(1).Matches<ForgeLogEntry>(entry => entry.System == ForgeLog.ESystem.Sequences &&
                entry.Level == ForgeLog.ELevel.Error && entry.Message.Contains("thrown on purpose")), "heard as an error of its system");

            heard.Clear();
            ForgeLog.SetLevel(ForgeLog.ESystem.Sequences, ForgeLog.ELevel.None);
            ForgeLog.Exception(ForgeLog.ESystem.Sequences, new InvalidOperationException("not heard"));
            LogAssert.NoUnexpectedReceived();
            Assert.That(heard, Is.Empty, "a level of None silences it");
        }
    }
}
