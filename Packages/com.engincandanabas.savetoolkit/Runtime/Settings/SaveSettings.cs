
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaveToolkit
{
    /// <summary>
    /// Root settings asset. Picks a SaveProfile for the running platform and
    /// store. Keep one instance at Resources/SaveSettings so a SaveController
    /// created at runtime can find it.
    /// </summary>
    /// <remarks>
    /// Resolution order: OverrideProfile → EditorProfile (Editor only) →
    /// first StoreProfiles entry whose define is active → platform profile
    /// → FallbackProfile.
    /// </remarks>
    [CreateAssetMenu(
        fileName = "SaveSettings",
        menuName = "SaveToolkit/Save Settings"
    )]
    public sealed class SaveSettings : ScriptableObject
    {
        public const string ResourcesPath = "SaveSettings";

        [Serializable]
        public sealed class StoreProfile
        {
            [Tooltip("Scripting define that marks this store build, e.g. STEAM, EPIC, ITCH.")]
            public string Define;
            public SaveProfile Profile;
        }

        [Header("Platform Profiles")]
        public SaveProfile EditorProfile;
        public SaveProfile MobileProfile;

        [Tooltip("Windows / macOS / Linux standalone.")]
        public SaveProfile DesktopProfile;

        [Tooltip("Used when the platform specific profile is not assigned.")]
        public SaveProfile FallbackProfile;

        [Header("Store Profiles")]
        [Tooltip("Checked in order; the first entry whose define is active wins " +
                 "over the platform profile. Baked into the build automatically.")]
        public List<StoreProfile> StoreProfiles = new List<StoreProfile>();

        [Header("Override")]
        [Tooltip("When set, used on every platform. Handy for testing " +
                 "the mobile profile inside the Editor.")]
        public SaveProfile OverrideProfile;

        // Written by the build hook (SaveToolkit.Editor) right before a player
        // build; defines are compile-time only, so the player reads this.
        [SerializeField, HideInInspector] private string bakedStoreDefine;

        public static SavePlatform CurrentPlatform =>
            GetPlatform(Application.platform);

        public string BakedStoreDefine
        {
            get => bakedStoreDefine;
            set => bakedStoreDefine = value;
        }

        public SaveProfile ResolveProfile()
        {
            return ResolveProfile(CurrentPlatform, GetActiveStoreDefine());
        }

        public SaveProfile ResolveProfile(SavePlatform platform, string storeDefine = null)
        {
            if (OverrideProfile != null)
                return OverrideProfile;

            if (platform == SavePlatform.Editor && EditorProfile != null)
                return EditorProfile;

            SaveProfile store = GetStoreProfile(storeDefine);
            if (store != null)
                return store;

            SaveProfile profile = GetPlatformProfile(platform);

            return profile != null ? profile : FallbackProfile;
        }

        public SaveProfile GetPlatformProfile(SavePlatform platform)
        {
            switch (platform)
            {
                case SavePlatform.Editor: return EditorProfile;
                case SavePlatform.Mobile: return MobileProfile;
                case SavePlatform.Desktop: return DesktopProfile;
                default: return null;
            }
        }

        public SaveProfile GetStoreProfile(string define)
        {
            if (string.IsNullOrEmpty(define))
                return null;

            foreach (StoreProfile entry in StoreProfiles)
            {
                if (entry != null && entry.Profile != null && entry.Define == define)
                    return entry.Profile;
            }

            return null;
        }

        /// <summary>
        /// Returns the first StoreProfiles define contained in a
        /// ';'-separated define list (as PlayerSettings stores them), or null.
        /// </summary>
        public string FindStoreDefine(string scriptingDefines)
        {
            if (string.IsNullOrEmpty(scriptingDefines))
                return null;

            string[] active = scriptingDefines.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (StoreProfile entry in StoreProfiles)
            {
                if (entry == null || entry.Profile == null || string.IsNullOrWhiteSpace(entry.Define))
                    continue;

                if (Array.IndexOf(active, entry.Define.Trim()) >= 0)
                    return entry.Define;
            }

            return null;
        }

        /// <summary>In the Editor: from the active build target's defines. In players: baked at build time.</summary>
        public string GetActiveStoreDefine()
        {
#if UNITY_EDITOR
            var group = UnityEditor.EditorUserBuildSettings.selectedBuildTargetGroup;
            var target = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
            return FindStoreDefine(UnityEditor.PlayerSettings.GetScriptingDefineSymbols(target));
#else
            return bakedStoreDefine;
#endif
        }

        public static SavePlatform GetPlatform(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.LinuxEditor:
                    return SavePlatform.Editor;

                case RuntimePlatform.Android:
                case RuntimePlatform.IPhonePlayer:
                    return SavePlatform.Mobile;

                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.LinuxPlayer:
                    return SavePlatform.Desktop;

                default:
                    return SavePlatform.Other;
            }
        }
    }
}
