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
        private const string Folder = "Assets/ED79Temp";

        private static string ScriptGuid()
        {
            var made = ScriptableObject.CreateInstance<RequirementTemplate>();
            try
            {
                return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(made)));
            }
            finally
            {
                Object.DestroyImmediate(made);
            }
        }

        // A template as Unity wrote one before its category was a tag
        private static string OldYaml(string guid, string name, string category) =>
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n" +
            "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n" +
            "  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n" +
            $"  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}\n  m_Name: {name}\n  m_EditorClassIdentifier: \n" +
            $"  Description: Saved before\n  Category: {category}\n";

        // The template's category, as the tag's name; null while it isn't a tag
        private static string CategoryName(RequirementTemplate template) =>
            typeof(RequirementTemplate).GetField(nameof(RequirementTemplate.Category))?.GetValue(template) is Tag tag ? tag.Name : null;

        /// <summary>
        /// A Requirement Template's category was a string, typed anew for each template (the user's note, ED-79). It's a
        /// tag, in a context of its own, Requirement Category, so its picker offers the categories templates use. A template
        /// saved with a string loads its category as the tag of that name, and keeps it saved again.
        /// </summary>
        [Test]
        public void ED_79_A_templates_category_is_a_tag_of_its_own_context()
        {
            var field = typeof(RequirementTemplate).GetField(nameof(RequirementTemplate.Category));
            Assert.AreEqual(typeof(Tag), field?.FieldType, "the category is a tag");
            Assert.That(field?.GetCustomAttribute<ForgeTagContext>()?.Context, Is.EqualTo(new[] { "Requirement Category" }),
                "in a context of its own");

            var path = $"{Folder}/Old.asset";
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(path, OldYaml(ScriptGuid(), "Old", "Combat"));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var loaded = AssetDatabase.LoadAssetAtPath<RequirementTemplate>(path);
                Assert.IsNotNull(loaded, "an old template loads");
                Assert.AreEqual("Combat", CategoryName(loaded), "its category, as the tag of its name");
                Assert.AreEqual("Saved before", loaded.Description, "with the rest as it was");

                EditorUtility.SetDirty(loaded);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Assert.AreEqual("Combat", CategoryName(AssetDatabase.LoadAssetAtPath<RequirementTemplate>(path)), "saved again, it loads the same");
            }
            finally
            {
                AssetDatabase.DeleteAsset(Folder);
            }
        }
    }
}
