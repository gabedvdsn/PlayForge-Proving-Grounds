using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// Local Data's numbers can scale (EP-16, the user's change of 2026-11-16): an entry's Int is a ScalableInt and its Float
    /// a ScalableFloat, read where they're read, in the reader's context. A wrapper made in code holds 0, not null; a number
    /// that reads its own key through a Local Data kind stops, at the kind's Default, where it overflowed the stack; and the
    /// Demo's numbers, saved as plain values before, hold them in the new form.
    /// </summary>
    public class LocalDataNumbersTests
    {
        private static readonly Tag Key = Tag.Generate("EP16.Reach");

        // A Local Data source of the test's own, holding the entries given
        private sealed class Source : ILocalDataSource
        {
            private readonly List<DataWrapper> entries;
            public Source(params DataWrapper[] entries) => this.entries = entries.ToList();
            public List<DataWrapper> GetLocalData() => entries;
        }

        // A context at a level, reading its own Local Data from a source
        private static NumberContext At(int level, ILocalDataSource source) =>
            new(null, null, new IntValuePairClamped(level, 1, 10), true, localData: source);

        /// <summary>A wrapper made in code holds a float and a whole number, each 0 to start, where it held null.</summary>
        [Test]
        public void EP_16_A_wrapper_made_in_code_holds_numbers()
        {
            var data = new DataWrapper { Key = Key, Type = EDataWrapperType.Float };
            Assert.IsNotNull(data.floatValue, "a float, 0 to start");
            Assert.IsNotNull(data.intValue, "and a whole number");
            Assert.IsTrue(At(1, new Source(data)).TryGetLocalData(Key, out float value), "it reads");
            Assert.AreEqual(0f, value, "as 0");
        }

        /// <summary>
        /// A Local Data number reads in the reader's context: a constant as it's given, a whole number as one, and one that
        /// scales by level at the context's level.
        /// </summary>
        [Test]
        public void EP_16_A_Local_Data_number_reads_in_its_readers_context()
        {
            var constant = new DataWrapper { Key = Key, Type = EDataWrapperType.Float, floatValue = 7.5f };
            Assert.IsTrue(At(1, new Source(constant)).TryGetLocalData(Key, out float value) && value == 7.5f, $"a constant as given: {value}");

            var whole = new DataWrapper { Key = Key, Type = EDataWrapperType.Int, intValue = 3 };
            Assert.IsTrue(At(1, new Source(whole)).TryGetLocalData(Key, out value) && value == 3f, $"a whole number as one: {value}");

            var scaling = new DataWrapper { Key = Key, Type = EDataWrapperType.Float, floatValue = ScalableFloat.From(context => context.Level.CurrentValue * 2f) };
            Assert.IsTrue(At(4, new Source(scaling)).TryGetLocalData(Key, out value), "one that scales reads");
            Assert.AreEqual(8f, value, "at the context's level");
        }

        /// <summary>
        /// A number that reads its own key through a Local Data kind stops, reading the kind's Default, where it read itself
        /// until the stack overflowed.
        /// </summary>
        [Test]
        public void EP_16_A_Local_Data_number_that_reads_itself_stops()
        {
            var data = new DataWrapper { Key = Key, Type = EDataWrapperType.Float };
            data.floatValue = new ScalableFloat(new LocalDataKind { Key = Key, Default = 4f }, 1f, EMagnitudeOperation.UseScaler);
            Assert.IsTrue(At(1, new Source(data)).TryGetLocalData(Key, out float value), "it reads, as far as it goes");
            Assert.AreEqual(4f, value, "and stops at its kind's Default");
        }

        /// <summary>
        /// The Demo's Local Data numbers, saved as plain values before EP-16, hold them in the new form: the Volley's reach 9,
        /// its radius 3.5 and its 5 arrows. Unconverted, they read 0.
        /// </summary>
        [Test]
        public void EP_16_The_Demos_Local_Data_numbers_hold_their_values()
        {
            var roster = AssetDatabase.FindAssets("Demo Roster").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => path.EndsWith("/Demo Roster.asset"));
            Assume.That(roster, Is.Not.Null, "the Demo is imported");
            var folder = roster.Substring(0, roster.LastIndexOf('/'));
            var volley = AssetDatabase.LoadAssetAtPath<Ability>($"{folder}/Abilities/Ranger/Volley.asset");
            Assume.That(volley, Is.Not.Null, "the Demo's Volley");

            DataWrapper Entry(string key) => volley.LocalData.FirstOrDefault(entry => entry != null && entry.Key.Name == key);
            Assert.AreEqual(9f, Entry("Demo.Reach")?.floatValue?.Magnitude, "its reach");
            Assert.AreEqual(3.5f, Entry("Demo.Volley.Radius")?.floatValue?.Magnitude, "its radius");
            Assert.AreEqual(5, Entry("Demo.Volley.Arrows")?.intValue?.Magnitude, "and its arrows");
            Assert.That(Entry("Demo.Volley.Radius")?.floatValue?.IsConstant, Is.True, "each a constant, as a value given in code is");
        }
    }
}
