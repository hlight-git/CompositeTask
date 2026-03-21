using System;
using System.Collections.Generic;
using System.IO;
using Hlight.Structures.CompositeTask.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEditor;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Editor
{
    [CustomEditor(typeof(TaskTree))]
    public class TaskTreeInspector : 
#if ODIN_INSPECTOR
        Sirenix.OdinInspector.Editor.OdinEditor
#else
        UnityEditor.Editor
#endif
    {
        TextAsset _importJson;
        DefaultAsset _exportFolder;
        TaskDefinitionDatabase _taskDefinitionDatabase;

        JsonSerializerSettings GetJsonSettings()
        {
            if (_taskDefinitionDatabase == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:TaskDefinitionDatabase");
                if (guids != null && guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _taskDefinitionDatabase = AssetDatabase.LoadAssetAtPath<TaskDefinitionDatabase>(path);
                }
            }

            return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new TaskTreeSerializationBinder(_taskDefinitionDatabase),
                Formatting = Formatting.Indented,
            };
        }

        public override void OnInspectorGUI()
        {
            // Vẽ inspector mặc định cho field Root
            base.OnInspectorGUI();

            var tree = (TaskTree)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Task Tree Tools", EditorStyles.boldLabel);

            // Nút mở TaskTreeEditorWindow
            if (GUILayout.Button("Open Task Tree Editor"))
            {
                TaskTreeEditorWindow.OpenWith(tree);
            }

            EditorGUILayout.Space();
            DrawJsonImportExport(tree);
        }

        void DrawJsonImportExport(TaskTree tree)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField("JSON Import / Export", EditorStyles.boldLabel);

            _importJson = (TextAsset)EditorGUILayout.ObjectField("Import JSON", _importJson, typeof(TextAsset), false);
            _exportFolder = (DefaultAsset)EditorGUILayout.ObjectField("Export Folder", _exportFolder, typeof(DefaultAsset), false);

            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Import from TextAsset"))
            {
                ImportTreeFromJson(tree);
            }
            if (GUILayout.Button("Export to Folder"))
            {
                ExportTreeToJson(tree);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        void ExportTreeToJson(TaskTree tree)
        {
            if (tree.Root == null)
            {
                EditorUtility.DisplayDialog(
                    "Export TaskTree",
                    "Root is null. Nothing to export.",
                    "OK");
                return;
            }

            var folderRelative = "Assets";
            if (_exportFolder != null)
            {
                var path = AssetDatabase.GetAssetPath(_exportFolder);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                    folderRelative = path;
            }

            var projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            var fullFolder = Path.Combine(projectRoot, folderRelative);

            try
            {
                if (!Directory.Exists(fullFolder))
                    Directory.CreateDirectory(fullFolder);
            }
            catch (IOException e)
            {
                Debug.LogError($"Failed to create export folder: {e.Message}");
                EditorUtility.DisplayDialog(
                    "Export TaskTree",
                    "Failed to create export folder. Check Console for details.",
                    "OK");
                return;
            }

            var fileName = $"{tree.gameObject.name}_TaskTree.json";
            var fullPath = Path.Combine(fullFolder, fileName);

            var json = JsonConvert.SerializeObject(tree.Root, GetJsonSettings());
            File.WriteAllText(fullPath, json);
            Debug.Log($"TaskTree exported to: {fullPath}");
            AssetDatabase.Refresh();
        }

        void ImportTreeFromJson(TaskTree tree)
        {
            if (_importJson == null)
            {
                EditorUtility.DisplayDialog(
                    "Import TaskTree",
                    "Import TextAsset is not assigned.",
                    "OK");
                return;
            }

            var json = _importJson.text;
            if (string.IsNullOrWhiteSpace(json))
            {
                EditorUtility.DisplayDialog(
                    "Import TaskTree",
                    "ImportJson TextAsset is empty.",
                    "OK");
                return;
            }

            CompositeTaskNode newRoot;
            try
            {
                newRoot = JsonConvert.DeserializeObject<CompositeTaskNode>(json, GetJsonSettings());
            }
            catch (JsonException e)
            {
                Debug.LogError($"Failed to deserialize TaskTree JSON: {e.Message}");
                EditorUtility.DisplayDialog(
                    "Import TaskTree",
                    "Failed to deserialize JSON into a TaskTree. Check Console for details.",
                    "OK");
                return;
            }

            if (newRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "Import TaskTree",
                    "Deserialized root is null. Import aborted.",
                    "OK");
                return;
            }

            Undo.RecordObject(tree, "Import TaskTree JSON");
            tree.Root = newRoot;
            EditorUtility.SetDirty(tree);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tree.gameObject.scene);
        }
    }

    public class TaskTreeSerializationBinder : ISerializationBinder
    {
        private readonly HashSet<string> _allowedTypes;

        public TaskTreeSerializationBinder(TaskDefinitionDatabase database)
        {
            _allowedTypes = new HashSet<string>
            {
                typeof(CompositeTaskNode).FullName,
                typeof(MonoTaskNode).FullName,
                typeof(CompositeTaskNode.Child).FullName,
            };

            if (database?.entries != null)
            {
                foreach (var entry in database.entries)
                {
                    var type = entry?.script?.GetClass();
                    if (type != null)
                        _allowedTypes.Add(type.FullName);
                }
            }
        }

        public Type BindToType(string assemblyName, string typeName)
        {
            if (!_allowedTypes.Contains(typeName))
                throw new JsonSerializationException(
                    $"Type '{typeName}' is not allowed for deserialization.");

            return Type.GetType($"{typeName}, {assemblyName}");
        }

        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            assemblyName = serializedType.Assembly.FullName;
            typeName = serializedType.FullName;
        }
    }
}