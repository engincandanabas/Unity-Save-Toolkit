
using System.Collections.Generic;
using UnityEngine;

namespace SaveToolkit
{
    public static class SaveableRegistry
    {
        private static readonly List<ISaveable> Saveables = new List<ISaveable>();

        public static IReadOnlyList<ISaveable> All => Saveables;

        /// <summary>Returns a warning when another saveable already uses the key.</summary>
        public static string Register(ISaveable saveable)
        {
            if (saveable == null || Saveables.Contains(saveable))
                return null;

            string warning = null;

            foreach (ISaveable existing in Saveables)
            {
                if (existing.SaveKey == saveable.SaveKey)
                {
                    warning = $"Duplicate ISaveable key '{saveable.SaveKey}'. " +
                              "Both will write to the same save entry.";
                    break;
                }
            }

            Saveables.Add(saveable);
            return warning;
        }

        public static void Unregister(ISaveable saveable)
        {
            Saveables.Remove(saveable);
        }

        public static void Clear()
        {
            Saveables.Clear();
        }

        // Static state survives Enter Play Mode without domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Saveables.Clear();
        }
    }
}
