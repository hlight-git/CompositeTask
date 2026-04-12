using System;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Marks a TaskNode subclass for discovery by TaskNodeRegistry.
    /// Id maps to LeafBlueprint.typeId for Blueprint → MonoBehaviour building.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class DefineTaskNodeAttribute : Attribute
    {
        public string Id { get; }
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";

        public DefineTaskNodeAttribute(string id)
        {
            Id = id;
        }
    }
}
