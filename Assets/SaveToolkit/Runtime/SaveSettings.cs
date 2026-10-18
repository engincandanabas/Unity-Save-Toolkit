
using UnityEngine;

public enum SaveSerializationFormat
{
    Json,
    MessagePack
}

public enum SaveStorageType
{
    File,
    PlayerPrefs
}

[CreateAssetMenu(
    fileName = "SaveSettings",
    menuName = "SaveToolkit/SaveSettings"
)]
public sealed class SaveSettings : ScriptableObject
{
    [Header("Storage")]
    public string SaveDirectory = "Saves";
    public SaveStorageType Storage = SaveStorageType.File;

    [Header("Serialization")]
    public SaveSerializationFormat Format =
        SaveSerializationFormat.Json;

    public bool IndentedJson = true;

    [Header("Optional Tools")]
    public bool EnableBackup = false;
    public bool EnableCompression = false;
    public bool EnableEncryption = false;

    [Header("Debug")]
    public bool EnableDebugLogs = true;
}
