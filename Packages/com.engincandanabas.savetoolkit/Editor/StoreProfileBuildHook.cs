
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SaveToolkit.Editor
{
    /// <summary>
    /// Scripting defines exist only at compile time, so right before a player
    /// build this writes the matching store define into SaveSettings; the
    /// player then resolves the store profile from it. Cleared after the build.
    /// </summary>
    internal sealed class StoreProfileBuildHook : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            SaveSettings settings = SaveToolkitMenu.FindSettings();
            if (settings == null)
                return;

            NamedBuildTarget target = NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup);
            string define = settings.FindStoreDefine(PlayerSettings.GetScriptingDefineSymbols(target));

            if (settings.BakedStoreDefine == define)
                return;

            settings.BakedStoreDefine = define;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);

            if (define != null)
                Debug.Log($"[SaveToolkit] Store profile for this build: {define} → " +
                          $"{settings.GetStoreProfile(define).name}");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            SaveSettings settings = SaveToolkitMenu.FindSettings();
            if (settings == null || string.IsNullOrEmpty(settings.BakedStoreDefine))
                return;

            settings.BakedStoreDefine = null;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }
    }
}
