using System.Collections.Generic;
using System.IO;
using Hlight.Structures.CompositeTask.Runtime;
using Hlight.Structures.CompositeTask.Runtime.Blueprint;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Editor
{
    /// <summary>
    /// Custom Inspector for TaskTree.
    /// Sections: Import/Export (JSON file) | Root Info | Runtime Controls.
    /// </summary>
    [CustomEditor(typeof(TaskTree))]
    public class TaskTreeEditor : UnityEditor.Editor
    {
        private TextAsset _importJson;
        private DefaultAsset _exportFolder;
        private bool _importExportFoldout;

        /// <summary>Override in subclass editors to supply presets for leaf instantiation.</summary>
        protected virtual IReadOnlyList<TaskNodePreset> GetPresets(TaskTree tree) => null;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var tree = (TaskTree)target;

            EditorGUILayout.Space(4);
            DrawRootInfo(tree);

            EditorGUILayout.Space(4);
            DrawImportExport(tree);

            // Only show runtime controls for scene instances, not prefab assets
            if (!Application.isPlaying) return;
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) return;

            EditorGUILayout.Space(4);
            DrawRuntimeControls(tree);
        }

        // ── Root Info ──────────────────────────────────────────────────

        private static void DrawRootInfo(TaskTree tree)
        {
            var root = tree.Root;
            if (root != null)
                EditorGUILayout.HelpBox(
                    $"Root: {root.gameObject.name} ({root.GetType().Name})",
                    MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "No root TaskNode found. Add a TaskNode child or assign Root Override.",
                    MessageType.Warning);
        }

        // ── Import / Export ────────────────────────────────────────────

        private void DrawImportExport(TaskTree tree)
        {
            _importExportFoldout = EditorGUILayout.Foldout(
                _importExportFoldout, "Import / Export", true, EditorStyles.foldoutHeader);
            if (!_importExportFoldout) return;

            EditorGUI.indentLevel++;

            _importJson = (TextAsset)EditorGUILayout.ObjectField(
                "Import JSON", _importJson, typeof(TextAsset), false);

            _exportFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Export Folder", _exportFolder, typeof(DefaultAsset), false);

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = _importJson != null;
            if (GUILayout.Button("Import")) ImportFromJson(tree);
            GUI.enabled = true;

            GUI.enabled = tree.Root != null;
            if (GUILayout.Button("Export")) ExportToJson(tree);
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }

        private void ImportFromJson(TaskTree tree)
        {
            var json = _importJson.text;
            if (string.IsNullOrWhiteSpace(json))
            { EditorUtility.DisplayDialog("Import", "JSON file is empty.", "OK"); return; }

            var blueprint = BlueprintJsonParser.Parse(json);
            if (blueprint?.root == null)
            { EditorUtility.DisplayDialog("Import", "Failed to parse JSON. Check Console.", "OK"); return; }

            Undo.SetCurrentGroupName("Import TaskTree JSON");
            int group = Undo.GetCurrentGroup();

            // Destroy existing TaskNode children
            var t = tree.transform;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i);
                if (child.GetComponent<TaskNode>() != null)
                    Undo.DestroyObjectImmediate(child.gameObject);
            }

            // Build via shared TaskTreeBuilder — editor passes PrefabUtility delegate
            var rootGo = TaskTreeBuilder.BuildNode(
                blueprint.root, t, GetPresets(tree), EditorInstantiatePrefab);

            if (rootGo != null)
            {
                Undo.RegisterCreatedObjectUndo(rootGo, "Import TaskTree JSON");

                // Auto-assign root override so it's explicit in inspector
                var rootNode = rootGo.GetComponent<TaskNode>();
                if (rootNode != null)
                {
                    var so = new SerializedObject(tree);
                    so.FindProperty("_rootOverride").objectReferenceValue = rootNode;
                    so.ApplyModifiedProperties();
                }
            }

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = tree.gameObject;
        }

        private void ExportToJson(TaskTree tree)
        {
            var json = TaskTreeJsonExporter.Export(tree);
            if (json == null) return;

            var folderRelative = "Assets";
            if (_exportFolder != null)
            {
                var path = AssetDatabase.GetAssetPath(_exportFolder);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                    folderRelative = path;
            }

            var projectRoot = Application.dataPath[..^"Assets".Length];
            var fullFolder  = Path.Combine(projectRoot, folderRelative);
            Directory.CreateDirectory(fullFolder);

            var fileName = $"{tree.gameObject.name}_TaskTree.json";
            var fullPath = Path.Combine(fullFolder, fileName);

            if (File.Exists(fullPath) &&
                !EditorUtility.DisplayDialog("Overwrite?",
                    $"File already exists:\n{fullPath}\n\nOverwrite?", "Yes", "Cancel"))
                return;

            File.WriteAllText(fullPath, json);
            AssetDatabase.Refresh();
            Debug.Log($"[TaskTree] Exported to: {fullPath}");
        }

        /// <summary>Editor prefab instantiation — preserves prefab connection.</summary>
        private static GameObject EditorInstantiatePrefab(GameObject prefab, Transform parent)
            => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);

        // ── Runtime Controls ───────────────────────────────────────────

        private void DrawRuntimeControls(TaskTree tree)
        {
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

            var root = tree.Root;
            if (root != null)
            {
                var statusColor = root.Status switch
                {
                    TaskStatus.Running   => Color.cyan,
                    TaskStatus.Finishing => Color.cyan,
                    TaskStatus.Completed => Color.green,
                    TaskStatus.Failed    => Color.red,
                    _                    => Color.gray
                };

                var prev = GUI.color;
                GUI.color = statusColor;
                EditorGUILayout.LabelField("Status", root.Status.ToString());
                GUI.color = prev;

                var rect = GUILayoutUtility.GetRect(18, 18, "TextField");
                EditorGUI.ProgressBar(rect, root.Progress, $"{root.Progress:P0}");
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !tree.IsRunning;
            if (GUILayout.Button("Execute")) tree.Execute();
            GUI.enabled = true;
            if (GUILayout.Button("Reset"))   tree.ResetTree();
            if (GUILayout.Button("Dispose")) tree.Dispose();
            EditorGUILayout.EndHorizontal();

            if (tree.IsRunning) Repaint();
        }
    }
}
