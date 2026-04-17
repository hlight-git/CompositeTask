using System.Reflection;
using Hlight.Structures.CompositeTask.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Editor
{
    /// <summary>
    /// Serializes a TaskTree hierarchy into JSON symmetric with BlueprintJsonParser.
    /// </summary>
    internal static class TaskTreeJsonExporter
    {
        public static string Export(TaskTree tree)
        {
            var root = tree.Root;
            if (root == null)
            {
                Debug.LogWarning("[TaskTreeJsonExporter] No root to export.");
                return null;
            }
            return SerializeNode(root).ToString(Formatting.Indented);
        }

        private static JObject SerializeNode(TaskNode node)
        {
            var obj = new JObject();

            if (node is CompositeNode composite)
            {
                obj["type"] = node is ParallelNode ? "parallel" : "sequential";
                WriteCommonFields(obj, node);

                // Traverse transform directly — cache may be stale in edit mode
                var children = new JArray();
                for (int i = 0; i < composite.transform.childCount; i++)
                {
                    if (composite.transform.GetChild(i).TryGetComponent<TaskNode>(out var child))
                        children.Add(SerializeNode(child));
                }
                obj["children"] = children;
            }
            else
            {
                var attr = node.GetType().GetCustomAttribute<DefineTaskNodeAttribute>();
                obj["type"] = attr?.Id ?? node.GetType().Name;
                WriteCommonFields(obj, node);

                var configProp = node.GetType().GetProperty("Config",
                    BindingFlags.Public | BindingFlags.Instance);
                if (configProp != null)
                {
                    var configValue = configProp.GetValue(node);
                    if (configValue != null)
                        obj["config"] = JObject.FromObject(configValue);
                }
            }

            return obj;
        }

        private static void WriteCommonFields(JObject obj, TaskNode node)
        {
            obj["name"]    = node.gameObject.name;
            obj["enabled"] = node.gameObject.activeSelf;
            obj["weight"]  = node.Weight;
        }
    }
}
