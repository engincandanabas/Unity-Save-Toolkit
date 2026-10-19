# Save Toolkit

A low-allocation save system for Unity mobile (iOS/Android) and PC/Steam games, configured with ScriptableObjects.

- **Profiles:** separate `SaveProfile` assets for Editor, Mobile, Desktop and stores (Steam/Epic/itch…), picked automatically at runtime.
- **Formats:** JSON (Newtonsoft) built in. MessagePack is an optional module that turns on by itself when MessagePack is in the project.
- **Security:** GZip compression and AES-256 + HMAC-SHA256 tamper detection.
- **Reliability:**
  - Atomic writes.
  - Rotating backups: `File.Replace` moves the old file to `bak1`, no copying.
  - Automatic recovery from the latest valid backup when a save is corrupted.
- **Versioning and slots:** `DataVersion` migrations and multiple save slots.
- **Async and autosave:** async save/load; autosave on pause, focus loss, quit or on an interval.
- **Performance:** every pipeline stage runs on `ArrayPool`-backed buffers, the sync path has no closures, and no log strings are built while debug logging is off.

## Installation

Package Manager → **+** → **Add package from git URL**:

```
https://github.com/engincandanabas/Unity-Save-Toolkit.git?path=Packages/com.engincandanabas.savetoolkit#v0.2.1
```

The only dependency, `com.unity.nuget.newtonsoft-json`, is installed automatically. Requires Unity 2022.3 or newer.

Other options (files are attached to each [GitHub release](https://github.com/engincandanabas/Unity-Save-Toolkit/releases), or build them with `python Tools/build_packages.py`):
- **UPM tarball:** `com.engincandanabas.savetoolkit-<version>.tgz` → Package Manager → **Add package from tarball**. Newtonsoft is installed automatically.
- **.unitypackage:** `SaveToolkit-<version>.unitypackage` → **Assets → Import Package**. Files go to `Assets/SaveToolkit`. This format cannot declare dependencies, so install `com.unity.nuget.newtonsoft-json` yourself, and update by hand.

**MessagePack (optional):** install MessagePack with [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity). Save Toolkit adds the `SAVETOOLKIT_MESSAGEPACK` define itself and the `SaveToolkit.MessagePack` module compiles. Turn this off or manage it by hand under **Tools → Save Toolkit → MessagePack Module**.

### First steps
1. **Tools → Save Toolkit → Create Default Settings** creates `Assets/SaveToolkit/Settings/Resources/SaveSettings.asset` and three profiles (Editor, Mobile, Steam). Without any asset the system still works: `SaveController` starts with a built-in default profile (JSON files with backups) and logs a warning.
2. If encryption is on, **set your own `EncryptionPassphrase` and `EncryptionSalt`.** Never change them after release: existing encrypted saves would become unreadable.
3. Samples: Package Manager → Save Toolkit → **Samples → Basic → Import**.

## Usage

```csharp
using SaveToolkit;

SaveController.Instance.Save("player_progress", progress, out string error);
SaveController.Instance.TryLoad("player_progress", out PlayerProgress loaded, out error);

SaveResult result = await SaveController.Instance.SaveAsync("player_progress", progress);
SaveResult<PlayerProgress> load = await SaveController.Instance.LoadAsync<PlayerProgress>("player_progress");
if (load.NotFound) { /* new game */ }

SaveController.Instance.SetActiveSlot(1, out error);
SaveController.Instance.TryGetSlotInfo(1, out SaveSlotInfo info, out error);
```

On success `TryLoad` leaves `error` null, or sets it to a note when the data was recovered from a backup. While the application quits, `SaveController.Instance` returns null so no new object is created during shutdown.

### Autosave and ISaveable

```csharp
public sealed class PlayerHealth : SaveableBehaviour<PlayerHealth.State>
{
    public sealed class State { public int Hp; }   // MessagePack + IL2CPP: add [MessagePackObject(true)]

    private int hp = 100;

    protected override State CaptureState() => new State { Hp = hp };
    protected override void RestoreState(State state) => hp = state.Hp;
}
```

Set a `saveKey` in the Inspector. While enabled, the component takes part in `SaveAll` / `LoadAll` and autosave. For data that is not an `ISaveable`, listen to `SaveController.AutosaveRequested`.

| Trigger | Profile field | Note |
|---|---|---|
| App sent to background | `AutosaveOnPause` | Synchronous: mobile OSes may kill the app at any time |
| Focus lost | `AutosaveOnFocusLost` | Saves once when it fires right after Pause |
| Quit | `AutosaveOnQuit` | Desktop |
| Interval | `AutosaveIntervalSeconds` | Asynchronous |

### Migrations

```csharp
SaveController.Instance.RegisterMigration(new SaveMigration("player_progress", 1, token =>
{
    token["Coins"] = token["Gold"];
    ((JObject)token).Remove("Gold");
    return token;
}));
```

Bump `DataVersion` in the profile and register a migration from the previous version, before the first load that needs it. `SaveMigration.AnyKey` (`"*"`) applies to every key.

## Profile selection

The profile is chosen in this order:
1. `OverrideProfile`, when set, on every platform.
2. `EditorProfile` in the Editor.
3. The **first** `StoreProfiles` entry whose define is active.
4. The platform profile: `MobileProfile` on Android and iOS, `DesktopProfile` on Windows, macOS and Linux players.
5. `FallbackProfile`.

**Store profiles** map scripting defines to profiles (`STEAM → Profile_Steam`, `EPIC → Profile_Epic`). Defines only exist at compile time, so a build hook writes the active define into `SaveSettings` before a player build and clears it afterwards. Defines passed through `BuildPlayerOptions.extraScriptingDefines` are not seen by the hook; declare store defines in Player Settings. The **Resolved Profiles** table in the `SaveSettings` Inspector shows which profile each platform and store will use.

## Performance

### Unity 6000.3 Editor (Mono), Windows, backups on, `FlushToDisk` on
Averages over 200 iterations; GC values from the `GC Allocated In Frame` counter.

| Scenario | Save alloc | Save time | Load alloc | Load time | File |
|---|---:|---:|---:|---:|---:|
| JSON, small DTO | 14.2 KB | 3.6 ms | 6.3 KB | 0.40 ms | 83 B |
| JSON, 10 KB data | 29.2 KB | 6.2 ms | 35.9 KB | 2.4 ms | 7.6 KB |
| MessagePack, 10 KB data | 13.6 KB | 4.9 ms | 18.6 KB | 1.7 ms | 4.9 KB |
| MessagePack + GZip + AES, 10 KB | 19.2 KB | 10.1 ms | 44.0 KB | 1.2 ms | 1.2 KB |

The pipeline itself is cheap: JSON serialization ~0.4 KB, envelope ~0.1 KB, buffer ~0.03 KB. Most allocations come from Mono's file APIs:

| Call | Allocated | Time |
|---|---:|---:|
| Opening a `FileStream` | ~4.6 KB | ~0.5 ms |
| `File.Replace` | ~3.3 KB | ~2 ms |
| `File.Move` | ~2.2 KB | — |
| `Directory.CreateDirectory` | ~2.4 KB | — |
| `File.Exists` | ~1 KB | — |

That is why `FileSaveStorage` caches known files and created folders. The rest is the unavoidable cost of writing a file.

### .NET 8 (pipeline only, v0.1 → v0.2)

| Scenario | v0.1 save | v0.2 save | v0.1 load | v0.2 load |
|---|---:|---:|---:|---:|
| JSON, small DTO | 17.5 KB | 1.4 KB | 6.3 KB | 0.55 KB |
| MessagePack, 10 KB data | 29.6 KB | 1.0 KB | 28.6 KB | 13.5 KB |

### Tips
- **Frequent saves:** prefer MessagePack and group data under fewer keys (`SaveAll` writes one file per `ISaveable`).
- **`BackupCount = 1`:** removes the extra `File.Move` during rotation (~2 KB and ~1 ms).
- **`FlushToDisk`:** turning it off skips fsync (~0.7 ms on Windows) at the risk of losing the latest write on power loss.
- **PlayerPrefs:** it only stores strings, so every operation allocates a Base64 string. Prefer file storage where possible.
- **Large saves:** `ArrayPool` does not pool buffers above ~1 MB. Split very large saves across keys.

## Platform notes

### Mobile (IL2CPP)
- Every type saved with MessagePack needs `[MessagePackObject]`; types with private setters must also be `partial`. The Editor warns once per type.
- Package assemblies are protected from stripping with `[assembly: Preserve]`. Mark your own save types with `[Preserve]` or list them in a `link.xml` under `Assets`.

### Steam Cloud (Auto-Cloud)
Saves are written to `Application.persistentDataPath/<SaveDirectory>/slot_N/*.sav`.

| Root | Subdirectory | Pattern | OS |
|---|---|---|---|
| `WinAppDataLocalLow` | `<Company>/<Product>/Saves` | `*.sav*` (recursive) | Windows |
| `MacAppSupport` | `unity.<Company>.<Product>/Saves` | `*.sav*` (recursive) | macOS |
| `LinuxXdgDataHome` | `unity3d/<Company>/<Product>/Saves` | `*.sav*` (recursive) | Linux |

The encryption key does not depend on the device, so saves open on other machines too. For cloud backends, implement `ISaveStorage` and pass it to `new SaveManager(options, storage)`.

## Extending
- **Custom format:** `SaveSerializerRegistry.Register(format, options => new MySerializer())`. An `ISaveSerializer` writes to an `IBufferWriter<byte>` and reads from `ReadOnlyMemory<byte>`.
- **Custom storage:** implement `ISaveStorage`. `Write` receives the header and payload as separate spans plus an optional backup path.

## File format
Each save starts with a 32-byte header: `STK2` magic, format, compression, encryption flag, `DataVersion`, payload length and a SHA-256 checksum, followed by the payload. Reads take their settings from the header, not the profile, so old saves stay readable after a profile change. Corrupted saves are never deleted; they are kept with a `.corrupt` extension for inspection.

## Tests
To see the package tests, add this to the project's `Packages/manifest.json`:

```json
"testables": ["com.engincandanabas.savetoolkit"]
```

Then run them from **Window → General → Test Runner → EditMode**. MessagePack tests appear only while the module is active.
