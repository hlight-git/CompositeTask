using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Abstract condition evaluated by <see cref="ConditionalNode"/>.
    /// Subclass and implement <see cref="OnEvaluate"/>; the cached <see cref="LastResult"/>
    /// lets <see cref="ConditionalNode.Evaluation.useLastResult"/> reuse the previous outcome.
    /// </summary>
    public abstract class TaskCondition : MonoBehaviour
    {
        /// <summary>The result of the most recent <see cref="Evaluate"/> call.</summary>
        public bool LastResult { get; private set; }

        /// <summary>Evaluate now, cache the result into <see cref="LastResult"/>, and return it.</summary>
        public bool Evaluate()
        {
            LastResult = OnEvaluate();
            return LastResult;
        }

        /// <summary>Override to compute the boolean outcome. Pure: no side effects.</summary>
        protected abstract bool OnEvaluate();
    }
}
