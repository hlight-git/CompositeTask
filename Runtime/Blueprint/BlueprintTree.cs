using System;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime.Blueprint
{
    /// <summary>
    /// Root container for a blueprint task tree. Holds a single IBlueprint root.
    /// Serializable for remote config or ScriptableObject embedding.
    /// </summary>
    [Serializable]
    public class BlueprintTree
    {
        [SerializeReference] public IBlueprint root;
    }
}
