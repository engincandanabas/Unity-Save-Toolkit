
using System;
using System.IO;
using MessagePack;
using MessagePack.Resolvers;
using Newtonsoft.Json;
using UnityEngine;

public sealed class SaveManager
{
    private const int MaxKeyLength = 64;
    private static readonly object FileOperationLock = new object();

    private static readonly MessagePackSerializerOptions MessagePackOptions =
        MessagePackSerializerOptions.Standard.WithResolver(
            ContractlessStandardResolver.Instance
        );

    private readonly string saveDirectory;
    private readonly SaveSettings settings;

    public SaveManager(string saveDirectory, SaveSettings settings)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory))
            throw new ArgumentException(
                "Save directory cannot be empty.",
                nameof(saveDirectory)
            );

        this.settings = settings != null
            ? settings
            : throw new ArgumentNullException(nameof(settings));

        this.saveDirectory = saveDirectory;
    }

    public bool Save<T>(string key, T data, out string error)
    {
        error = null;

        if (!IsValidKey(key))
        {
            error = "Invalid save key.";
            LogError(error);
            return false;
        }

        if (data is null)
        {
            error = "Cannot save null data.";
            LogError(error);
            return false;
        }

        lock (FileOperationLock)
        {
            string tempPath = null;

            try
            {
                byte[] bytes = Serialize(data);

                if (settings.Storage == SaveStorageType.PlayerPrefs)
                {
                    string value = settings.Format ==
                        SaveSerializationFormat.Json
                        ? System.Text.Encoding.UTF8.GetString(bytes)
                        : Convert.ToBase64String(bytes);

                    PlayerPrefs.SetString(GetPrefsKey(key), value);
                    PlayerPrefs.Save();

                    Log("Save completed: " + key);
                    return true;
                }

                Directory.CreateDirectory(saveDirectory);

                string filePath = GetFilePath(key);
                tempPath = filePath + ".tmp";

                File.WriteAllBytes(tempPath, bytes);
                File.Move(tempPath, filePath);

                Log("Save completed: " + key);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Save failed: {ex.Message}";
                LogError(error);
                return false;
            }
            finally
            {
                if (tempPath != null)
                {
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }

    public bool TryLoad<T>(
        string key,
        out T data,
        out string error)
    {
        data = default;
        error = null;

        if (!IsValidKey(key))
        {
            error = "Invalid save key.";
            LogError(error);
            return false;
        }

        lock (FileOperationLock)
        {
            try
            {
                byte[] bytes;

                if (settings.Storage == SaveStorageType.PlayerPrefs)
                {
                    string prefsKey = GetPrefsKey(key);

                    if (!PlayerPrefs.HasKey(prefsKey))
                    {
                        error = "Save data not found.";
                        Log(error);
                        return false;
                    }

                    string value = PlayerPrefs.GetString(prefsKey);

                    bytes = settings.Format ==
                        SaveSerializationFormat.Json
                        ? System.Text.Encoding.UTF8.GetBytes(value)
                        : Convert.FromBase64String(value);
                }
                else
                {
                    string filePath = GetFilePath(key);

                    if (!File.Exists(filePath))
                    {
                        error = "Save file not found.";
                        Log(error);
                        return false;
                    }

                    bytes = File.ReadAllBytes(filePath);

                    if (bytes.Length == 0)
                    {
                        error = "Save file is empty.";
                        LogError(error);
                        return false;
                    }
                }

                data = Deserialize<T>(bytes);

                if (data is null)
                {
                    error = "Save data is null or invalid.";
                    LogError(error);
                    return false;
                }

                Log("Load completed: " + key);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Load failed: {ex.Message}";
                LogError(error);
                data = default;
                return false;
            }
        }
    }

    public bool Exists(string key, out string error)
    {
        error = null;

        if (!IsValidKey(key))
        {
            error = "Invalid save key.";
            LogError(error);
            return false;
        }

        try
        {
            bool exists = settings.Storage == SaveStorageType.PlayerPrefs
                ? PlayerPrefs.HasKey(GetPrefsKey(key))
                : File.Exists(GetFilePath(key));

            Log($"Exists check for '{key}': {exists}");
            return exists;
        }
        catch (Exception ex)
        {
            error = $"Save existence check failed: {ex.Message}";
            LogError(error);
            return false;
        }
    }

    public bool Delete(string key, out string error)
    {
        error = null;

        if (!IsValidKey(key))
        {
            error = "Invalid save key.";
            LogError(error);
            return false;
        }

        try
        {
            if (settings.Storage == SaveStorageType.PlayerPrefs)
            {
                PlayerPrefs.DeleteKey(GetPrefsKey(key));
                PlayerPrefs.Save();
            }
            else
            {
                string filePath = GetFilePath(key);

                if (File.Exists(filePath))
                    File.Delete(filePath);
            }

            Log("Delete completed: " + key);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Delete failed: {ex.Message}";
            LogError(error);
            return false;
        }
    }

    private byte[] Serialize<T>(T data)
    {
        if (settings.Format == SaveSerializationFormat.Json)
        {
            string json = JsonConvert.SerializeObject(
                data,
                new JsonSerializerSettings
                {
                    Formatting = settings.IndentedJson
                        ? Formatting.Indented
                        : Formatting.None
                }
            );

            return System.Text.Encoding.UTF8.GetBytes(json);
        }

        return MessagePackSerializer.Serialize(
            data,
            MessagePackOptions
        );
    }

    private T Deserialize<T>(byte[] bytes)
    {
        if (settings.Format == SaveSerializationFormat.Json)
        {
            string json = System.Text.Encoding.UTF8.GetString(bytes);

            return JsonConvert.DeserializeObject<T>(json);
        }

        return MessagePackSerializer.Deserialize<T>(
            bytes,
            MessagePackOptions
        );
    }

    private string GetFilePath(string key)
    {
        string extension = settings.Format ==
            SaveSerializationFormat.Json
            ? ".json"
            : ".dat";

        return Path.Combine(saveDirectory, key + extension);
    }

    private string GetPrefsKey(string key)
    {
        return settings.SaveDirectory + "/" + key;
    }

    private static bool IsValidKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
            return false;

        foreach (char c in key)
        {
            bool valid =
                (c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') ||
                c == '_' ||
                c == '-';

            if (!valid)
                return false;
        }

        return true;
    }

    private void Log(string message)
    {
        if (settings.EnableDebugLogs)
            Debug.Log("[SaveToolkit] " + message);
    }

    private void LogError(string message)
    {
        if (settings.EnableDebugLogs)
            Debug.LogError("[SaveToolkit] " + message);
    }
}
