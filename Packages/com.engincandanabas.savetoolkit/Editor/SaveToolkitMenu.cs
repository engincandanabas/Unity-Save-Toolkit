
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SaveToolkit.Editor
{
    internal static class SaveToolkitMenu
    {
        private const string MenuRoot = "Tools/Save Toolkit/";
        private const string DefaultSettingsFolder = "Assets/SaveToolkit/Settings";

        [MenuItem(MenuRoot + "Open Save Folder")]
        private static void OpenSaveFolder()
        {
            SaveProfile profile = FindEditorProfile();
            if (profile == null)
                return;

            if (profile.Storage != SaveStorageType.File)
            {
                EditorUtility.DisplayDialog(
                    "Save Toolkit",
                    $"Profile '{profile.name}' stores saves in PlayerPrefs, not files.",
                    "OK"
                );
                return;
            }

            string path = Path.Combine(Application.persistentDataPath, profile.SaveDirectory);
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem(MenuRoot + "Delete All Saves (Editor Profile)")]
        private static void DeleteAllSaves()
        {
            SaveProfile profile = FindEditorProfile();
            if (profile == null)
                return;

            bool confirmed = EditorUtility.DisplayDialog(
                "Save Toolkit",
                $"Delete every save written with profile '{profile.name}' " +
                $"({profile.Storage}: {profile.SaveDirectory})?",
                "Delete",
                "Cancel"
            );

            if (!confirmed)
                return;

            SaveManager manager;

            try
            {
                manager = SaveManager.Create(profile);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[SaveToolkit] " + ex.Message);
                return;
            }

            if (manager.DeleteAll(out string error))
                Debug.Log("[SaveToolkit] All saves deleted.");
            else
                Debug.LogError("[SaveToolkit] " + error);
        }

        [MenuItem(MenuRoot + "Create Default Settings")]
        private static void CreateDefaultSettings()
        {
            string resourcesFolder = DefaultSettingsFolder + "/Resources";
            Directory.CreateDirectory(resourcesFolder);

            SaveProfile editor = CreateProfile("Profile_Editor", p =>
            {
                p.IndentedJson = true;
                p.SlotCount = 3;
                p.AutosaveOnQuit = true;
            });

            bool messagePack = SaveSerializerRegistry.IsAvailable(SaveSerializationFormat.MessagePack);

            SaveProfile mobile = CreateProfile("Profile_Mobile", p =>
            {
                p.Format = messagePack ? SaveSerializationFormat.MessagePack : SaveSerializationFormat.Json;
                p.IndentedJson = false;
                p.Compression = SaveCompressionType.GZip;
                p.EnableEncryption = true;
                p.EncryptionPassphrase = GUID.Generate().ToString();
                p.EncryptionSalt = GUID.Generate().ToString();
                p.SlotCount = 3;
                p.AutosaveOnPause = true;
                p.AutosaveOnFocusLost = true;
                p.AutosaveOnQuit = true;
                p.EnableDebugLogs = false;
            });

            SaveProfile steam = CreateProfile("Profile_Steam", p =>
            {
                p.IndentedJson = false;
                p.BackupCount = 3;
                p.SlotCount = 3;
                p.AutosaveOnQuit = true;
                p.AutosaveIntervalSeconds = 300f;
                p.EnableDebugLogs = false;
            });

            string settingsPath = resourcesFolder + "/SaveSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<SaveSettings>(settingsPath);

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<SaveSettings>();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }

            settings.EditorProfile = settings.EditorProfile ? settings.EditorProfile : editor;
            settings.MobileProfile = settings.MobileProfile ? settings.MobileProfile : mobile;
            settings.DesktopProfile = settings.DesktopProfile ? settings.DesktopProfile : steam;
            settings.FallbackProfile = settings.FallbackProfile ? settings.FallbackProfile : steam;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
        }

        private static SaveProfile CreateProfile(string name, System.Action<SaveProfile> configure)
        {
            string path = $"{DefaultSettingsFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<SaveProfile>(path);

            if (existing != null)
                return existing;

            var profile = ScriptableObject.CreateInstance<SaveProfile>();
            configure(profile);
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        private static SaveProfile FindEditorProfile()
        {
            SaveSettings settings = FindSettings();
            SaveProfile profile = settings != null
                ? settings.ResolveProfile(SavePlatform.Editor, settings.GetActiveStoreDefine())
                : null;

            if (profile == null)
                EditorUtility.DisplayDialog(
                    "Save Toolkit",
                    "No SaveSettings with an Editor/Fallback profile was found. " +
                    "Use Tools/Save Toolkit/Create Default Settings.",
                    "OK"
                );

            return profile;
        }

        internal static SaveSettings FindSettings()
        {
            var settings = Resources.Load<SaveSettings>(SaveSettings.ResourcesPath);
            if (settings != null)
                return settings;

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(SaveSettings));

            return guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<SaveSettings>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }
    }
}
