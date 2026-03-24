using System;

namespace Hlight.Structures.CompositeTask.Runtime
{
    public enum TypeSerializationBindingMode
    {
        ByDisplayName,
        ByTypeName,
        ByTypeFullName,
    }

    /// <summary>
    /// Marks an ITaskDefinition implementation for automatic discovery by the editor.
    /// Replaces manual registration in TaskDefinitionDatabase.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class TaskDefinitionAttribute : Attribute
    {
        public string DisplayName { get; }
        public string Description { get; set; } = "";
        public TypeSerializationBindingMode BindingMode { get; set; } = TypeSerializationBindingMode.ByDisplayName;

        public TaskDefinitionAttribute(string displayName)
        {
            DisplayName = displayName;
        }

        /// <summary>
        /// Returns the serialization binding name based on the configured mode.
        /// </summary>
        public string GetBindingName(Type type)
        {
            return BindingMode switch
            {
                TypeSerializationBindingMode.ByDisplayName => DisplayName,
                TypeSerializationBindingMode.ByTypeName => type.Name,
                TypeSerializationBindingMode.ByTypeFullName => type.FullName,
                _ => DisplayName,
            };
        }
    }
}
