using System;
using System.Collections.Generic;
using Hlight.Structures.CompositeTask.Runtime;

namespace Hlight.Structures.CompositeTask.Editor
{
    /// <summary>
    /// Scans all assemblies for [TaskDefinition]-attributed ITaskDefinition types.
    /// Cached on first access; call Refresh() after domain reload if needed.
    /// </summary>
    public static class TaskDefinitionRegistry
    {
        public struct Entry
        {
            public Type Type;
            public string DisplayName;
            public string Description;
            public string BindingName;
        }

        private static List<Entry> cachedEntries;

        public static List<Entry> Entries
        {
            get
            {
                if (cachedEntries == null) Refresh();
                return cachedEntries;
            }
        }

        public static void Refresh()
        {
            cachedEntries = new List<Entry>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(ITaskDefinition).IsAssignableFrom(type)) continue;

                    var attr = (TaskDefinitionAttribute)Attribute.GetCustomAttribute(type, typeof(TaskDefinitionAttribute));
                    if (attr == null) continue;

                    cachedEntries.Add(new Entry
                    {
                        Type = type,
                        DisplayName = attr.DisplayName,
                        Description = attr.Description,
                        BindingName = attr.GetBindingName(type),
                    });
                }
            }

            cachedEntries.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
        }
    }
}
