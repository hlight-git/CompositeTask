using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Abstract condition evaluated by <see cref="ConditionalNode"/>.
    /// Assign to a Branch via the ConditionalNode inspector.
    /// </summary>
    public abstract class TaskCondition : MonoBehaviour
    {
        public abstract bool Evaluate();
    }
}
