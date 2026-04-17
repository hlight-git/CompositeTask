using System;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime.Blueprint
{
    /// <summary>
    /// Maps a typeId to a prefab GameObject.
    /// When a TaskTree builds a leaf with this typeId, the prefab is instantiated
    /// instead of creating a plain empty GameObject.
    /// The prefab should already carry the matching TaskNode component (and visuals, physics, etc.).
    /// If the TaskNode component is missing on the prefab it will be added automatically.
    /// </summary>
    [Serializable]
    public class TaskNodePreset
    {
        [Tooltip("typeId as declared in [DefineTaskNode(\"...\")].")]
        public string typeId;

        [Tooltip("Prefab to instantiate for this node type.")]
        public GameObject prefab;
    }
}
