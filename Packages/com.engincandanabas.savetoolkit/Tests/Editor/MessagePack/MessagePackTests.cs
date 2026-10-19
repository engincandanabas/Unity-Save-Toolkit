
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace SaveToolkit.Tests
{
    public class MessagePackTests
    {
        private const string Key = "player";

        private string root;
        private FileSaveStorage storage;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "SaveToolkitTests", Guid.NewGuid().ToString("N"));
            storage = new FileSaveStorage(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private SaveManager Create(SaveOptions options)
        {
            return new SaveManager(options, storage, new TestLogger());
        }

        [Test]
        public void RoundTrip_AllPipelineCombinations(
            [Values] SaveCompressionType compression,
            [Values] bool encryption)
        {
            SaveManager manager = Create(SaveManagerTests.Options(
                SaveSerializationFormat.MessagePack, compression, encryption));
            var data = new TestData { Level = 7, Name = "Ada", Items = new List<int> { 1, 2, 3 } };

            Assert.IsTrue(manager.Save(Key, data, out string saveError), saveError);
            Assert.IsTrue(manager.TryLoad(Key, out TestData loaded, out string loadError), loadError);

            Assert.AreEqual(7, loaded.Level);
            Assert.AreEqual("Ada", loaded.Name);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, loaded.Items);
        }

        [Test]
        public void ProfileChange_JsonSaveReadableByMessagePackProfile()
        {
            Create(SaveManagerTests.Options(SaveSerializationFormat.Json))
                .Save(Key, new TestData { Level = 4 }, out _);

            SaveManager messagePack = Create(SaveManagerTests.Options(
                SaveSerializationFormat.MessagePack,
                SaveCompressionType.GZip,
                encryption: true));

            Assert.IsTrue(messagePack.TryLoad(Key, out TestData loaded, out string error), error);
            Assert.AreEqual(4, loaded.Level);
        }

        [Test]
        public void Migration_RunsThroughJToken()
        {
            Create(SaveManagerTests.Options(SaveSerializationFormat.MessagePack, dataVersion: 1))
                .Save(Key, new TestData { Level = 1, Name = "x" }, out _);

            SaveManager manager = Create(SaveManagerTests.Options(SaveSerializationFormat.MessagePack, dataVersion: 2));
            manager.RegisterMigration(new SaveMigration(Key, 1, token =>
            {
                token["Level"] = (int)token["Level"] + 10;
                return token;
            }));

            Assert.IsTrue(manager.TryLoad(Key, out TestData loaded, out string error), error);
            Assert.AreEqual(11, loaded.Level);
            Assert.AreEqual("x", loaded.Name);
        }

        [Test]
        public void Registry_FindsModule()
        {
            Assert.IsTrue(SaveSerializerRegistry.IsAvailable(SaveSerializationFormat.MessagePack));
        }
    }
}
