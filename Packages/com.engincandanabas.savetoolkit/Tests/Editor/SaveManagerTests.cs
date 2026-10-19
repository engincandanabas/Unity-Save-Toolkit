
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SaveToolkit.Tests
{
    public class SaveManagerTests
    {
        private const string Key = "player";
        private const string Passphrase = "test-passphrase-123";
        private const string Salt = "test-salt-456";

        private string root;
        private FileSaveStorage storage;
        private TestLogger logger;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "SaveToolkitTests", Guid.NewGuid().ToString("N"));
            storage = new FileSaveStorage(root);
            logger = new TestLogger();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        public static SaveOptions Options(
            SaveSerializationFormat format = SaveSerializationFormat.Json,
            SaveCompressionType compression = SaveCompressionType.None,
            bool encryption = false,
            bool backup = true,
            int dataVersion = 1,
            string passphrase = Passphrase)
        {
            return new SaveOptions
            {
                Format = format,
                Compression = compression,
                EnableEncryption = encryption,
                EncryptionPassphrase = encryption ? passphrase : "",
                EncryptionSalt = encryption ? Salt : "",
                EnableBackup = backup,
                BackupCount = 2,
                DataVersion = dataVersion,
                SlotCount = 3,
                EnableDebugLogs = false
            };
        }

        internal SaveManager Create(SaveOptions options)
        {
            return new SaveManager(options, storage, logger);
        }

        private string FullPath(string relative)
        {
            return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string MainPath(int slot = 0)
        {
            return $"slot_{slot}/{Key}{SaveManager.SaveExtension}";
        }

        // ------------------------------------------------------------------
        // Round trips
        // ------------------------------------------------------------------

        [Test]
        public void RoundTrip_AllPipelineCombinations(
            [Values] SaveCompressionType compression,
            [Values] bool encryption,
            [Values] bool flushToDisk)
        {
            SaveOptions options = Options(SaveSerializationFormat.Json, compression, encryption);
            options.FlushToDisk = flushToDisk;
            SaveManager manager = Create(options);
            var data = new TestData { Level = 7, Name = "Ada", Items = new List<int> { 1, 2, 3 } };

            Assert.IsTrue(manager.Save(Key, data, out string saveError), saveError);
            Assert.IsTrue(manager.TryLoad(Key, out TestData loaded, out string loadError), loadError);

            Assert.AreEqual(7, loaded.Level);
            Assert.AreEqual("Ada", loaded.Name);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, loaded.Items);
        }

        [Test]
        public void Save_SameKeyTwice_Overwrites()
        {
            SaveManager manager = Create(Options());

            Assert.IsTrue(manager.Save(Key, new TestData { Level = 1 }, out string e1), e1);
            Assert.IsTrue(manager.Save(Key, new TestData { Level = 2 }, out string e2), e2);
            Assert.IsTrue(manager.TryLoad(Key, out TestData loaded, out _));

            Assert.AreEqual(2, loaded.Level);
        }

        [Test]
        public void Json_PrivateSetter_RoundTrips()
        {
            SaveManager manager = Create(Options());
            var data = new PrivateSetterData();
            data.SetCoins(150);

            manager.Save(Key, data, out _);
            Assert.IsTrue(manager.TryLoad(Key, out PrivateSetterData loaded, out string error), error);

            Assert.AreEqual(150, loaded.Coins);
        }

        [Test]
        public void Json_ListInitializer_IsReplacedNotAppended()
        {
            SaveManager manager = Create(Options());

            manager.Save(Key, new TestData { Items = new List<int> { 5, 6 } }, out _);
            manager.TryLoad(Key, out TestData loaded, out _);

            CollectionAssert.AreEqual(new[] { 5, 6 }, loaded.Items);
        }

        [Test]
        public void Json_UnicodeAndLargeText_RoundTrip()
        {
            SaveManager manager = Create(Options(compression: SaveCompressionType.GZip));
            string text = "Çığ öğün şeker ÜĞİŞ — 日本語 — emoji 😀🚀 " +
                          new string('x', 9000) + " 👍 son";

            manager.Save(Key, new UnicodeData { Text = text }, out _);
            Assert.IsTrue(manager.TryLoad(Key, out UnicodeData loaded, out string error), error);

            Assert.AreEqual(text, loaded.Text);
        }

        [Test]
        public void Json_LegacyFileWithBom_IsReadable()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, Key + ".json"), "{\"Level\":6}", new System.Text.UTF8Encoding(true));

            SaveManager manager = Create(Options());

            Assert.IsTrue(manager.TryLoad(Key, out TestData loaded, out string error), error);
            Assert.AreEqual(6, loaded.Level);
        }

#if !SAVETOOLKIT_MESSAGEPACK
        [Test]
        public void MessagePackProfile_WithoutModule_FailsClearly()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                Create(Options(SaveSerializationFormat.MessagePack)));

            StringAssert.Contains("MessagePack module", ex.Message);
        }
#endif

        [Test]
        public void PooledBufferWriter_GrowsAndKeepsData()
        {
            using (var writer = new PooledBufferWriter(16))
            {
                for (int i = 0; i < 5000; i++)
                    writer.Write(new[] { (byte)(i % 251) });

                Assert.AreEqual(5000, writer.WrittenCount);

                for (int i = 0; i < 5000; i++)
                    Assert.AreEqual((byte)(i % 251), writer.WrittenSpan[i]);

                writer.Clear();
                Assert.AreEqual(0, writer.WrittenCount);
            }
        }

        [Test]
        public void PooledBufferWriter_ThrowsAfterDispose()
        {
            var writer = new PooledBufferWriter();
            writer.Dispose();

            Assert.Throws<ObjectDisposedException>(() => writer.GetSpan(1));
        }

        // ------------------------------------------------------------------
        // Corruption and backups
        // ------------------------------------------------------------------

        [Test]
        public void CorruptedMain_RecoversFromBackup()
        {
            SaveManager manager = Create(Options());
            manager.Save(Key, new TestData { Level = 1 }, out _);
            manager.Save(Key, new TestData { Level = 2 }, out _);

            CorruptLastByte(MainPath());

            Assert.IsTrue(manager.TryLoad(Key, out TestData loaded, out string note));
            Assert.AreEqual(1, loaded.Level);
            StringAssert.Contains("Recovered", note);
            Assert.IsTrue(File.Exists(FullPath(MainPath() + ".corrupt")));

            Assert.IsTrue(manager.TryLoad(Key, out TestData again, out string error));
            Assert.AreEqual(1, again.Level);
            Assert.IsNull(error);
        }

        [Test]
        public void BackupRotation_KeepsConfiguredCount()
        {
            SaveManager manager = Create(Options());

            for (int i = 1; i <= 4; i++)
                manager.Save(Key, new TestData { Level = i }, out _);

            Assert.IsTrue(File.Exists(FullPath(MainPath() + ".bak1")));
            Assert.IsTrue(File.Exists(FullPath(MainPath() + ".bak2")));
            Assert.IsFalse(File.Exists(FullPath(MainPath() + ".bak3")));
        }

        [Test]
        public void SavesDeletedOutsideTheGame_AreHandled()
        {
            SaveManager manager = Create(Options());
            manager.Save(Key, new TestData { Level = 1 }, out _);
            manager.Save(Key, new TestData { Level = 2 }, out _);

            // The storage remembers these files; wipe them behind its back.
            Directory.Delete(root, true);

            Assert.IsFalse(manager.TryLoad(Key, out TestData _, out string error));
            Assert.AreEqual("Save data not found.", error);

            Assert.IsTrue(manager.Save(Key, new TestData { Level = 3 }, out error), error);
            Assert.IsTrue(manager.TryLoad(Key, out TestData loaded, out error), error);
            Assert.AreEqual(3, loaded.Level);

            File.Delete(FullPath(MainPath()));
            Assert.IsTrue(manager.Save(Key, new TestData { Level = 4 }, out error), error);
            Assert.IsTrue(manager.TryLoad(Key, out loaded, out error), error);
            Assert.AreEqual(4, loaded.Level);
        }

        [Test]
        public void TamperedEncryptedPayload_FailsAuthentication()
        {
            SaveManager manager = Create(Options(encryption: true, backup: false));
            manager.Save(Key, new TestData { Level = 9 }, out _);

            // Re-wrap a modified payload with a valid checksum so only the
            // HMAC can catch it.
            byte[] raw = File.ReadAllBytes(FullPath(MainPath()));
            byte[] payload = SaveEnvelope.Read(raw, out SaveEnvelopeHeader header);
            payload[payload.Length / 2] ^= 0xFF;
            File.WriteAllBytes(FullPath(MainPath()), SaveEnvelope.Write(header, payload));

            Assert.IsFalse(manager.TryLoad(Key, out TestData _, out string error));
            StringAssert.Contains("authentication", error);
        }

        [Test]
        public void WrongPassphrase_FailsToLoad()
        {
            Create(Options(encryption: true)).Save(Key, new TestData { Level = 3 }, out _);

            SaveManager other = Create(Options(encryption: true, passphrase: "another-passphrase"));

            Assert.IsFalse(other.TryLoad(Key, out TestData _, out _));
        }

        [Test]
        public void AllCorrupted_MovesMainAsideAndFails()
        {
            SaveManager manager = Create(Options(backup: false));
            manager.Save(Key, new TestData { Level = 1 }, out _);
            CorruptLastByte(MainPath());

            Assert.IsFalse(manager.TryLoad(Key, out TestData _, out string error));
            StringAssert.Contains("corrupted", error);
            Assert.IsFalse(manager.Exists(Key, out _));
            Assert.IsTrue(File.Exists(FullPath(MainPath() + ".corrupt")));
        }

        [Test]
        public void TypeMismatch_LeavesFilesUntouched()
        {
            SaveManager manager = Create(Options());
            manager.Save(Key, new TestData { Level = 1 }, out _);

            Assert.IsFalse(manager.TryLoad(Key, out int _, out _));
            Assert.IsTrue(File.Exists(FullPath(MainPath())));
            Assert.IsFalse(File.Exists(FullPath(MainPath() + ".corrupt")));
        }

        // ------------------------------------------------------------------
        // Versioning
        // ------------------------------------------------------------------

        [Test]
        public void Migration_LegacyJson_UpgradesInTwoSteps()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, Key + ".json"), "{\"Gold\":5}");

            SaveManager manager = Create(Options(dataVersion: 2));
            manager.RegisterMigration(new SaveMigration(Key, 0, token =>
            {
                token["Coins"] = token["Gold"];
                ((JObject)token).Remove("Gold");
                return token;
            }));
            manager.RegisterMigration(new SaveMigration(SaveMigration.AnyKey, 1, token =>
            {
                token["Level"] = 3;
                return token;
            }));

            Assert.IsTrue(manager.TryLoad(Key, out MigratedData loaded, out string error), error);
            Assert.AreEqual(5, loaded.Coins);
            Assert.AreEqual(3, loaded.Level);

            Assert.IsFalse(File.Exists(Path.Combine(root, Key + ".json")));
            SaveEnvelope.Read(File.ReadAllBytes(FullPath(MainPath())), out SaveEnvelopeHeader header);
            Assert.AreEqual(2, header.DataVersion);
        }

        [Test]
        public void NewerDataVersion_FailsWithoutTouchingFile()
        {
            Create(Options(dataVersion: 3)).Save(Key, new TestData(), out _);

            SaveManager older = Create(Options(dataVersion: 2));

            Assert.IsFalse(older.TryLoad(Key, out TestData _, out string error));
            StringAssert.Contains("newer", error);
            Assert.IsTrue(File.Exists(FullPath(MainPath())));
            Assert.IsFalse(File.Exists(FullPath(MainPath() + ".corrupt")));
        }

        [Test]
        public void DuplicateMigration_Throws()
        {
            var migrator = new SaveMigrator();
            migrator.Register(new SaveMigration(Key, 1, t => t));

            Assert.Throws<ArgumentException>(() =>
                migrator.Register(new SaveMigration(Key, 1, t => t)));
        }

        // ------------------------------------------------------------------
        // Slots
        // ------------------------------------------------------------------

        [Test]
        public void Slots_AreIsolated()
        {
            SaveManager manager = Create(Options());
            manager.Save(Key, new TestData { Level = 1 }, out _);

            Assert.IsTrue(manager.SetActiveSlot(1, out _));
            Assert.IsFalse(manager.Exists(Key, out _));

            manager.Save(Key, new TestData { Level = 2 }, out _);
            manager.SetActiveSlot(0, out _);
            manager.TryLoad(Key, out TestData loaded, out _);

            Assert.AreEqual(1, loaded.Level);
        }

        [Test]
        public void SlotInfo_ReportsKeysAndTime()
        {
            SaveManager manager = Create(Options());
            DateTime before = DateTime.UtcNow.AddSeconds(-1);
            manager.Save(Key, new TestData(), out _);
            manager.Save("settings", new TestData(), out _);

            Assert.IsTrue(manager.TryGetSlotInfo(0, out SaveSlotInfo info, out string error), error);
            Assert.IsTrue(info.Exists);
            CollectionAssert.AreEquivalent(new[] { Key, "settings" }, info.Keys.ToArray());
            Assert.IsNotNull(info.LastSavedUtc);
            Assert.GreaterOrEqual(info.LastSavedUtc.Value, before);
            Assert.AreEqual(1, info.DataVersion);

            manager.TryGetSlotInfo(2, out SaveSlotInfo empty, out _);
            Assert.IsFalse(empty.Exists);
        }

        [Test]
        public void DeleteSlot_RemovesData()
        {
            SaveManager manager = Create(Options());
            manager.Save(Key, new TestData(), out _);

            Assert.IsTrue(manager.DeleteSlot(0, out _));
            Assert.IsFalse(manager.Exists(Key, out _));
        }

        [Test]
        public void Delete_RemovesMainAndBackups()
        {
            SaveManager manager = Create(Options());
            manager.Save(Key, new TestData(), out _);
            manager.Save(Key, new TestData(), out _);

            Assert.IsTrue(manager.Delete(Key, out _));
            Assert.IsFalse(manager.TryLoad(Key, out TestData _, out string error));
            Assert.AreEqual("Save data not found.", error);
        }

        [Test]
        public void SetActiveSlot_OutOfRange_Fails()
        {
            SaveManager manager = Create(Options());

            Assert.IsFalse(manager.SetActiveSlot(3, out _));
            Assert.IsFalse(manager.SetActiveSlot(-1, out _));
            Assert.AreEqual(0, manager.ActiveSlot);
        }

        [Test]
        public void InvalidKey_Fails()
        {
            SaveManager manager = Create(Options());

            Assert.IsFalse(manager.Save("../evil", new TestData(), out _));
            Assert.IsFalse(manager.Save("", new TestData(), out _));
        }

        // ------------------------------------------------------------------
        // Async
        // ------------------------------------------------------------------

        [Test]
        public void SaveAsync_Concurrent_LeavesValidFile()
        {
            SaveManager manager = Create(Options(compression: SaveCompressionType.GZip, encryption: true));

            Task<SaveResult>[] tasks = Enumerable.Range(1, 20)
                .Select(i => manager.SaveAsync(Key, new TestData { Level = i }))
                .ToArray();

            Task.WhenAll(tasks).GetAwaiter().GetResult();

            Assert.IsTrue(tasks.All(t => t.Result.Success));
            Assert.IsTrue(manager.TryLoad(Key, out TestData loaded, out string error), error);
            Assert.That(loaded.Level, Is.InRange(1, 20));
        }

        [Test]
        public void LoadAsync_ReturnsDataAndNotFound()
        {
            SaveManager manager = Create(Options());
            manager.SaveAsync(Key, new TestData { Level = 5 }).GetAwaiter().GetResult();

            SaveResult<TestData> found = manager.LoadAsync<TestData>(Key).GetAwaiter().GetResult();
            SaveResult<TestData> missing = manager.LoadAsync<TestData>("missing").GetAwaiter().GetResult();

            Assert.IsTrue(found.Success, found.Error);
            Assert.AreEqual(5, found.Data.Level);
            Assert.IsFalse(missing.Success);
            Assert.IsTrue(missing.NotFound);
        }

        // ------------------------------------------------------------------
        // ISaveable groups
        // ------------------------------------------------------------------

        [Test]
        public void SaveAll_LoadAll_RestoresSaveables()
        {
            SaveManager manager = Create(Options());
            var a = new FakeSaveable("a", 10);
            var b = new FakeSaveable("b", 20);

            Assert.IsTrue(manager.SaveAll(new ISaveable[] { a, b }, out string saveError), saveError);

            a.Value = 0;
            b.Value = 0;
            var notSaved = new FakeSaveable("c", 99);

            Assert.IsTrue(manager.LoadAll(new ISaveable[] { a, b, notSaved }, out string loadError), loadError);
            Assert.AreEqual(10, a.Value);
            Assert.AreEqual(20, b.Value);
            Assert.AreEqual(99, notSaved.Value);
        }

        [Test]
        public void SaveAllAsync_WritesSaveables()
        {
            SaveManager manager = Create(Options());
            var a = new FakeSaveable("a", 42);

            SaveResult result = manager.SaveAllAsync(new ISaveable[] { a }).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsTrue(manager.TryLoad("a", out TestData loaded, out _));
            Assert.AreEqual(42, loaded.Level);
        }

        // ------------------------------------------------------------------
        // Options
        // ------------------------------------------------------------------

        [Test]
        public void Options_EncryptionWithoutPassphrase_IsInvalid()
        {
            var options = Options();
            options.EnableEncryption = true;

            Assert.IsFalse(options.Validate(out string error));
            Assert.Throws<ArgumentException>(() => Create(options));
            StringAssert.Contains("Passphrase", error);
        }

        private void CorruptLastByte(string relativePath)
        {
            string path = FullPath(relativePath);
            byte[] bytes = File.ReadAllBytes(path);
            bytes[bytes.Length - 1] ^= 0xFF;
            File.WriteAllBytes(path, bytes);
        }
    }
}
