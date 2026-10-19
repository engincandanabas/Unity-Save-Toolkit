
using System;

namespace SaveToolkit
{
    /// <summary>
    /// Something the SaveController saves and loads as a group
    /// (SaveAll / LoadAll and autosave). Most users derive from
    /// <see cref="SaveableBehaviour{TState}"/> instead of implementing this.
    /// </summary>
    public interface ISaveable
    {
        string SaveKey { get; }

        Type StateType { get; }

        /// <summary>Called on the main thread. Return a plain data object.</summary>
        object CaptureState();

        /// <summary>Called on the main thread with the loaded state.</summary>
        void RestoreState(object state);
    }
}
