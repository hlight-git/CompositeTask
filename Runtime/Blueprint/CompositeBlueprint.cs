using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime.Blueprint
{
    /// <summary>
    /// Blueprint node representing a composite (sequential/parallel) with children.
    /// Pure data — no execution logic.
    /// </summary>
    [Serializable]
    public class CompositeBlueprint : IBlueprint
    {
        public string name;
        public bool enabled = true;
        public float weight;
        public BlueprintExecutionMode mode;
        [SerializeReference] public List<IBlueprint> children = new();

        string IBlueprint.Name { get => name; set => name = value; }
        bool IBlueprint.Enabled { get => enabled; set => enabled = value; }
        float IBlueprint.Weight { get => weight; set => weight = value; }
    }
}
