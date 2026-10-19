# Changelog

## [0.2.0] - 2026-10-19
### Added
- UPM package `com.engincandanabas.savetoolkit`, installable via Git URL.
- Store profiles: map scripting defines (STEAM, EPIC, …) to SaveProfiles; baked into player builds by a build hook.
- Optional MessagePack module (`SaveToolkit.MessagePack`), enabled automatically through the `SAVETOOLKIT_MESSAGEPACK` define when MessagePack is installed.
- Built-in default profile when no SaveSettings asset exists.
- `FlushToDisk` profile option.
- `SaveSerializerRegistry` for custom formats.

### Changed
- Pooled-buffer pipeline: serializers, compression, encryption and storage write into `ArrayPool` buffers; JSON is encoded/decoded without intermediate strings; Aes/HMAC/SHA-256 instances are reused per thread. Save allocations drop from ~17-121 KB to ~1-33 KB per call in the benchmarks (see README).
- Backups use `File.Replace` (atomic move of the old file) instead of copying.
- `FileSaveStorage` caches known files and created folders: under Unity's Mono each `File.Exists`/`Directory.CreateDirectory` allocates 1-2.5 KB. A small save went from ~26 KB to ~14 KB of GC allocations in the Unity Editor; files deleted outside the game are still handled.
- A load of a missing key checks only `bak1` before reporting "not found".
- Loads read the main file directly and probe backups only on corruption.
- Slot metadata comes from file timestamps and headers; `_slot.json` is gone.
- GZip uses `CompressionLevel.Fastest`.
- **Breaking:** `ISaveSerializer`, `ISaveStorage`, `ISaveCompressor`, `ISaveEncryptor`, `ISaveLogger` signatures changed; `SaveAll`/`LoadAll` take `IReadOnlyList<ISaveable>`.
- `SaveController` no longer derives from `Singleton<T>`; `Instance` returns null while quitting.

### Compatibility
- Save files written by 0.1 (STK2 header and legacy v1 files) are read unchanged.

## [0.1.0] - 2026-10-19
- Initial save system: profiles per platform, JSON/MessagePack, GZip, AES + HMAC, backups and recovery, migrations, slots, async, autosave.
