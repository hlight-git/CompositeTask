using System;
using System.Collections.Generic;
using Hlight.Structures.CompositeTask.Runtime;
using Newtonsoft.Json.Serialization;

namespace Hlight.Structures.CompositeTask.Editor
{
    public class TaskTreeSerializationBinder : ISerializationBinder
    {
        private readonly Dictionary<Type, string> typeToName;
        private readonly Dictionary<string, Type> nameToType;

        public TaskTreeSerializationBinder()
        {
            typeToName = new Dictionary<Type, string>
            {
                {typeof(CompositeTaskNode), nameof(CompositeTaskNode)},
                {typeof(MonoTaskNode), nameof(MonoTaskNode)},
            };

            foreach (var entry in TaskDefinitionRegistry.Entries)
            {
                typeToName.TryAdd(entry.Type, entry.BindingName);
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
            throw new InvalidOperationException($"Unknown task type: '{typeName}'. Ensure it has a [TaskDefinition] attribute.");
        }

        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            assemblyName = null;
            if (!typeToName.TryGetValue(serializedType, out typeName))
                throw new InvalidOperationException($"Unknown type: '{serializedType.FullName}'. Ensure it has a [TaskDefinition] attribute.");
        }
    }
}
