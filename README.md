# Unity Save Toolkit

A ScriptableObject-configured save system for Unity mobile and PC/Steam games, distributed as a UPM package.

## Features
- **Profiles:** per-platform and per-store (Steam, Epic, …) profiles, picked automatically at runtime.
- **Formats:** JSON built in; MessagePack as an optional, auto-detected module.
- **Security:** GZip compression and AES-256 + HMAC encryption.
- **Reliability:** atomic writes, rotating backups and automatic corruption recovery.
- **Data:** versioned saves with migrations, plus save slots.
- **Workflow:** async save/load and autosave on pause, quit or interval.
- **Performance:** low-allocation pipeline built on pooled buffers.

## Install
Package Manager → **Add package from git URL**:

```
https://github.com/engincandanabas/Unity-Save-Toolkit.git?path=Packages/com.engincandanabas.savetoolkit#v0.2.0
```

Requires Unity 2022.3+. A `.tgz` and a `.unitypackage` are also attached to each release.

## Usage
```csharp
using SaveToolkit;

SaveController.Instance.Save("player", progress, out string error);
SaveController.Instance.TryLoad("player", out PlayerProgress loaded, out error);
```

Run **Tools → Save Toolkit → Create Default Settings** to generate the settings and profiles.

## Repository layout
| Path | Contents |
|---|---|
| `Packages/com.engincandanabas.savetoolkit` | The package. Full docs in its [README](Packages/com.engincandanabas.savetoolkit/README.md). |
| `Assets/` | Development project (settings, demo scene). |
| `Tools/build_packages.py` | Builds the `.tgz` and `.unitypackage` into `Builds/`. |

## License
MIT
