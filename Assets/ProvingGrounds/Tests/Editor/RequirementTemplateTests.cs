using System.IO;
using System.Reflection;
using FarEmerald.PlayForge.Extended;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>A Requirement Template's own fields, as the user's notes of 2026-11-11 left them (ED-79).</summary>
    public class RequirementTemplateTests
    {
        /// <summary>
        /// A Requirement Template's category was a string, typed anew for each template (the user's note, ED-79). It's a
        /// tag, in a context of its own, Requirement Category, so its picker offers the categories templates use.
        /// </summary>
        [Test]
        public void ED_79_A_templates_category_is_a_tag_of_its_own_context()
        {
            var field = typeof(RequirementTemplate).GetField(nameof(RequirementTemplate.Category));
            Assert.AreEqual(typeof(Tag), field?.FieldType, "the category is a tag");
            Assert.That(field?.GetCustomAttribute<ForgeTagContext>()?.Context, Is.EqualTo(new[] { "Requirement Category" }),
                "in a context of its own");
        }
    }
}
