
using System;
using Newtonsoft.Json.Linq;

namespace SaveToolkit
{
    /// <summary>
    /// Upgrades save data from <see cref="FromVersion"/> to FromVersion + 1.
    /// Works on a JToken so it applies to both JSON and MessagePack saves.
    /// </summary>
    public interface ISaveMigration
    {
        /// <summary>Save key this migration applies to, or <see cref="SaveMigration.AnyKey"/>.</summary>
        string Key { get; }

        int FromVersion { get; }

        JToken Migrate(JToken data);
    }

    public sealed class SaveMigration : ISaveMigration
    {
        public const string AnyKey = "*";

        private readonly Func<JToken, JToken> migrate;

        public SaveMigration(string key, int fromVersion, Func<JToken, JToken> migrate)
        {
            Key = string.IsNullOrEmpty(key)
                ? throw new ArgumentException("Key is required.", nameof(key))
                : key;

            FromVersion = fromVersion;
            this.migrate = migrate ?? throw new ArgumentNullException(nameof(migrate));
        }

        public string Key { get; }

        public int FromVersion { get; }

        public JToken Migrate(JToken data)
        {
            return migrate(data);
        }
    }
}
