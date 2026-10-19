
#if SAVETOOLKIT_MESSAGEPACK
using MessagePack;
#endif
using SaveToolkit;
using UnityEngine;

/// <summary>
/// Example ISaveable: its position is written by SaveController.SaveAll
/// and by autosave (pause / quit / interval), and restored by LoadAll.
/// </summary>
public sealed class SaveableTransform : SaveableBehaviour<SaveableTransform.State>
{
#if SAVETOOLKIT_MESSAGEPACK
    [MessagePackObject(true)]
#endif
    public sealed class State
    {
        public float X;
        public float Y;
        public float Z;
    }

    protected override State CaptureState()
    {
        Vector3 position = transform.position;
        return new State { X = position.x, Y = position.y, Z = position.z };
    }

    protected override void RestoreState(State state)
    {
        transform.position = new Vector3(state.X, state.Y, state.Z);
    }
}
