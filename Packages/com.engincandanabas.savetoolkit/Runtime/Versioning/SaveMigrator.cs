
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SaveToolkit
{
    public sealed class SaveMigrator
    {
        private readonly List<ISaveMigration> migrations = new List<ISaveMigration>();

        public void Register(ISaveMigration migration)
        {
            if (migration == null)
                throw new ArgumentNullException(nameof(migration));

            if (migration.FromVersion < 0)
                throw new ArgumentException("FromVersion cannot be negative.", nameof(migration));

            lock (migrations)
            {
                foreach (ISaveMigration existing in migrations)
                {
                    if (existing.Key == migration.Key &&
                        existing.FromVersion == migration.FromVersion)
                        throw new ArgumentException(
                            $"A migration for '{migration.Key}' from version " +
                            $"{migration.FromVersion} is already registered.");
                }

                migrations.Add(migration);
            }
        }

        public bool HasMigrations(string key, int fromVersion, int toVersion)
        {
            lock (migrations)
            {
                foreach (ISaveMigration migration in migrations)
                {
                    if (Matches(migration, key) &&
                        migration.FromVersion >= fromVersion &&
                        migration.FromVersion < toVersion)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Applies migrations step by step (v -> v + 1). Key-specific
        /// migrations run before wildcard ones at the same step.
        /// </summary>
        public JToken Migrate(string key, JToken data, int fromVersion, int toVersion)
        {
            ISaveMigration[] snapshot;
            lock (migrations)
            {
                snapshot = migrations.ToArray();
            }

            for (int version = fromVersion; version < toVersion; version++)
            {
                data = Apply(snapshot, version, key, data);
                data = Apply(snapshot, version, SaveMigration.AnyKey, data);
            }

            return data;
        }

        private static JToken Apply(
            ISaveMigration[] snapshot,
            int version,
            string key,
            JToken data)
        {
            foreach (ISaveMigration migration in snapshot)
            {
                if (migration.FromVersion != version || migration.Key != key)
                    continue;

                data = migration.Migrate(data) ?? throw new InvalidOperationException(
                    $"Migration '{migration.Key}' v{version} returned null.");
            }

            return data;
        }

        private static bool Matches(ISaveMigration migration, string key)
        {
            return migration.Key == key || migration.Key == SaveMigration.AnyKey;
        }
    }
}
