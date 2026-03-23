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
                    var type = entry.script.GetClass();
                    typeToName.Add(type, entry.TypeSerializationBindingName);
                }
            }

            nameToType = new();
            foreach (var item in typeToName)
            {
                nameToType.Add(item.Value, item.Key);
            }
        }

        public Type BindToType(string assemblyName, string typeName)
        {
            return nameToType[typeName];
        }

        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            assemblyName = null;
            typeName = typeToName[serializedType];
        }
    }
}