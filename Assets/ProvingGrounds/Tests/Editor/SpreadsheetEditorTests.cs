using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// TW-27, Decision 67: numbers by level and item stats to and from CSV. The Forge writes a sheet of the project's
    /// numbers, or its items' stats, and reads one back through a window that lists each change, applied as one Undo step.
    /// A row names its asset both ways: its asset tag first, its name where it gives no tag, a name two assets share
    /// reported.
    /// </summary>
    public class SpreadsheetEditorTests
    {
        private const string Folder = "Assets/TW27 Sheets";
        private const string Magnitude = "Impact Specification › Magnitude";
        private const string NumbersHeader = "Asset Tag,Name,Field,Kind,Level 1,Level 2,Level 3";

        [SetUp]
        public void MakeFolder()
        {
            Assume.That(AssetDatabase.IsValidFolder(Folder), Is.False, $"{Folder} is free");
            AssetDatabase.CreateFolder("Assets", "TW27 Sheets");
        }

        [TearDown]
        public void Clean() => AssetDatabase.DeleteAsset(Folder);

        private static T Saved<T>(T asset, string file) where T : ScriptableObject
        {
            asset.name = file;
            AssetDatabase.CreateAsset(asset, $"{Folder}/{file}.asset");
            return asset;
        }

        private static Attribute Stat(string name)
        {
            var attribute = ScriptableObject.CreateInstance<Attribute>();
            attribute.Name = name;
            return Saved(attribute, name);
        }

        private static EffectTagRequirements NoRequirements() => new()
        {
            ApplicationRequirements = new AvoidRequireTagGroup(),
            OngoingRequirements = new AvoidRequireTagGroup(),
            RemovalRequirements = new AvoidRequireTagGroup(),
        };

        // An effect, saved: one impact that adds its number to an attribute, its name and its asset tag given
        private static GameplayEffect Effect(string name, Attribute target, ScalableFloat magnitude, string tag = null, string file = null,
            EEffectDurationPolicy policy = EEffectDurationPolicy.Instant)
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffect>();
            effect.Definition = new GameplayEffectDefinition { Name = name, Textures = new List<TextureItem>() };
            effect.Tags = new GameplayEffectTags
            {
                AssetTag = Tag.Generate(tag ?? $"Effect.{name.Replace(" ", "").Replace(",", "")}"), ContextTags = new List<Tag>(), GrantedTags = new List<Tag>(),
            };
            effect.Workers = new List<AbstractEffectWorker>();
            effect.SourceRequirements = NoRequirements();
            effect.TargetRequirements = NoRequirements();
            effect.ImpactSpecification = new GameplayEffectImpact
            {
                AttributeTarget = target,
                ImpactOperation = ECalculationOperation.Add,
                AffiliationPolicy = EAffiliationPolicy.AlwaysAllow,
                ImpactTypes = new List<Tag>(),
                MagnitudeOperation = magnitude,
                Packets = System.Array.Empty<ContainedEffectPacket>(),
            };
            effect.DurationSpecification = new GameplayEffectDuration
            {
                DurationPolicy = policy,
                DurationOperation = new ScalableFloat(0f),
                TicksOperation = new ScalableInt { Magnitude = 0 },
                TickIntervalOperation = new ScalableFloat(1f),
                StackingConfigs = new List<AbstractStackingConfig>(),
            };
            return Saved(effect, file ?? name.Replace(",", ""));
        }

        private static Item Item(string name, params GameplayEffect[] granted)
        {
            var item = ScriptableObject.CreateInstance<Item>();
            item.Definition.Name = name;
            item.Tags.AssetTag = Tag.Generate($"Item.{name.Replace(" ", "")}");
            item.GrantedEffects.AddRange(granted);
            return Saved(item, name);
        }

        // A Table of these values, read times a number
        private static ScalableFloat Table(float times, params float[] values) =>
            new(new SimpleScaler { StretchOverLevels = false, LevelValues = values }, times,
                Mathf.Approximately(times, 1f) ? EMagnitudeOperation.UseScaler : EMagnitudeOperation.MultiplyWithScaler);

        private static float[] ValuesOf(GameplayEffect effect) => ((SimpleScaler)effect.ImpactSpecification.MagnitudeOperation.Scaler).LevelValues;

        private static string Csv(params string[] rows) => string.Join("\n", rows) + "\n";

        private static List<string> Rows(string csv) => csv.Replace("\r", "").Split('\n').Where(row => row.Length > 0).ToList();

        /// <summary>A number by level is a row of what it reads at each level: its table times its number, as Laguna Blade's -450/-600/-750.</summary>
        [Test]
        public void TW_27_Numbers_by_level_export_as_rows_of_what_they_read()
        {
            var bolt = Effect("TW27 Bolt", Stat("TW27 Health"), Table(-1f, 450f, 600f, 750f));
            var rows = Rows(ForgeSpreadsheets.NumbersOf(new Object[] { bolt }));

            Assert.AreEqual(NumbersHeader, rows[0], "a column for each level");
            Assert.That(rows, Has.Member($"Effect.TW27Bolt,TW27 Bolt,{Magnitude},Table,-450,-600,-750"), "what it reads at each level");
            Assert.AreEqual(2, rows.Count, "and no row for its constant numbers");
        }

        /// <summary>
        /// A sheet read back sets each number's table to what the row says it reads, divided by its number, and the whole
        /// sheet goes back as one Undo step; a sheet read back as it was written changes nothing.
        /// </summary>
        [Test]
        public void TW_27_A_sheet_of_numbers_by_level_imports_as_one_undo_step()
        {
            var bolt = Effect("TW27 Bolt", Stat("TW27 Health"), Table(-1f, 450f, 600f, 750f));
            var unchanged = ForgeSpreadsheets.Read(ForgeSpreadsheets.NumbersOf(new Object[] { bolt }));
            Assert.That(unchanged.Problems, Is.Empty, "a sheet as written reads back");
            Assert.That(unchanged.Changes, Is.Empty, "and changes nothing");

            var import = ForgeSpreadsheets.Read(Csv(NumbersHeader + ",Level 4", $"Effect.TW27Bolt,TW27 Bolt,{Magnitude},Table,-500,-650,-800,-950"));
            Assert.That(import.Problems, Is.Empty, string.Join(" | ", import.Problems));
            Assert.AreEqual(1, import.Changes.Count, "one number changes");
            Assert.AreSame(bolt, import.Changes[0].Asset);
            import.Apply();
            Assert.That(ValuesOf(bolt), Is.EqualTo(new[] { 500f, 650f, 800f, 950f }), "its table reads the row, times its number");

            Undo.PerformUndo();
            Assert.That(ValuesOf(bolt), Is.EqualTo(new[] { 450f, 600f, 750f }), "one Undo step takes it back");
        }

        /// <summary>
        /// A row finds its asset by its asset tag, or by its name where it gives none; a name two assets share, an asset
        /// that isn't there and a field it hasn't got are reported, not guessed.
        /// </summary>
        [Test]
        public void TW_27_Rows_find_their_assets_by_tag_then_by_name()
        {
            var health = Stat("TW27 Health");
            var bolt = Effect("TW27 Bolt", health, Table(1f, 10f, 20f));
            var first = Effect("TW27 Twin", health, Table(1f, 1f), tag: "Effect.TW27TwinA", file: "TW27 Twin A");
            Effect("TW27 Twin", health, Table(1f, 2f), tag: "Effect.TW27TwinB", file: "TW27 Twin B");

            var import = ForgeSpreadsheets.Read(Csv(NumbersHeader,
                $",TW27 Bolt,{Magnitude},Table,11,21",
                $"Effect.TW27TwinA,TW27 Twin,{Magnitude},Table,5",
                $",TW27 Twin,{Magnitude},Table,6",
                $"Effect.TW27Nothing,Nothing,{Magnitude},Table,1",
                "Effect.TW27Bolt,TW27 Bolt,Duration › Nonsense,Table,1"));

            Assert.That(import.Changes.Select(change => change.Asset), Is.EquivalentTo(new Object[] { bolt, first }),
                "the bolt by its name, the first twin by its tag");
            Assert.AreEqual(3, import.Problems.Count, string.Join(" | ", import.Problems));
            Assert.That(import.Problems, Has.Some.Contains("TW27 Twin"), "the shared name");
            Assert.That(import.Problems, Has.Some.Contains("Effect.TW27Nothing"), "the asset that isn't there");
            Assert.That(import.Problems, Has.Some.Contains("Nonsense"), "and the field it hasn't got");
        }

        /// <summary>A name with a comma or a quote is quoted, and reads back as it was.</summary>
        [Test]
        public void TW_27_A_sheet_quotes_what_needs_it()
        {
            var bolt = Effect("TW27 Bolt, \"Big\"", Stat("TW27 Health"), Table(1f, 3f, 4f), tag: "Effect.TW27BigBolt", file: "TW27 Big Bolt");
            var sheet = ForgeSpreadsheets.NumbersOf(new Object[] { bolt });
            StringAssert.Contains("\"TW27 Bolt, \"\"Big\"\"\"", sheet, "the name in quotes, its own quotes doubled");

            var import = ForgeSpreadsheets.Read(sheet.Replace(",3,4", ",5,6"));
            Assert.That(import.Problems, Is.Empty, string.Join(" | ", import.Problems));
            Assert.AreEqual(1, import.Changes.Count, "it reads back");
        }

        /// <summary>
        /// Item stats are a row for each item and a column for each attribute its granted effects add to: a number, or one
        /// for each level as 10/15/20. A sheet read back sets them; an item with no effect adding to a column's attribute is
        /// reported.
        /// </summary>
        [Test]
        public void TW_27_Item_stats_export_and_import()
        {
            var armor = Stat("TW27 Armor");
            var health = Stat("TW27 Health");
            var armorBonus = Effect("TW27 Ring Armor", armor, new ScalableFloat(5f), policy: EEffectDurationPolicy.Infinite);
            var healthBonus = Effect("TW27 Ring Health", health, Table(1f, 10f, 15f, 20f), policy: EEffectDurationPolicy.Infinite);
            var ring = Item("TW27 Ring", armorBonus, healthBonus);
            var band = Item("TW27 Band");

            var rows = Rows(ForgeSpreadsheets.StatsOf(new[] { ring, band }));
            Assert.AreEqual("Asset Tag,Name,TW27 Armor,TW27 Health", rows[0], "a column for each attribute");
            Assert.That(rows, Has.Member("Item.TW27Ring,TW27 Ring,5,10/15/20"), "a number, and one for each level");
            Assert.That(rows, Has.Member("Item.TW27Band,TW27 Band,,"), "and an item that adds nothing");

            var import = ForgeSpreadsheets.Read(Csv("Asset Tag,Name,TW27 Armor,TW27 Health",
                "Item.TW27Ring,TW27 Ring,8,12/18/24",
                "Item.TW27Band,TW27 Band,3,"));
            Assert.AreEqual(2, import.Changes.Count, "the ring's two stats");
            Assert.That(import.Problems, Has.Exactly(1).Matches<string>(problem => problem.Contains("TW27 Band") && problem.Contains("TW27 Armor")),
                $"the band has no effect adding Armor: {string.Join(" | ", import.Problems)}");
            import.Apply();
            Assert.AreEqual(8f, armorBonus.ImpactSpecification.MagnitudeOperation.Constant, 0.0001f, "the ring's Armor");
            Assert.That(ValuesOf(healthBonus), Is.EqualTo(new[] { 12f, 18f, 24f }), "and its Health by level");
        }

        /// <summary>The import window lists each change, and Import applies the ones chosen.</summary>
        [UnityTest]
        public IEnumerator TW_27_The_import_window_lists_each_change_and_applies_them()
        {
            var bolt = Effect("TW27 Bolt", Stat("TW27 Health"), Table(1f, 1f, 2f));
            var import = ForgeSpreadsheets.Read(Csv("Asset Tag,Name,Field,Kind,Level 1,Level 2", $"Effect.TW27Bolt,TW27 Bolt,{Magnitude},Table,3,4"));
            LogAssert.ignoreFailingMessages = true;
            ForgeSpreadsheetWindow window = null;
            bool closed = false;
            try
            {
                window = ForgeSpreadsheetWindow.Show(import);
                for (int i = 0; i < 3; i++) yield return null;
                var root = window.rootVisualElement;
                Assert.AreEqual(1, root.Q<MultiColumnListView>("Changes").itemsSource.Count, "the window lists the change");

                var button = root.Q<Button>("Import");
                Assert.IsNotNull(button, "with Import");
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = button;
                    button.SendEvent(submit);
                }
                // Import closes the window
                closed = true;
                yield return null;
                Assert.That(ValuesOf(bolt), Is.EqualTo(new[] { 3f, 4f }), "which applies it");
            }
            finally
            {
                if (!closed && window != null) window.Close();
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
