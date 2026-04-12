using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>Fires when a 2D trigger is entered. Optional tag filter.</summary>
    public class TriggerEnterEventSource : TaskEventSource
    {
        [SerializeField, Tooltip("Leave empty to accept any tag.")]
        private string tagFilter;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!string.IsNullOrEmpty(tagFilter) && !other.CompareTag(tagFilter)) return;
            Fire();
        }
    }
}
