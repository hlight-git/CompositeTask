using System;
using System.Collections.Generic;
using Hlight.Structures.CompositeTask.Runtime;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Editor
{
    public class TaskTreeSerializationBinder : ISerializationBinder
    {
        private readonly Dictionary<Type, string> typeToName;
        private readonly Dictionary<string, Type> nameToType;
        private TaskDefinitionDatabase database;

        public TaskTreeSerializationBinder(TaskDefinitionDatabase database)
        {
            typeToName = new Dictionary<Type, string>
            {
                {typeof(CompositeTaskNode), nameof(CompositeTaskNode)},
                {typeof(MonoTaskNode), nameof(MonoTaskNode)},
            };

            if (database != null && database.entries != null)
            {
                foreach (var entry in database.entries)
                {
                    if (entry.script == null) continue;
                    var type = entry.script.GetClass();
                    if (type == null) continue;
                    typeToName.TryAdd(type, entry.TypeSerializationBindingName);
                }
            }

            nameToType = new();
            foreach (var item in typeToName)
            {
                nameToType.TryAdd(item.Value, item.Key);
            }
        }

        public Type BindToType(string assemblyName, string typeName)
        {
            if (nameToType.TryGetValue(typeName, out var type))
                return type;
            throw new InvalidOperationException($"Unknown task type: '{typeName}'. Ensure it is registered in TaskDefinitionDatabase.");
        }

        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            assemblyName = null;
            if (!typeToName.TryGetValue(serializedType, out typeName))
                throw new InvalidOperationException($"Unknown type: '{serializedType.FullName}'. Ensure it is registered in TaskDefinitionDatabase.");
        }
    }
}