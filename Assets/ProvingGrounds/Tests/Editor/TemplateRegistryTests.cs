using System.Linq;
using FarEmerald.PlayForge.Extended.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FarEmerald.PlayForge.ProvingGrounds.EditorTests
{
    /// <summary>
    /// The template registry lives in EditorPrefs, which every project on the machine shares. Each test puts back the
    /// entries it touches.
    /// </summary>
    public class TemplateRegistryTests
    {
        private const string SharedKey = "PlayForge_TemplateRegistry";
        private static string ProjectKey => SharedKey + "_" + PlayerSettings.productGUID.ToString("N");

        // A template whose asset is in another project, so no GUID here resolves it
        private const string ForeignGuid = "0123456789abcdef0123456789abcdef";

        private bool _hadShared, _hadProject;
        private string _shared, _project;

        [SetUp]
        public void KeepPrefs()
        {
            _hadShared = EditorPrefs.HasKey(SharedKey);
            _shared = EditorPrefs.GetString(SharedKey, "");
            _hadProject = EditorPrefs.HasKey(ProjectKey);
            _project = EditorPrefs.GetString(ProjectKey, "");
        }

        [TearDown]
        public void RestorePrefs()
        {
            if (_hadShared) EditorPrefs.SetString(SharedKey, _shared); else EditorPrefs.DeleteKey(SharedKey);
            if (_hadProject) EditorPrefs.SetString(ProjectKey, _project); else EditorPrefs.DeleteKey(ProjectKey);
            TemplateRegistry.Load();
        }

        /// <summary>
        /// One EditorPrefs entry served every project, and Save dropped each template whose asset this project lacks:
        /// opening a second project and saving there wiped the first project's templates.
        /// </summary>
        [Test]
        public void ED_22_Saving_keeps_another_projects_templates()
        {
            var localGuid = AssetDatabase.AssetPathToGUID("Assets/ProvingGrounds");
            var data = new TemplateRegistryData();
            data.Collections.Add(new AssetTypeTemplateCollection
            {
                TypeName = nameof(Ability),
                Templates =
                {
                    new AssetTemplate { AssetGUID = ForeignGuid, DisplayName = "Elsewhere" },
                    new AssetTemplate { AssetGUID = localGuid, DisplayName = "Here" },
                }
            });
            EditorPrefs.SetString(SharedKey, JsonUtility.ToJson(data));
            EditorPrefs.DeleteKey(ProjectKey);

            TemplateRegistry.Load();
            TemplateRegistry.Save();

            var shared = JsonUtility.FromJson<TemplateRegistryData>(EditorPrefs.GetString(SharedKey, ""));
            Assert.That(shared.Collections.SelectMany(c => c.Templates).Select(t => t.AssetGUID), Does.Contain(ForeignGuid),
                "the other project still finds its template");

            TemplateRegistry.Load();
            Assert.That(TemplateRegistry.Data.Collections.SelectMany(c => c.Templates).Select(t => t.AssetGUID),
                Does.Contain(localGuid), "this project keeps its own");
        }
    }
}
