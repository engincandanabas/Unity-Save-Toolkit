
using System;
using UnityEngine;

namespace SaveToolkit
{
    /// <summary>
    /// MonoBehaviour that registers itself with <see cref="SaveableRegistry"/>
    /// while enabled. Override Capture/Restore with a plain data type.
    /// </summary>
    public abstract class SaveableBehaviour<TState> : MonoBehaviour, ISaveable
    {
        [SerializeField] private string saveKey;

        public virtual string SaveKey => saveKey;

        public Type StateType => typeof(TState);

        protected abstract TState CaptureState();

        protected abstract void RestoreState(TState state);

        object ISaveable.CaptureState()
        {
            return CaptureState();
        }

        void ISaveable.RestoreState(object state)
        {
            RestoreState((TState)state);
        }

        protected virtual void OnEnable()
        {
            string warning = SaveableRegistry.Register(this);

            if (warning != null)
                Debug.LogWarning("[SaveToolkit] " + warning, this);
        }

        protected virtual void OnDisable()
        {
            SaveableRegistry.Unregister(this);
        }
    }
}
