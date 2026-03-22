using System;
using System.Collections.Generic;
using Hlight.Structures.CompositeTask.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Hlight.Structures.CompositeTask.Editor
{
    public class TaskTreeSerializationBinder : ISerializationBinder
    {
        private const string FULL_NAME_FORMAT = "_{0}";
        private readonly HashSet<string> _allowedTypes;

        public TaskTreeSerializationBinder(TaskDefinitionDatabase database)
        {
            _allowedTypes = new HashSet<string>
            {
                nameof(CompositeTaskNode),
                nameof(MonoTaskNode)
            };

            if (database?.entries != null)
            {
                foreach (var entry in database.entries)
                {
                    var type = entry?.script?.GetClass();
                    if (type != null)
                        _allowedTypes.Add(entry.useFullNameWhenSerializationBinding ?
                            string.Format(FULL_NAME_FORMAT, type.FullName) :
                            type.Name);
                }
            }
        }

        public Type BindToType(string assemblyName, string typeName)
        {
            if (!_allowedTypes.Contains(typeName))
                throw new JsonSerializationException(
                    $"Type '{typeName}' is not allowed for deserialization.");
            if (typeName.StartsWith('_')) typeName = typeName[1..];

            return Type.GetType($"{typeName}, {assemblyName}");
        }

        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            assemblyName = serializedType.Assembly.FullName;
            typeName = _allowedTypes.Contains(serializedType.Name) ?
                serializedType.Name : string.Format(FULL_NAME_FORMAT, serializedType.FullName);
        }
    }
}