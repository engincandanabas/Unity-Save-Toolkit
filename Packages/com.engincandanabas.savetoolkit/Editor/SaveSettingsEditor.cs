
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SaveToolkit.Editor
{
    [CustomEditor(typeof(SaveSettings))]
    internal sealed class SaveSettingsEditor : UnityEditor.Editor
    {
        private static readonly SavePlatform[] Platforms =
        {
            SavePlatform.Editor,
            SavePlatform.Mobile,
            SavePlatform.Desktop,
            SavePlatform.Other
        };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var settings = (SaveSettings)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resolved Profiles", EditorStyles.boldLabel);

            foreach (SavePlatform platform in Platforms)
            {
                SaveProfile profile = settings.ResolveProfile(platform);
                string label = profile != null ? profile.name : "<none>";
                EditorGUILayout.LabelField(platform.ToString(), label);

                if (profile != null && !profile.Validate(out string error))
                    EditorGUILayout.HelpBox($"{profile.name}: {error}", MessageType.Error);
            }

            foreach (SaveSettings.StoreProfile store in settings.StoreProfiles)
            {
                if (store == null || string.IsNullOrWhiteSpace(store.Define))
                    continue;

                SaveProfile profile = settings.ResolveProfile(SavePlatform.Desktop, store.Define);
                EditorGUILayout.LabelField($"Desktop + {store.Define}", profile != null ? profile.name : "<none>");
            }

            string activeStore = settings.GetActiveStoreDefine();
            if (activeStore != null)
                EditorGUILayout.HelpBox(
                    $"Active build target defines {activeStore}: players built now use " +
                    $"'{settings.GetStoreProfile(activeStore).name}'.",
                    MessageType.Info
                );

            if (settings.OverrideProfile != null)
                EditorGUILayout.HelpBox(
                    $"OverrideProfile '{settings.OverrideProfile.name}' is used on every " +
                    "platform. Clear it before shipping.",
                    MessageType.Warning
                );

            SaveProfile editorProfile = settings.ResolveProfile(SavePlatform.Editor);
            if (editorProfile != null && editorProfile.Storage == SaveStorageType.File)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(
                    "Editor Save Path",
                    Path.Combine(Application.persistentDataPath, editorProfile.SaveDirectory),
                    EditorStyles.wordWrappedMiniLabel
                );
            }

            if (!AssetDatabase.GetAssetPath(settings).Contains("/Resources/"))
                EditorGUILayout.HelpBox(
                    "Place this asset in a Resources folder (Resources/SaveSettings) " +
                    "so a SaveController created at runtime can find it.",
                    MessageType.Info
                );
        }
    }

    [CustomEditor(typeof(SaveProfile))]
    internal sealed class SaveProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var profile = (SaveProfile)target;

            if (!profile.Validate(out string error))
                EditorGUILayout.HelpBox(error, MessageType.Error);

            if (profile.EnableEncryption)
                EditorGUILayout.HelpBox(
                    "Changing the passphrase or salt makes existing encrypted saves " +
                    "unreadable. The passphrase ships inside the build: this stops " +
                    "casual edits, not a determined attacker.",
                    MessageType.Info
                );

            if (profile.Format == SaveSerializationFormat.MessagePack)
            {
                if (!SaveSerializerRegistry.IsAvailable(SaveSerializationFormat.MessagePack))
                    EditorGUILayout.HelpBox(
                        "MessagePack is not installed, so this profile cannot be used. " +
                        "Install MessagePack (e.g. via NuGetForUnity) or switch to Json.",
                        MessageType.Error
                    );

                EditorGUILayout.HelpBox(
                    "IL2CPP builds (iOS/Android) need [MessagePackObject] on every " +
                    "saved type.",
                    MessageType.Info
                );
            }

            if (!profile.FlushToDisk)
                EditorGUILayout.HelpBox(
                    "FlushToDisk is off: saves are faster but a power loss or OS crash " +
                    "right after saving can lose the latest write (backups still apply).",
                    MessageType.Info
                );

            if (profile.Storage == SaveStorageType.PlayerPrefs)
                EditorGUILayout.HelpBox(
                    "PlayerPrefs has size limits (WebGL ~1 MB) and runs on the main " +
                    "thread; async saves run synchronously.",
                    MessageType.Info
                );
        }
    }
}
