using System;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Abstract event source for <see cref="WaitEventNode"/>.
    /// Subclass and call <see cref="Fire"/> when your event occurs.
    /// </summary>
    public abstract class TaskEventSource : MonoBehaviour
    {
        public event Action Fired;
        protected void Fire() => Fired?.Invoke();
    }
}
