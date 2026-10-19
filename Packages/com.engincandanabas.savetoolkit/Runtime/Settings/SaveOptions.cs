
namespace SaveToolkit
{
    /// <summary>
    /// Plain, thread-safe snapshot of the settings SaveManager needs.
    /// SaveProfile (a ScriptableObject) produces one of these, so the
    /// manager never touches Unity objects from background threads.
    /// </summary>
    public sealed class SaveOptions
    {
        public const int MaxBackupCount = 5;
        public const int MaxSlotCount = 16;
        public const int MinSecretLength = 8;

        public SaveSerializationFormat Format = SaveSerializationFormat.Json;
        public bool IndentedJson = true;
        public SaveCompressionType Compression = SaveCompressionType.None;

        public bool EnableEncryption;
        public string EncryptionPassphrase = "";
        public string EncryptionSalt = "";

        public bool EnableBackup = true;
        public int BackupCount = 2;

        /// <summary>Force each write to the physical disk (safer, a few ms slower).</summary>
        public bool FlushToDisk = true;

        public int DataVersion = 1;

        public int SlotCount = 1;
        public int DefaultSlot;

        public bool EnableDebugLogs = true;

        public bool HasEncryptionSecret =>
            !string.IsNullOrEmpty(EncryptionPassphrase) &&
            !string.IsNullOrEmpty(EncryptionSalt);

        public bool Validate(out string error)
        {
            if (EnableEncryption)
            {
                if (string.IsNullOrEmpty(EncryptionPassphrase) ||
                    EncryptionPassphrase.Length < MinSecretLength)
                {
                    error = $"EncryptionPassphrase must be at least {MinSecretLength} characters.";
                    return false;
                }

                if (string.IsNullOrEmpty(EncryptionSalt) ||
                    EncryptionSalt.Length < MinSecretLength)
                {
                    error = $"EncryptionSalt must be at least {MinSecretLength} characters.";
                    return false;
                }
            }

            if (EnableBackup && (BackupCount < 1 || BackupCount > MaxBackupCount))
            {
                error = $"BackupCount must be between 1 and {MaxBackupCount}.";
                return false;
            }

            if (DataVersion < 1)
            {
                error = "DataVersion must be 1 or greater.";
                return false;
            }

            if (SlotCount < 1 || SlotCount > MaxSlotCount)
            {
                error = $"SlotCount must be between 1 and {MaxSlotCount}.";
                return false;
            }

            if (DefaultSlot < 0 || DefaultSlot >= SlotCount)
            {
                error = "DefaultSlot must be within SlotCount.";
                return false;
            }

            error = null;
            return true;
        }

        public SaveOptions Clone()
        {
            return (SaveOptions)MemberwiseClone();
        }
    }
}
