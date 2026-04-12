using System;
using System.Collections.Generic;
using System.Reflection;
using Hlight.Structures.CompositeTask.Runtime;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
#endif

namespace Hlight.Structures.CompositeTask.Editor
{
    /// <summary>
    /// Custom Inspector for all TaskNode subclasses.
    /// CompositeNodes show a single "Add Child" button that opens a searchable type dropdown.
    /// </summary>
    [CustomEditor(typeof(TaskNode), true)]
    public class TaskNodeEditor :
#if ODIN_INSPECTOR
    OdinEditor
#else
    UnityEditor.Editor
#endif
    {
        private TaskNodeTypeDropdown _dropdown;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var node = (TaskNode)target;

            DrawValidationWarnings(node);

            if (node is CompositeNode)
                DrawAddChildSection(node.gameObject);

            if (Application.isPlaying && PrefabStageUtility.GetCurrentPrefabStage() == null)
                DrawRuntimeStatus(node);
        }

        // ── Add Child ──────────────────────────────────────────────────

        private void DrawAddChildSection(GameObject parent)
        {
            EditorGUILayout.Space(6);

            var node = (TaskNode)target;
            bool isSequential = node is SequentialNode;
            bool isParallel   = node is ParallelNode;

            // Switch mode button (only for Sequential ↔ Parallel)
            if (isSequential || isParallel)
            {
                var switchLabel = isSequential ? "⇄  Switch to Parallel" : "⇄  Switch to Sequential";
                if (GUILayout.Button(switchLabel))
                    SwitchMode(parent, isSequential);
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.LabelField($"Children ({parent.transform.childCount})", EditorStyles.boldLabel);
            var buttonRect = GUILayoutUtility.GetRect(new GUIContent("Add Child Node"), EditorStyles.miniButton);
            if (GUI.Button(buttonRect, "Add Child Node"))
            {
                _dropdown ??= new TaskNodeTypeDropdown(type => AddChild(parent, type));
                _dropdown.SetParent(parent);
                _dropdown.Show(buttonRect);
            }
        }

        private static void SwitchMode(GameObject go, bool fromSequential)
        {
            if (fromSequential)
            {
                Undo.DestroyObjectImmediate(go.GetComponent<SequentialNode>());
                Undo.AddComponent<ParallelNode>(go);
            }
            else
            {
                Undo.DestroyObjectImmediate(go.GetComponent<ParallelNode>());
                Undo.AddComponent<SequentialNode>(go);
            }
        }

        private static void AddChild(GameObject parent, Type nodeType)
        {
            var nodeName = GetNodeDisplayName(nodeType);
            var go = new GameObject(nodeName);
            Undo.RegisterCreatedObjectUndo(go, $"Add {nodeName}");
            go.transform.SetParent(parent.transform, false);
            go.AddComponent(nodeType);
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
        }

        private static string GetNodeDisplayName(Type nodeType)
        {
            if (nodeType == typeof(SequentialNode)) return "Sequential";
            if (nodeType == typeof(ParallelNode))   return "Parallel";
            var attr = nodeType.GetCustomAttribute<DefineTaskNodeAttribute>();
            return attr?.Id ?? nodeType.Name;
        }

        // ── Validation ─────────────────────────────────────────────────

        private static readonly List<string> _warningsBuffer = new();

        private static void DrawValidationWarnings(TaskNode node)
        {
            // Orphan / invalid placement check
            var parent = node.transform.parent;
            bool valid = node.GetComponent<TaskTree>() != null
                || (parent != null && parent.GetComponent<TaskTree>() != null);

            if (!valid && parent != null)
            {
                var parentNode = parent.GetComponent<TaskNode>();
                valid = parentNode != null && parentNode.IsValidChild(node);
            }

            if (!valid)
                EditorGUILayout.HelpBox("Invalid placement: not a recognized child of parent node.", MessageType.Warning);

            // Node-specific warnings (OCP — each node type validates itself)
            _warningsBuffer.Clear();
            node.GetValidationWarnings(_warningsBuffer);
            foreach (var w in _warningsBuffer)
                EditorGUILayout.HelpBox(w, MessageType.Warning);
        }

        // ── Runtime Status ─────────────────────────────────────────────

        private void DrawRuntimeStatus(TaskNode node)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

            var statusColor = node.Status switch
            {
                TaskStatus.Running   => Color.cyan,
                TaskStatus.Finishing => Color.cyan,
                TaskStatus.Completed => Color.green,
                TaskStatus.Failed    => Color.red,
                _                            => Color.gray
            };

            var prevColor = GUI.color;
            GUI.color = statusColor;
            EditorGUILayout.LabelField("Status", node.Status.ToString());
            GUI.color = prevColor;

            var rect = GUILayoutUtility.GetRect(18, 18, "TextField");
            EditorGUI.ProgressBar(rect, node.Progress, $"{node.Progress:P0}");

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("ForceComplete"))  node.ForceComplete();
            if (GUILayout.Button("ForceImmediate")) node.ForceComplete(immediate: true);
            if (GUILayout.Button("Reset"))          node.ResetTask();
            EditorGUILayout.EndHorizontal();

            if (node.Status == TaskStatus.Running || node.Status == TaskStatus.Finishing)
                Repaint();
        }
    }

    // ── Searchable dropdown ────────────────────────────────────────────

    internal class TaskNodeTypeDropdownItem : AdvancedDropdownItem
    {
        public Type Type { get; }
        public TaskNodeTypeDropdownItem(string label, Type type) : base(label) => Type = type;
    }

    internal class TaskNodeTypeDropdown : AdvancedDropdown
    {
        private Action<Type> _onSelected;
        private GameObject _parent;

        public TaskNodeTypeDropdown(Action<Type> onSelected) : base(new AdvancedDropdownState())
        {
            _onSelected = onSelected;
            minimumSize = new Vector2(220, 250);
        }

        /// <summary>Update parent before each Show() call — inspector target may change.</summary>
        public void SetParent(GameObject parent) => _parent = parent;

        private static readonly HashSet<string> PackageAssemblies = new()
        {
            "Hlight.Structures.TaskTree.Runtime",
            "Hlight.Structures.TaskTree.Tween"
        };

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Add Child Node");

            // 1. Composites first
            root.AddChild(new TaskNodeTypeDropdownItem("Sequential Node", typeof(SequentialNode)));
            root.AddChild(new TaskNodeTypeDropdownItem("Parallel Node",   typeof(ParallelNode)));

            // 2. Classify all entries
            var pkgCategorized   = new List<(string id, Type type, string category)>();
            var pkgUncategorized = new List<(string id, Type type)>();
            var extCategorized   = new List<(string id, Type type, string category)>();
            var extUncategorized = new List<(string id, Type type)>();

            foreach (var kv in TaskNodeRegistry.AllEntries)
            {
                var type = kv.Value;
                var attr = type.GetCustomAttribute<DefineTaskNodeAttribute>();
                var category = attr?.Category;
                bool isPkg = PackageAssemblies.Contains(type.Assembly.GetName().Name);

                if (isPkg)
                {
                    if (!string.IsNullOrEmpty(category))
                        pkgCategorized.Add((kv.Key, type, category));
                    else
                        pkgUncategorized.Add((kv.Key, type));
                }
                else
                {
                    if (!string.IsNullOrEmpty(category))
                        extCategorized.Add((kv.Key, type, category));
                    else
                        extUncategorized.Add((kv.Key, type));
                }
            }

            // 3. Package categories (sorted)
            var pkgCache = new Dictionary<string, AdvancedDropdownItem>();
            pkgCategorized.Sort((a, b) => string.Compare(a.category, b.category, StringComparison.Ordinal));
            foreach (var (id, type, category) in pkgCategorized)
            {
                var parent = GetOrCreateGroup(root, pkgCache, category);
                parent.AddChild(new TaskNodeTypeDropdownItem(id, type));
            }

            // 4. Package uncategorized → "Miscellaneous" group
            if (pkgUncategorized.Count > 0)
            {
                var miscGroup = GetOrCreateGroup(root, pkgCache, "Miscellaneous");
                pkgUncategorized.Sort((a, b) => string.Compare(a.id, b.id, StringComparison.Ordinal));
                foreach (var (id, type) in pkgUncategorized)
                    miscGroup.AddChild(new TaskNodeTypeDropdownItem(id, type));
            }

            // 5. Others (external/project nodes)
            if (extCategorized.Count > 0 || extUncategorized.Count > 0)
            {
                var othersGroup = new AdvancedDropdownItem("Others");
                var extCache = new Dictionary<string, AdvancedDropdownItem>();

                extCategorized.Sort((a, b) => string.Compare(a.category, b.category, StringComparison.Ordinal));
                foreach (var (id, type, category) in extCategorized)
                {
                    var parent = GetOrCreateGroup(othersGroup, extCache, category);
                    parent.AddChild(new TaskNodeTypeDropdownItem(id, type));
                }

                extUncategorized.Sort((a, b) => string.Compare(a.id, b.id, StringComparison.Ordinal));
                foreach (var (id, type) in extUncategorized)
                    othersGroup.AddChild(new TaskNodeTypeDropdownItem(id, type));

                root.AddChild(othersGroup);
            }

            return root;
        }

        private static AdvancedDropdownItem GetOrCreateGroup(
            AdvancedDropdownItem root,
            Dictionary<string, AdvancedDropdownItem> cache,
            string path)
        {
            if (cache.TryGetValue(path, out var existing)) return existing;

            var segments = path.Split('/');
            var current = root;
            var builtPath = "";

            foreach (var segment in segments)
            {
                builtPath = builtPath.Length == 0 ? segment : builtPath + "/" + segment;

                if (!cache.TryGetValue(builtPath, out var group))
                {
                    group = new AdvancedDropdownItem(segment);
                    cache[builtPath] = group;
                    current.AddChild(group);
                }

                current = group;
            }

            return current;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is TaskNodeTypeDropdownItem typed)
                _onSelected?.Invoke(typed.Type);
        }
    }
}
