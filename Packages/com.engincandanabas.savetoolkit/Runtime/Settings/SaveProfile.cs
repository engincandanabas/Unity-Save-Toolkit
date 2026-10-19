
using System;
using UnityEngine;

namespace SaveToolkit
{
    [CreateAssetMenu(
        fileName = "SaveProfile",
        menuName = "SaveToolkit/Save Profile"
    )]
    public sealed class SaveProfile : ScriptableObject
    {
        [Header("Storage")]
        public SaveStorageType Storage = SaveStorageType.File;

        [Tooltip("Relative folder under Application.persistentDataPath " +
                 "(or PlayerPrefs key prefix).")]
        public string SaveDirectory = "Saves";

        [Tooltip("Force every write to the physical disk. Survives power loss and " +
                 "OS crashes; costs a few milliseconds per save.")]
        public bool FlushToDisk = true;

        [Header("Serialization")]
        public SaveSerializationFormat Format = SaveSerializationFormat.Json;
        public bool IndentedJson = true;

        [Header("Compression")]
        public SaveCompressionType Compression = SaveCompressionType.None;

        [Header("Encryption")]
        [Tooltip("AES-256 + HMAC. Protects against casual tampering; " +
                 "the passphrase ships inside the build.")]
        public bool EnableEncryption;
        public string EncryptionPassphrase = "";
        public string EncryptionSalt = "";

        [Header("Backup")]
        public bool EnableBackup = true;

        [Range(1, SaveOptions.MaxBackupCount)]
        public int BackupCount = 2;

        [Header("Versioning")]
        [Tooltip("Bump when the save data shape changes and register " +
                 "a migration from the previous version.")]
        [Min(1)]
        public int DataVersion = 1;

        [Header("Slots")]
        [Range(1, SaveOptions.MaxSlotCount)]
        public int SlotCount = 1;

        [Min(0)]
        public int DefaultSlot;

        [Header("Autosave")]
        [Tooltip("Save all ISaveables when the app is paused (mobile background).")]
        public bool AutosaveOnPause;

        [Tooltip("Save all ISaveables when the app loses focus.")]
        public bool AutosaveOnFocusLost;

        [Tooltip("Save all ISaveables when the app quits (desktop).")]
        public bool AutosaveOnQuit;

        [Tooltip("Periodic background save in seconds. 0 disables it.")]
        [Min(0)]
        public float AutosaveIntervalSeconds;

        [Header("Debug")]
        public bool EnableDebugLogs = true;

        public SaveOptions ToOptions()
        {
            return new SaveOptions
            {
                Format = Format,
                IndentedJson = IndentedJson,
                Compression = Compression,
                EnableEncryption = EnableEncryption,
                EncryptionPassphrase = EncryptionPassphrase ?? "",
                EncryptionSalt = EncryptionSalt ?? "",
                EnableBackup = EnableBackup,
                BackupCount = BackupCount,
                FlushToDisk = FlushToDisk,
                DataVersion = DataVersion,
                SlotCount = SlotCount,
                DefaultSlot = DefaultSlot,
                EnableDebugLogs = EnableDebugLogs
            };
        }

        /// <summary>
        /// In-memory profile used when no SaveSettings asset exists:
        /// JSON files with backups, no encryption.
        /// </summary>
        public static SaveProfile CreateDefault()
        {
            SaveProfile profile = CreateInstance<SaveProfile>();
            profile.name = "Default (built-in)";
            profile.hideFlags = HideFlags.DontSave;
            profile.IndentedJson = false;
            profile.AutosaveOnPause = true;
            profile.AutosaveOnQuit = true;
            profile.EnableDebugLogs = false;
            return profile;
        }

        public bool Validate(out string error)
        {
            if (!IsValidRelativeDirectory(SaveDirectory))
            {
                error = $"SaveDirectory '{SaveDirectory}' is invalid.";
                return false;
            }

            return ToOptions().Validate(out error);
        }

        public static bool IsValidRelativeDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                System.IO.Path.IsPathRooted(path))
                return false;

            string[] segments = path.Split(
                new[] { '/', '\\' },
                StringSplitOptions.RemoveEmptyEntries
            );

            foreach (string segment in segments)
            {
                if (segment == "." || segment == "..")
                    return false;
            }

            return segments.Length > 0;
        }

        private void OnValidate()
        {
            DefaultSlot = Mathf.Clamp(DefaultSlot, 0, Mathf.Max(0, SlotCount - 1));
        }
    }
}
