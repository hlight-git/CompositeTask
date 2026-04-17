using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Static registry mapping DefineTaskNode TypeId strings to TaskNode concrete types.
    /// Lazy-initialized via assembly scan. Used by TaskTreeBuilder for Blueprint → hierarchy.
    /// </summary>
    public static class TaskNodeRegistry
    {
        private static Dictionary<string, Type> _typeIdToType;
        private static Dictionary<Type, string> _typeToTypeId;
        private static bool _initialized;

        public static Type GetNodeType(string typeId)
        {
            EnsureInitialized();
            return _typeIdToType.TryGetValue(typeId, out var type) ? type : null;
        }

        public static string GetTypeId(Type nodeType)
        {
            EnsureInitialized();
            return _typeToTypeId.TryGetValue(nodeType, out var id) ? id : null;
        }

        public static IEnumerable<KeyValuePair<string, Type>> AllEntries
        {
            get
            {
                EnsureInitialized();
                return _typeIdToType;
            }
        }

        /// <summary>Force re-scan. Call after domain reload if needed.</summary>
        public static void Refresh()
        {
            _initialized = false;
            EnsureInitialized();
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;

            _typeIdToType = new Dictionary<string, Type>();
            _typeToTypeId = new Dictionary<Type, string>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Skip system/Unity assemblies for performance
                var asmName = assembly.GetName().Name;
                if (asmName.StartsWith("System") || asmName.StartsWith("Unity") ||
                    asmName.StartsWith("mscorlib") || asmName.StartsWith("netstandard"))
                    continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(TaskNode).IsAssignableFrom(type)) continue;

                    var attr = type.GetCustomAttribute<DefineTaskNodeAttribute>();
                    if (attr == null) continue;

                    if (_typeIdToType.ContainsKey(attr.Id))
                    {
                        Debug.LogWarning(
                            $"[TaskNodeRegistry] Duplicate TypeId '{attr.Id}': " +
                            $"{type.FullName} conflicts with {_typeIdToType[attr.Id].FullName}. Skipping.");
                        continue;
                    }

                    _typeIdToType[attr.Id] = type;
                    _typeToTypeId[type] = attr.Id;
                }
            }

            _initialized = true;
        }
    }
}
