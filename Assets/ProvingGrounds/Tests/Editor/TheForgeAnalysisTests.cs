using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>The Forge's Analysis tab, built as clicking its tab builds it.</summary>
    public class TheForgeAnalysisTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>The Items and Entities groups only ever showed a "Not Implemented" placeholder.</summary>
        [Test]
        public void ED_10_Analysis_offers_only_the_groups_it_can_analyse()
        {
            var forge = ScriptableObject.CreateInstance<TheForge>();
            try
            {
                var content = new VisualElement();
                typeof(TheForge).GetField("contentContainer", Private).SetValue(forge, content);
                typeof(TheForge).GetMethod("BuildAnalysisTab", Private).Invoke(forge, null);

                var buttons = content.Query<Button>().ToList().Select(button => button.text).ToList();
                Assert.That(buttons, Has.Some.EndsWith("Effects"), "the group bar is built");
                Assert.That(buttons, Has.None.EndsWith("Items"));
                Assert.That(buttons, Has.None.EndsWith("Entities"));
            }
            finally
            {
                Object.DestroyImmediate(forge);
            }
        }

        /// <summary>The distribution view listed the first ten warnings and dropped the rest without a word.</summary>
        [Test]
        public void ED_10_Warnings_past_ten_are_counted()
        {
            var forge = ScriptableObject.CreateInstance<TheForge>();
            var assets = new List<ScalerAsset>();
            try
            {
                var analysisType = typeof(TheForge).GetNestedType("PowerAnalysis", BindingFlags.NonPublic);
                var warningType = typeof(TheForge).GetNestedType("EBalanceWarning", BindingFlags.NonPublic);
                var analyses = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(analysisType));
                for (int i = 0; i < 12; i++)
                {
                    var asset = ScriptableObject.CreateInstance<ScalerAsset>();
                    asset.name = "ED-10 " + i;
                    assets.Add(asset);

                    var analysis = Activator.CreateInstance(analysisType);
                    analysisType.GetField("Asset").SetValue(analysis, asset);
                    analysisType.GetField("TotalPowerScore").SetValue(analysis, (float)i);
                    analysisType.GetField("Warning").SetValue(analysis, Enum.Parse(warningType, "HighComplexity"));
                    analysisType.GetField("WarningMessage").SetValue(analysis, "ED-10");
                    analyses.Add(analysis);
                }

                var view = new VisualElement();
                typeof(TheForge).GetMethod("BuildDistributionView", Private).Invoke(forge, new object[] { view, analyses });

                Assert.That(view.Query<Label>().ToList().Select(label => label.text), Has.Some.Contains("2 more"));
            }
            finally
            {
                Object.DestroyImmediate(forge);
                foreach (var asset in assets) Object.DestroyImmediate(asset);
            }
        }
    }
}
