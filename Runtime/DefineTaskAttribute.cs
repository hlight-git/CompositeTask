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
    /// Marks a concrete ATask subclass for automatic discovery by the editor.
    /// Classes with this attribute appear in the task type dropdown.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class DefineTaskAttribute : Attribute
    {
        public string DisplayName { get; }
        public string Description { get; set; } = "";
        public TypeSerializationBindingMode BindingMode { get; set; } = TypeSerializationBindingMode.ByDisplayName;

        public DefineTaskAttribute(string displayName)
        {
            DisplayName = displayName;
        }

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
