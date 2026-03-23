// =============================================================================
//  TaskDefinitionDatabaseEditor.cs
//  Custom Inspector cho TaskDefinitionDatabase ScriptableObject.
//  Vẽ trực tiếp vào Inspector — không cần EditorWindow riêng.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Hlight.Structures.CompositeTask.Runtime;
using UnityEditor;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Editor
{
    [CustomEditor(typeof(TaskDefinitionDatabase))]
    public class TaskDefinitionDatabaseEditor : UnityEditor.Editor
    {
        // Add entry form state
        Type[]   _availableTypes   = Array.Empty<Type>();
        string[] _availableOptions = { "None" };
        int      _selectedTypeIndex;
        string   _newDisplayName = "";
        string   _newDescription = "";

        public override void OnInspectorGUI()
        {
            var db = (TaskDefinitionDatabase)target;
            db.entries ??= new List<TaskDefinitionDatabase.Entry>();

            serializedObject.Update();

            // ── Existing entries ──
            EditorGUILayout.LabelField("Registered Task Definitions", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            var entriesProp = serializedObject.FindProperty("entries");
            if (entriesProp != null && entriesProp.arraySize > 0)
            {
                for (int i = 0; i < entriesProp.arraySize; i++)
                {
                    var entryProp = entriesProp.GetArrayElementAtIndex(i);
                    var entry = db.entries[i];
                    string typeName = entry?.script != null
                        ? (entry.script.GetClass()?.Name ?? "?")
                        : "(missing script)";
                    string label = !string.IsNullOrEmpty(entry?.displayName)
                        ? $"{entry.displayName} ({typeName})"
                        : typeName;

                    EditorGUILayout.BeginHorizontal();

                    entryProp.isExpanded = EditorGUILayout.Foldout(entryProp.isExpanded, label, true);

                    // Delete button
                    var oldColor = GUI.color;
                    GUI.color = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        Undo.RecordObject(db, "Remove Entry");
                        db.entries.RemoveAt(i);
                        EditorUtility.SetDirty(db);
                        GUI.color = oldColor;
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                    GUI.color = oldColor;

                    EditorGUILayout.EndHorizontal();

                    if (entryProp.isExpanded)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("displayName"));
                        EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("script"));
                        EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("typeSerializationBindingMode"),
                            new GUIContent("Serialization Binding Mode"));
                        EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("description"));
                        EditorGUI.indentLevel--;
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No entries. Add ITaskDefinition types below.", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();

            // ── Add New Entry ──
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Add New Entry", EditorStyles.boldLabel);

            DrawAddEntryForm(db);

            // ── Export ──
            EditorGUILayout.Space(8);
            if (GUILayout.Button("Export Task Definitions JSON", GUILayout.Height(22)))
                ExportToJson(db);
        }

        void DrawAddEntryForm(TaskDefinitionDatabase db)
        {
            // Popup rect — scan types on click
            EditorGUILayout.LabelField("ITaskDefinition Type", EditorStyles.miniBoldLabel);
            var popupRect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());

            var e = Event.current;
            if (e.type == EventType.MouseDown && popupRect.Contains(e.mousePosition))
            {
                _availableTypes = FindAvailableDefinitionTypes(db.entries);
                _availableOptions = new string[_availableTypes.Length + 1];
                _availableOptions[0] = "None";
                for (int i = 0; i < _availableTypes.Length; i++)
                    _availableOptions[i + 1] = _availableTypes[i].FullName;
                _selectedTypeIndex = Mathf.Clamp(_selectedTypeIndex, 0, _availableOptions.Length - 1);
            }

            if (_availableOptions == null || _availableOptions.Length == 0)
            {
                _availableOptions = new[] { "None" };
                _selectedTypeIndex = 0;
            }

            int newIndex = EditorGUI.Popup(popupRect, "Type", _selectedTypeIndex, _availableOptions);
            if (newIndex != _selectedTypeIndex)
            {
                _selectedTypeIndex = newIndex;
                if (_selectedTypeIndex > 0 && _availableTypes != null &&
                    _selectedTypeIndex - 1 < _availableTypes.Length)
                {
                    var t = _availableTypes[_selectedTypeIndex - 1];
                    if (t != null) _newDisplayName = t.Name;
                }
            }

            EditorGUILayout.Space(4);
            _newDisplayName = EditorGUILayout.TextField("Display Name", _newDisplayName);
            EditorGUILayout.LabelField("Description");
            _newDescription = EditorGUILayout.TextArea(_newDescription, GUILayout.MinHeight(40));

            bool hasValidType = _selectedTypeIndex > 0 &&
                                _availableTypes != null &&
                                _selectedTypeIndex - 1 < _availableTypes.Length &&
                                _availableTypes[_selectedTypeIndex - 1] != null;

            using (new EditorGUI.DisabledScope(!hasValidType))
            {
                if (GUILayout.Button("Add Entry", GUILayout.Height(24)))
                {
                    var type = _availableTypes[_selectedTypeIndex - 1];
                    if (type == null) return;

                    var script = FindMonoScriptForType(type);
                    if (script == null)
                    {
                        EditorUtility.DisplayDialog("Cannot find script",
                            $"Không tìm thấy MonoScript cho type: {type.FullName}", "OK");
                        return;
                    }

                    Undo.RecordObject(db, "Add TaskDefinition Entry");
                    db.entries.Add(new TaskDefinitionDatabase.Entry
                    {
                        script      = script,
                        displayName = _newDisplayName,
                        description = _newDescription,
                    });
                    EditorUtility.SetDirty(db);
                    AssetDatabase.SaveAssets();

                    // Reset form
                    _selectedTypeIndex = 0;
                    _newDisplayName    = "";
                    _newDescription    = "";
                    _availableTypes    = Array.Empty<Type>();
                    _availableOptions  = new[] { "None" };
                }
            }
        }

        static Type[] FindAvailableDefinitionTypes(List<TaskDefinitionDatabase.Entry> entries)
        {
            var existing = new HashSet<Type>();
            if (entries != null)
                foreach (var entry in entries)
                {
                    if (entry?.script == null) continue;
                    var t = entry.script.GetClass();
                    if (t != null) existing.Add(t);
                }

            var result = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(ITaskDefinition).IsAssignableFrom(t)) continue;
                    if (existing.Contains(t)) continue;
                    result.Add(t);
                }
            }

            return result.OrderBy(t => t.Name).ToArray();
        }

        static MonoScript FindMonoScriptForType(Type type)
        {
            if (type == null) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type)
                    return script;
            }
            return null;
        }

        void ExportToJson(TaskDefinitionDatabase db)
        {
            if (db.entries == null || db.entries.Count == 0)
            {
                EditorUtility.DisplayDialog("Export", "No entries to export.", "OK");
                return;
            }

            var path = EditorUtility.SaveFilePanel("Export Task Definitions JSON",
                "", "TaskDefinitions.json", "json");
            if (string.IsNullOrEmpty(path)) return;

            var list = new List<SerializableEntry>();
            foreach (var entry in db.entries)
            {
                if (entry?.script == null) continue;
                var type = entry.script.GetClass();
                if (type == null) continue;
                list.Add(new SerializableEntry
                {
                    typeName    = $"{type.FullName}, {type.Assembly.GetName().Name}",
                    description = entry.description ?? "",
                });
            }

            var json = JsonUtility.ToJson(new SerializableEntryList { entries = list.ToArray() }, true);
            System.IO.File.WriteAllText(path, json);
            EditorUtility.RevealInFinder(path);
        }

        [Serializable] class SerializableEntry { public string typeName; public string description; }
        [Serializable] class SerializableEntryList { public SerializableEntry[] entries; }
    }
}
