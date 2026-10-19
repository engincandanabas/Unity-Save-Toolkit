# Changelog

## [0.2.1] - 2026-10-19
### Fixed
- A wrong passphrase/salt (e.g. two store builds with different salts sharing a save folder) was treated as corruption and moved the save aside. Authentication failures now leave every file untouched and report "could not be decrypted".
- A synchronous save that reached the storage gate before an earlier queued async save could be overwritten by that older snapshot. Writes now carry a capture sequence and older snapshots are skipped.
- Storage errors (locked files, I/O faults) escaped `TryLoad`, `LoadAsync` and `LoadAll` as exceptions; they now return a failed result. Invalid Base64 in PlayerPrefs counts as corruption, so backups are tried.
- `LoadAll` called from inside a `RestoreState` silently skipped the remaining saveables of the outer call.
- `SaveController` on a child object was not kept across scenes; it is now moved to the scene root. A duplicate controller removes only itself when its GameObject holds other components.
- The `File.Replace` fallback could delete the previous backup and then fail; the old backup is now kept until the new file is in place.
- `DeleteSlot` on the default slot left v1 legacy saves behind, so a "new game" could bring them back.
- `Utf8MemoryTextReader.Peek` could drop a character.
- The store build hook and Editor resolution now also read the active Build Profile's defines (Unity 6.2+).

### Notes
- MessagePack migrations round-trip through JSON: `byte[]` and extension types (e.g. `DateTime`) do not survive them.

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
