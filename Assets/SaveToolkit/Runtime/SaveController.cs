
using System.IO;
using UnityEngine;

public sealed class SaveController : Singleton<SaveController>
{
    [SerializeField] private SaveSettings settings;

    private SaveManager saveManager;



    protected override void Awake()
    {
        Debug.Log($"[SaveController] Awake started. Object: {name}", this);

        base.Awake();

        if (IsDuplicate)
        {
            Debug.LogWarning(
                $"[SaveController] Duplicate destroyed: {name}",
                this
            );
            return;
        }

        Debug.Log(
            $"[SaveController] Settings assigned: {settings != null}",
            this
        );

        if (settings == null)
        {
            Debug.LogError("SaveSettings asset is not assigned.", this);
            enabled = false;
            return;
        }

        Debug.Log(
            $"[SaveController] Directory: '{settings.SaveDirectory}', " +
            $"Backup: {settings.EnableBackup}, " +
            $"Compression: {settings.EnableCompression}, " +
            $"Encryption: {settings.EnableEncryption}",
            this
        );

        if (string.IsNullOrWhiteSpace(settings.SaveDirectory) ||
            Path.IsPathRooted(settings.SaveDirectory) ||
            HasInvalidPathSegment(settings.SaveDirectory))
        {
            Debug.LogError("SaveDirectory is invalid.", this);
            enabled = false;
            return;
        }

        if (settings.EnableBackup ||
            settings.EnableCompression ||
            settings.EnableEncryption)
        {
            Debug.LogError(
                "Disable backup, compression and encryption until implemented.",
                this
            );

            enabled = false;
            return;
        }

        string savePath = Path.Combine(
            Application.persistentDataPath,
            settings.SaveDirectory
        );

        saveManager = new SaveManager(savePath, settings);

        Debug.Log($"[SaveController] Initialized successfully: {savePath}", this);
    }



    public bool Save<T>(
        string key,
        T data,
        out string error)
    {
        if (saveManager == null)
        {
            error = "SaveController is not initialized.";
            return false;
        }

        return saveManager.Save(key, data, out error);
    }

    public bool TryLoad<T>(
        string key,
        out T data,
        out string error)
    {
        if (saveManager == null)
        {
            data = default;
            error = "SaveController is not initialized.";
            return false;
        }

        return saveManager.TryLoad(key, out data, out error);
    }

    public bool Exists(string key, out string error)
    {
        if (saveManager == null)
        {
            error = "SaveController is not initialized.";
            return false;
        }

        return saveManager.Exists(key, out error);
    }

    public bool Delete(string key, out string error)
    {
        if (saveManager == null)
        {
            error = "SaveController is not initialized.";
            return false;
        }

        return saveManager.Delete(key, out error);
    }

    private static bool HasInvalidPathSegment(string path)
    {
        string[] segments = path.Split(
            new[] { '/', '\\' },
            System.StringSplitOptions.RemoveEmptyEntries
        );

        foreach (string segment in segments)
        {
            if (segment == "." || segment == "..")
                return true;
        }

        return false;
    }
}
