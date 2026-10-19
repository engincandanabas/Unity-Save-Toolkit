
using System;
using NUnit.Framework;
using UnityEngine;

namespace SaveToolkit.Tests
{
    public class SaveSettingsTests
    {
        private SaveSettings settings;
        private SaveProfile editor;
        private SaveProfile mobile;
        private SaveProfile fallback;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<SaveSettings>();
            editor = ScriptableObject.CreateInstance<SaveProfile>();
            mobile = ScriptableObject.CreateInstance<SaveProfile>();
            fallback = ScriptableObject.CreateInstance<SaveProfile>();

            settings.EditorProfile = editor;
            settings.MobileProfile = mobile;
            settings.FallbackProfile = fallback;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(settings);
            UnityEngine.Object.DestroyImmediate(editor);
            UnityEngine.Object.DestroyImmediate(mobile);
            UnityEngine.Object.DestroyImmediate(fallback);
        }

        [Test]
        public void ResolveProfile_UsesPlatformProfile()
        {
            Assert.AreSame(editor, settings.ResolveProfile(SavePlatform.Editor));
            Assert.AreSame(mobile, settings.ResolveProfile(SavePlatform.Mobile));
        }

        [Test]
        public void ResolveProfile_FallsBackWhenMissing()
        {
            Assert.AreSame(fallback, settings.ResolveProfile(SavePlatform.Desktop));
            Assert.AreSame(fallback, settings.ResolveProfile(SavePlatform.Other));
        }

        [Test]
        public void ResolveProfile_OverrideWins()
        {
            var overrideProfile = ScriptableObject.CreateInstance<SaveProfile>();
            settings.OverrideProfile = overrideProfile;

            Assert.AreSame(overrideProfile, settings.ResolveProfile(SavePlatform.Mobile));
            Assert.AreSame(overrideProfile, settings.ResolveProfile(SavePlatform.Editor));

            UnityEngine.Object.DestroyImmediate(overrideProfile);
        }

        [Test]
        public void StoreProfile_BeatsPlatformProfile()
        {
            var steam = ScriptableObject.CreateInstance<SaveProfile>();
            var desktop = ScriptableObject.CreateInstance<SaveProfile>();
            settings.DesktopProfile = desktop;
            settings.StoreProfiles.Add(new SaveSettings.StoreProfile { Define = "STEAM", Profile = steam });

            Assert.AreSame(steam, settings.ResolveProfile(SavePlatform.Desktop, "STEAM"));
            Assert.AreSame(desktop, settings.ResolveProfile(SavePlatform.Desktop, "EPIC"));
            Assert.AreSame(desktop, settings.ResolveProfile(SavePlatform.Desktop));

            // The Editor profile still wins inside the Editor.
            Assert.AreSame(editor, settings.ResolveProfile(SavePlatform.Editor, "STEAM"));

            UnityEngine.Object.DestroyImmediate(steam);
            UnityEngine.Object.DestroyImmediate(desktop);
        }

        [Test]
        public void FindStoreDefine_UsesListOrder()
        {
            settings.StoreProfiles.Add(new SaveSettings.StoreProfile { Define = "EPIC", Profile = mobile });
            settings.StoreProfiles.Add(new SaveSettings.StoreProfile { Define = "STEAM", Profile = fallback });

            Assert.AreEqual("EPIC", settings.FindStoreDefine("FOO;STEAM;EPIC"));
            Assert.AreEqual("STEAM", settings.FindStoreDefine("STEAM;BAR"));
            Assert.IsNull(settings.FindStoreDefine("BAR"));
            Assert.IsNull(settings.FindStoreDefine(""));
        }

        [Test]
        public void GetPlatform_MapsRuntimePlatforms()
        {
            Assert.AreEqual(SavePlatform.Mobile, SaveSettings.GetPlatform(RuntimePlatform.Android));
            Assert.AreEqual(SavePlatform.Mobile, SaveSettings.GetPlatform(RuntimePlatform.IPhonePlayer));
            Assert.AreEqual(SavePlatform.Desktop, SaveSettings.GetPlatform(RuntimePlatform.WindowsPlayer));
            Assert.AreEqual(SavePlatform.Editor, SaveSettings.GetPlatform(RuntimePlatform.WindowsEditor));
        }

        [Test]
        public void Profile_RejectsUnsafeDirectory()
        {
            editor.SaveDirectory = "../outside";
            Assert.IsFalse(editor.Validate(out _));

            editor.SaveDirectory = "Saves/Profile1";
            Assert.IsTrue(editor.Validate(out string error), error);
        }
    }

    public class PlayerPrefsStorageTests
    {
        private string rootKey;
        private PlayerPrefsSaveStorage storage;

        [SetUp]
        public void SetUp()
        {
            rootKey = "SaveToolkitTests_" + Guid.NewGuid().ToString("N");
            storage = new PlayerPrefsSaveStorage(rootKey);
        }

        [TearDown]
        public void TearDown()
        {
            storage.DeleteDirectory("");
            PlayerPrefs.DeleteKey(rootKey + "/__index");
            PlayerPrefs.Save();
        }

        [Test]
        public void SaveManager_RoundTripsThroughPlayerPrefs()
        {
            var manager = new SaveManager(
                new SaveOptions { EncryptionPassphrase = "", EnableDebugLogs = false, SlotCount = 2 },
                storage,
                new TestLogger()
            );

            manager.Save("player", new TestData { Level = 3 }, out _);
            manager.Save("player", new TestData { Level = 4 }, out _);

            Assert.IsTrue(manager.TryLoad("player", out TestData loaded, out string error), error);
            Assert.AreEqual(4, loaded.Level);

            Assert.IsTrue(manager.TryGetSlotInfo(0, out SaveSlotInfo info, out _));
            CollectionAssert.Contains(info.Keys, "player");

            Assert.IsTrue(manager.DeleteSlot(0, out _));
            Assert.IsFalse(manager.Exists("player", out _));
        }

        [Test]
        public void CorruptedPrefsValue_RecoversFromBackup()
        {
            var manager = new SaveManager(new SaveOptions { EnableDebugLogs = false }, storage, new TestLogger());
            manager.Save("player", new TestData { Level = 1 }, out _);
            manager.Save("player", new TestData { Level = 2 }, out _);

            // Truncated / hand-edited value: not valid Base64 any more.
            PlayerPrefs.SetString(rootKey + "/slot_0/player.sav", "STK2:%%%not-base64%%%");

            Assert.IsTrue(manager.TryLoad("player", out TestData loaded, out string note));
            Assert.AreEqual(1, loaded.Level);
            StringAssert.Contains("Recovered", note);
        }

        [Test]
        public void LegacyPlainJsonValue_IsReadable()
        {
            PlayerPrefs.SetString(rootKey + "/player", "{\"Level\":8}");

            var manager = new SaveManager(new SaveOptions(), storage, new TestLogger());

            Assert.IsTrue(manager.TryLoad("player", out TestData loaded, out string error), error);
            Assert.AreEqual(8, loaded.Level);
        }
    }
}
