
using System;
using System.Collections.Generic;

namespace SaveToolkit
{
    public sealed class SaveSlotInfo
    {
        public int Slot;

        /// <summary>True when the slot holds at least one save.</summary>
        public bool Exists;

        /// <summary>UTC time of the most recent save in the slot. Null if unknown.</summary>
        public DateTime? LastSavedUtc;

        /// <summary>Data version of the most recently written save.</summary>
        public int DataVersion;

        public IReadOnlyList<string> Keys = Array.Empty<string>();
    }
}
