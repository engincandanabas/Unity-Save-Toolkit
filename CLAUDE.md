# Unity Save Toolkit

Development repo for the UPM package `com.engincandanabas.savetoolkit`: a ScriptableObject-configured, low-allocation save system for Unity mobile and PC/Steam games. The user writes in Turkish; answer in Turkish. Code, comments, commit messages and package docs are in English.

## Rules
- **Never add Claude attribution** (`Co-Authored-By: Claude`, "Generated with Claude Code") to commits, tags or PRs.
- **Don't push.** This machine's git account (`engincandanabasgm`) has no write access to the repo, so a push returns 403. Commit locally, then give the user the push/merge/release commands.
- **Commit only what you changed.** Unity rewrites `Assets/NuGet.config.meta`, `ProjectSettings/*.asset` and `Assets/Scenes/Main.unity` on its own; leave those out unless asked.
- **Every new file or folder inside `Packages/com.engincandanabas.savetoolkit/` needs a `.meta` file.** Git-installed packages are read-only, and Unity skips files without one. Don't create `.meta` files for `Samples~/` (Unity ignores folders ending in `~`).
- **Don't change the save file format** (32-byte `STK2` header) without a migration path. Old saves must stay readable.
- The minimum Unity version is **2022.3**, and the code is C# 9 / netstandard2.1. Guard newer APIs with `#if UNITY_6000_x_OR_NEWER`.

## Layout
```
Packages/com.engincandanabas.savetoolkit/   the package (embedded in this dev project)
  Runtime/            SaveToolkit.Runtime  (depends only on Newtonsoft.Json)
    Core/             SaveController (MonoBehaviour entry point), SaveManager (pipeline), results, logger
    Settings/         SaveSettings (profile resolution), SaveProfile, SaveOptions (thread-safe snapshot)
    Pipeline/         ISaveSerializer, JSON serializer, GZip, AES+HMAC, SaveEnvelope (header), registry
    Storage/          ISaveStorage, FileSaveStorage, PlayerPrefsSaveStorage
    Buffers/          PooledBufferWriter + UTF-8 TextWriter/Reader for zero-string JSON
    Versioning/       migrations (JToken based)     Saveables/  ISaveable, SaveableBehaviour, registry
    MessagePack/      SaveToolkit.MessagePack, optional; compiles only with SAVETOOLKIT_MESSAGEPACK
  Editor/             menus, inspectors, StoreProfileBuildHook, MessagePackModuleDetector
  Tests/Editor/       EditMode tests (+ MessagePack/ tests with the same define constraint)
  Samples~/Basic/     demo imported via Package Manager (scene Assets/Scenes/Main.unity uses it)
Assets/SaveToolkit/Settings/   the dev project's SaveSettings + profiles (Resources/SaveSettings.asset)
Assets/Packages/               MessagePack installed with NuGetForUnity (exercises the optional module)
Tools/build_packages.py        builds Builds/*.tgz and *.unitypackage (Builds/ is gitignored)
```

## How it works
- **Save path:** serialize (on the caller's thread) → GZip → AES-256-CBC + HMAC-SHA256 → 32-byte header → `ISaveStorage`. Load reverses it. Reads take their settings from the header, so old saves stay readable after a profile change.
- **Files:** `slot_N/<key>.sav`, with backups at `<key>.sav.bak1..N` (`File.Replace` moves the old file into the backup) and bad files moved to `.corrupt`. v1 legacy files live at the root (`<key>.json` / `.dat`) and are migrated on load.
- **Concurrency:** one `SemaphoreSlim` gate guards storage. Async calls run the write on the thread pool with `ConfigureAwait(false)`; keep that, or a sync save on the main thread can deadlock. PlayerPrefs is main-thread only.
- **Profile resolution:** Override → EditorProfile (in the Editor) → first active store define (`StoreProfiles`, baked into players by the build hook) → platform profile → Fallback. With no settings asset, `SaveProfile.CreateDefault()` is used.

## Performance conventions
Allocation matters; measure before and after any change on a hot path.
- Write into `PooledBufferWriter`/`ArrayPool`; never build intermediate `byte[]` or JSON strings.
- Keep the sync path free of closures and LINQ. Guard info logs with `logger.IsInfoEnabled`.
- In Unity's Mono every `File.Exists` / `Directory.CreateDirectory` / `FileStream` call allocates 1–5 KB. `FileSaveStorage` caches known files and folders; don't add extra I/O probes.

## Testing
- **Unity tests:** add `"testables": ["com.engincandanabas.savetoolkit"]` to `Packages/manifest.json` (already present), then use **Test Runner → EditMode**.
- **Batchmode:** Unity can't open a project the Editor already has open, so copy it to a temp folder first (Assets, Packages, ProjectSettings; change `productName` so tests don't touch real saves). Open it by its **long path**: an 8.3 short path such as `ADMINI~1` breaks script↔class mapping. Each command runs tests in one mode; use `-testPlatform EditMode` or `-testPlatform PlayMode`:
  ```
  "C:/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor/Unity.exe" -batchmode -projectPath <copy> -runTests -testPlatform EditMode -testResults <out.xml> -logFile <out.log>
  ```
  The first run in a fresh copy adds `SAVETOOLKIT_MESSAGEPACK` mid-run, so run it a second time to include the MessagePack tests.
- **Tests to add:** every bug fix gets a regression test. Revert the fix briefly to confirm the test fails without it.
- **Compile check:** Unity 2022.3.62f3, 6000.3.26f1 and 6000.6.x are installed. Compile against 2022.3 to catch API use newer than the minimum.

## Releasing
1. Bump `version` in `package.json` and add a `CHANGELOG.md` entry.
2. Update the `#vX.Y.Z` in both READMEs.
3. Run `python Tools/build_packages.py`.
4. Commit. The user pushes, then creates the GitHub release: new tag `vX.Y.Z` on `main`, with `Builds/*.tgz` and `*.unitypackage` attached.
