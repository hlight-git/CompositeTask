using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime.Blueprint
{
    /// <summary>
    /// Parses a JSON string into a BlueprintTree.
    ///
    /// JSON format — the root object represents the root node:
    /// <code>
    /// {
    ///   "type": "sequential",   // "sequential" | "parallel" | &lt;typeId&gt;
    ///   "name": "Root",
    ///   "enabled": true,
    ///   "weight": 1.0,
    ///   "children": [           // only for composite types
    ///     { "type": "my-task-id", "name": "Task A" },
    ///     { "type": "parallel",  "name": "Group", "children": [...] }
    ///   ]
    /// }
    /// </code>
    /// For leaf nodes, "type" maps to the [DefineTaskNode] typeId.
    /// Fields "enabled" and "weight" are optional (default: true, 0.0).
    /// </summary>
    public static class BlueprintJsonParser
    {
        public static BlueprintTree Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogWarning("[BlueprintJsonParser] JSON is empty.");
                return null;
            }

            try
            {
                var jObj = JObject.Parse(json);
                return new BlueprintTree { root = ParseNode(jObj) };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BlueprintJsonParser] Failed to parse JSON: {ex.Message}");
                return null;
            }
        }

        private static IBlueprint ParseNode(JObject obj)
        {
            var type    = obj["type"]?.Value<string>()    ?? "sequential";
            var name    = obj["name"]?.Value<string>()    ?? "Node";
            var enabled = obj["enabled"]?.Value<bool>()   ?? true;
            var weight  = obj["weight"]?.Value<float>()   ?? 0f;

            if (IsCompositeType(type))
                return ParseComposite(obj, type, name, enabled, weight);

            return new LeafBlueprint
            {
                name       = name,
                enabled    = enabled,
                weight     = weight,
                typeId     = type,
                configJson = obj["config"]?.ToString()
            };
        }

        private static CompositeBlueprint ParseComposite(
            JObject obj, string type, string name, bool enabled, float weight)
        {
            var composite = new CompositeBlueprint
            {
                name    = name,
                enabled = enabled,
                weight  = weight,
                mode    = type == "parallel"
                    ? BlueprintExecutionMode.Parallel
                    : BlueprintExecutionMode.Sequential
            };

            var childrenToken = obj["children"] as JArray;
            if (childrenToken == null) return composite;

            foreach (var token in childrenToken)
            {
                if (token is not JObject childObj)
                {
                    Debug.LogWarning("[BlueprintJsonParser] Child token is not an object, skipping.");
                    continue;
                }

                var child = ParseNode(childObj);
                if (child != null)
                    composite.children.Add(child);
            }

            return composite;
        }

        private static bool IsCompositeType(string type)
            => type == "sequential" || type == "parallel";
    }
}
