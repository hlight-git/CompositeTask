using System.Collections.Generic;
using Hlight.Structures.CompositeTask.Runtime.Blueprint;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// TaskTree variant that maps typeIds to prefab GameObjects.
    /// When building from JSON, matching leaf nodes are instantiated from their preset prefab
    /// (preserving visuals, physics, etc.) instead of a plain empty GameObject.
    /// </summary>
    public class PresetTaskTree : TaskTree
    {
        [SerializeField, Tooltip("typeId → prefab map used when building leaves from JSON.")]
        private List<TaskNodePreset> _nodePresets = new();

        public IReadOnlyList<TaskNodePreset> NodePresets => _nodePresets;
    }
}
