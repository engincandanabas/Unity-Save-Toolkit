
using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace SaveToolkit
{
    /// <summary>
    /// Scene-level entry point: resolves the SaveProfile, owns the
    /// SaveManager and drives autosave. Created on first access to
    /// <see cref="Instance"/> when no instance exists in the scene.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class SaveController : MonoBehaviour
    {
        private const string NotInitialized = "SaveController is not initialized.";

        // Mobile fires Pause and FocusLost back to back; save once.
        private const float AutosaveDebounceSeconds = 1f;

        private static SaveController instance;
        private static bool isQuitting;

        [SerializeField] private SaveSettings settings;

        private SaveManager saveManager;
        private SaveProfile profile;
        private ISaveLogger logger;
        private Coroutine intervalRoutine;
        private float lastAutosaveTime = float.NegativeInfinity;

        /// <summary>
        /// Raised right before an autosave writes the registered ISaveables.
        /// Use it to save data that is not an ISaveable.
        /// </summary>
        public event Action<SaveTrigger> AutosaveRequested;

        /// <summary>
        /// The scene instance, created on demand. Null while the application
        /// is quitting, so late callers cannot spawn a new object.
        /// </summary>
        public static SaveController Instance
        {
            get
            {
                if (instance != null || isQuitting)
                    return instance;

                instance = FindAnyObjectByType<SaveController>();

                if (instance == null)
                    instance = new GameObject(nameof(SaveController)).AddComponent<SaveController>();

                return instance;
            }
        }

        public bool IsInitialized => saveManager != null;

        public SaveProfile ActiveProfile => profile;

        public SaveManager Manager => saveManager;

        public int ActiveSlot => saveManager != null ? saveManager.ActiveSlot : -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Static state survives Enter Play Mode without domain reload.
            instance = null;
            isQuitting = false;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);

            if (settings == null)
                settings = Resources.Load<SaveSettings>(SaveSettings.ResourcesPath);

            if (settings != null)
            {
                profile = settings.ResolveProfile();

                if (profile == null)
                {
                    Debug.LogError(
                        $"[SaveToolkit] No SaveProfile for platform {SaveSettings.CurrentPlatform} " +
                        "and no FallbackProfile assigned.",
                        settings
                    );
                    enabled = false;
                    return;
                }
            }
            else
            {
                profile = SaveProfile.CreateDefault();
                Debug.LogWarning(
                    "[SaveToolkit] No SaveSettings found at Resources/" + SaveSettings.ResourcesPath +
                    "; using the built-in default profile (JSON files with backups). " +
                    "Create one via Tools/Save Toolkit/Create Default Settings.",
                    this
                );
            }

            if (!profile.Validate(out string error))
            {
                Debug.LogError($"[SaveToolkit] Profile '{profile.name}' is invalid: {error}", profile);
                enabled = false;
                return;
            }

            logger = new UnitySaveLogger(profile.EnableDebugLogs);

            try
            {
                saveManager = SaveManager.Create(profile, logger);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveToolkit] Profile '{profile.name}' cannot be used: {ex.Message}", profile);
                enabled = false;
                return;
            }

            if (logger.IsInfoEnabled)
                logger.Log(
                    $"Initialized with profile '{profile.name}' " +
                    $"({SaveSettings.CurrentPlatform}, {profile.Storage}, {profile.Format}, " +
                    $"compression {profile.Compression}, encryption {profile.EnableEncryption}).");
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        private void OnEnable()
        {
            if (saveManager != null && profile.AutosaveIntervalSeconds > 0f)
                intervalRoutine = StartCoroutine(AutosaveIntervalRoutine());
        }

        private void OnDisable()
        {
            if (intervalRoutine != null)
            {
                StopCoroutine(intervalRoutine);
                intervalRoutine = null;
            }
        }

        // ------------------------------------------------------------------
        // Single entries
        // ------------------------------------------------------------------

        public bool Save<T>(string key, T data, out string error)
        {
            if (!EnsureInitialized(out error))
                return false;

            return saveManager.Save(key, data, out error);
        }

        public bool TryLoad<T>(string key, out T data, out string error)
        {
            if (!EnsureInitialized(out error))
            {
                data = default;
                return false;
            }

            return saveManager.TryLoad(key, out data, out error);
        }

        public bool Exists(string key, out string error)
        {
            if (!EnsureInitialized(out error))
                return false;

            return saveManager.Exists(key, out error);
        }

        public bool Delete(string key, out string error)
        {
            if (!EnsureInitialized(out error))
                return false;

            return saveManager.Delete(key, out error);
        }

        public Task<SaveResult> SaveAsync<T>(string key, T data)
        {
            if (!EnsureInitialized(out string error))
                return Task.FromResult(SaveResult.Fail(error));

            return saveManager.SaveAsync(key, data);
        }

        public Task<SaveResult<T>> LoadAsync<T>(string key)
        {
            if (!EnsureInitialized(out string error))
                return Task.FromResult(new SaveResult<T>(false, default, error, false, false));

            return saveManager.LoadAsync<T>(key);
        }

        // ------------------------------------------------------------------
        // ISaveable groups
        // ------------------------------------------------------------------

        public bool SaveAll(out string error)
        {
            if (!EnsureInitialized(out error))
                return false;

            return saveManager.SaveAll(SaveableRegistry.All, out error);
        }

        public Task<SaveResult> SaveAllAsync()
        {
            if (!EnsureInitialized(out string error))
                return Task.FromResult(SaveResult.Fail(error));

            return saveManager.SaveAllAsync(SaveableRegistry.All);
        }

        public bool LoadAll(out string error)
        {
            if (!EnsureInitialized(out error))
                return false;

            return saveManager.LoadAll(SaveableRegistry.All, out error);
        }

        // ------------------------------------------------------------------
        // Slots and migrations
        // ------------------------------------------------------------------

        public bool SetActiveSlot(int slot, out string error)
        {
            if (!EnsureInitialized(out error))
                return false;

            return saveManager.SetActiveSlot(slot, out error);
        }

        public bool TryGetSlotInfo(int slot, out SaveSlotInfo info, out string error)
        {
            if (!EnsureInitialized(out error))
            {
                info = null;
                return false;
            }

            return saveManager.TryGetSlotInfo(slot, out info, out error);
        }

        public bool DeleteSlot(int slot, out string error)
        {
            if (!EnsureInitialized(out error))
                return false;

            return saveManager.DeleteSlot(slot, out error);
        }

        /// <summary>Register before the first load that needs it (e.g. in Awake).</summary>
        public void RegisterMigration(ISaveMigration migration)
        {
            if (!EnsureInitialized(out string error))
            {
                Debug.LogError("[SaveToolkit] " + error, this);
                return;
            }

            saveManager.RegisterMigration(migration);
        }

        // ------------------------------------------------------------------
        // Autosave
        // ------------------------------------------------------------------

        /// <summary>Raises AutosaveRequested and saves all ISaveables now.</summary>
        public bool Autosave(SaveTrigger trigger, out string error)
        {
            if (!EnsureInitialized(out error))
                return false;

            lastAutosaveTime = Time.realtimeSinceStartup;
            RaiseAutosaveRequested(trigger);

            bool success = saveManager.SaveAll(SaveableRegistry.All, out error);

            if (success)
            {
                if (logger.IsInfoEnabled)
                    logger.Log($"Autosave ({trigger}) completed.");
            }
            else
                logger.LogError($"Autosave ({trigger}) failed: {error}");

            return success;
        }

        // Mobile OSes may kill a backgrounded app at any moment, so these
        // save synchronously.
        private void OnApplicationPause(bool paused)
        {
            if (paused && profile != null && profile.AutosaveOnPause)
                TryAutosave(SaveTrigger.Pause);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && profile != null && profile.AutosaveOnFocusLost)
                TryAutosave(SaveTrigger.FocusLost);
        }

        private void OnApplicationQuit()
        {
            if (profile != null && profile.AutosaveOnQuit)
                Autosave(SaveTrigger.Quit, out _);

            isQuitting = true;
        }

        private void TryAutosave(SaveTrigger trigger)
        {
            if (Time.realtimeSinceStartup - lastAutosaveTime < AutosaveDebounceSeconds)
                return;

            Autosave(trigger, out _);
        }

        private IEnumerator AutosaveIntervalRoutine()
        {
            var wait = new WaitForSecondsRealtime(profile.AutosaveIntervalSeconds);

            while (true)
            {
                yield return wait;

                lastAutosaveTime = Time.realtimeSinceStartup;
                RaiseAutosaveRequested(SaveTrigger.Interval);

                Task<SaveResult> task = saveManager.SaveAllAsync(SaveableRegistry.All);

                while (!task.IsCompleted)
                    yield return null;

                if (task.IsFaulted || !task.Result.Success)
                {
                    string error = task.IsFaulted
                        ? task.Exception?.GetBaseException().Message
                        : task.Result.Error;

                    logger.LogError($"Interval autosave failed: {error}");
                }
            }
        }

        private void RaiseAutosaveRequested(SaveTrigger trigger)
        {
            try
            {
                AutosaveRequested?.Invoke(trigger);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }

        private bool EnsureInitialized(out string error)
        {
            if (saveManager != null)
            {
                error = null;
                return true;
            }

            error = NotInitialized;
            return false;
        }
    }
}
