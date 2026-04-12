using System;

namespace Hlight.Structures.CompositeTask.Runtime.Blueprint
{
    /// <summary>
    /// Blueprint node representing a leaf task. TypeId maps to a concrete TaskNode
    /// via [DefineTaskNode] attribute at build time.
    /// </summary>
    [Serializable]
    public class LeafBlueprint : IBlueprint
    {
        public string name;
        public bool enabled = true;
        public float weight;
        public string typeId;
        /// <summary>Raw JSON of the node's config. Set by BlueprintJsonParser; applied by TaskTreeBuilder.</summary>
        public string configJson;

        string IBlueprint.Name { get => name; set => name = value; }
        bool IBlueprint.Enabled { get => enabled; set => enabled = value; }
        float IBlueprint.Weight { get => weight; set => weight = value; }
    }

    /// <summary>
    /// Blueprint leaf node with typed config data. TConfig must be [Serializable]
    /// with only primitives/strings/arrays (no Unity object references).
    /// </summary>
    [Serializable]
    public class LeafBlueprint<TConfig> : LeafBlueprint where TConfig : class, new()
    {
        public TConfig config = new();
    }
}
