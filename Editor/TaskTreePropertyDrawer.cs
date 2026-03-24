// =============================================================================
//  TaskTreePropertyDrawer.cs
//  PropertyDrawer cho TaskTree (pure serializable class).
//  Vẽ inline trong Unity Inspector: Import/Export → Hierarchy → Inspector.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hlight.Structures.CompositeTask.Runtime;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Editor
{
    [CustomPropertyDrawer(typeof(TaskTree))]
    public class TaskTreePropertyDrawer : PropertyDrawer
    {
        // ══════════════════════════════════════════════════════════════════
        //  CONSTANTS
        // ══════════════════════════════════════════════════════════════════

        #region Constants

        const float RowHeight       = 20f;
        const float IndentStep      = 14f;
        const float FoldoutW        = 14f;
        const float StatusDotW      = 14f;
        const float BadgeW          = 38f;
        const float BadgePadding    = 2f;
        const float RowPadLeft      = 4f;
        internal const float SearchHeight = 20f;
        internal const float HierarchyHeight = 200f;
        const float DragThreshSq    = 100f; // 10px threshold

        static readonly Color HierarchyBg        = new(0.19f, 0.19f, 0.19f);
        static readonly Color SelectionHighlight  = new(0.24f, 0.49f, 0.91f, 0.85f);
        static readonly Color HoverHighlight      = new(0.3f, 0.3f, 0.3f, 0.4f);
        static readonly Color DropLineColor       = new(0.35f, 0.8f, 1f);
        static readonly Color DividerColor        = new(0.1f, 0.1f, 0.1f);
        internal static readonly Color StatusRunning       = new(0.2f, 0.8f, 1f);
        internal static readonly Color StatusCompleted     = new(0.2f, 0.85f, 0.3f);
        internal static readonly Color StatusFailed        = new(1f, 0.25f, 0.2f);
        internal static readonly Color StatusPending       = new(0.45f, 0.45f, 0.45f);
        static readonly Color DisabledTextColor   = new(0.5f, 0.5f, 0.5f);
        static readonly Color ErrorTextColor      = new(1f, 0.25f, 0.25f);
        static readonly Color DeleteBtnColor      = new(1f, 0.4f, 0.4f);
        static readonly Color FoldoutArrowColor   = new(0.8f, 0.8f, 0.8f, 0.8f);

        const string RenameControlName = "RenameField";

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  DRAWER STATE (per-property instance)
        // ══════════════════════════════════════════════════════════════════

        #region DrawerState

        // Foldout IDs
        internal const int FoldoutImportExport = 0;
        internal const int FoldoutHierarchy    = 1;
        internal const int FoldoutInspector    = 2;

        internal class DrawerState
        {
            // Foldout keys (dùng SessionState để persist qua domain reload & Odin re-wrap)
            public string foldoutKeyPrefix;

            // Import/Export
            public TextAsset importJson;
            public DefaultAsset exportFolder;

            // Hierarchy / Selection
            public ATaskNode selected;          // primary (inspector shows this)
            public HashSet<ATaskNode> multiSelected = new(); // all highlighted nodes
            public ATaskNode lastClicked;       // anchor for Shift+Click range
            public Dictionary<ATaskNode, bool> expanded = new();
            public string searchFilter = "";
            public Vector2 hierarchyScroll;
            public Rect hierarchyScrollRect;

            // Rename
            public ATaskNode renamingNode;
            public string renameBuffer;
            public bool focusRenameField;

            // Drag
            public ATaskNode draggedNode;
            public bool dragActive;
            public bool dragNeedCaptureStart;
            public Vector2 dragStartPos;       // screen coords
            public CompositeTaskNode dropParentTarget;
            public int dropInsertIndex;
            public bool dropValid;
            public bool dropAsChild;           // true = insert as child, false = insert between siblings
            public float contentLocalMouseY;   // mouseY trong content space (set inside ScrollView)
            public bool dropNeedsUpdate;       // flag: HandleDrag đã xử lý mouse event → cần update drop target

            // Clipboard (multi)
            public List<ATaskNode> clipboard = new();


            // Enabled cache
            public Dictionary<ATaskNode, bool> enabledCache = new();

            // Registry entries (cached from [TaskDefinition] attribute scan)
            public List<TaskDefinitionRegistry.Entry> RegistryEntries => TaskDefinitionRegistry.Entries;

            // Styles
            public GUIStyle labelStyle;
            public GUIStyle dimLabelStyle;
            public GUIStyle headerStyle;
            public GUIStyle sectionStyle;
            public GUIStyle renameStyle;
            public GUIStyle foldoutArrowStyle;
            public GUIStyle statusDotStyle;
            public GUIStyle nodeNameStyle;
            public bool stylesBuilt;

        }

        static readonly Dictionary<string, DrawerState> _states = new();

        static string GetStateKey(SerializedProperty property)
        {
            return property.propertyPath + "_" + property.serializedObject.targetObject.GetInstanceID();
        }

        static DrawerState GetState(SerializedProperty property)
        {
            var key = GetStateKey(property);
            if (!_states.TryGetValue(key, out var state))
            {
                state = _states[key] = new DrawerState();
                state.foldoutKeyPrefix = "TaskTreePD_" + key + "_";
            }
            return state;
        }

        internal static bool GetFoldout(DrawerState s, int id)
        {
            bool defaultVal = id == FoldoutHierarchy;
            return SessionState.GetBool(s.foldoutKeyPrefix + id, defaultVal);
        }

        internal static void SetFoldout(DrawerState s, int id, bool value)
        {
            SessionState.SetBool(s.foldoutKeyPrefix + id, value);
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  HEIGHT CALCULATION
        // ══════════════════════════════════════════════════════════════════

        #region Height

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var s = GetState(property);
            float h = EditorGUIUtility.singleLineHeight + 2; // Main foldout label

            if (!property.isExpanded) return h;

            // Import/Export foldout
            h += EditorGUIUtility.singleLineHeight + 2; // foldout header
            if (GetFoldout(s, FoldoutImportExport))
                h += EditorGUIUtility.singleLineHeight * 3 + 8; // fields + buttons

            // Hierarchy foldout
            h += EditorGUIUtility.singleLineHeight + 2; // foldout header
            if (GetFoldout(s, FoldoutHierarchy))
                h += HierarchyHeight + SearchHeight;

            // Execute button (play mode)
            if (Application.isPlaying)
                h += EditorGUIUtility.singleLineHeight + 4;

            // Inspector foldout (only when node selected)
            if (s.selected != null)
            {
                h += EditorGUIUtility.singleLineHeight + 2; // foldout header
                if (GetFoldout(s, FoldoutInspector))
                    h += CalculateInspectorHeight(s, property);
            }

            return h;
        }

        /// <summary>
        /// Tính chính xác chiều cao inspector content dựa trên SerializedProperty,
        /// thay vì dùng lastInspectorHeight (luôn trễ 1 frame).
        /// </summary>
        static float CalculateInspectorHeight(DrawerState s, SerializedProperty property)
        {
            string nodePath = FindNodePropertyPath(property, s.selected);
            if (nodePath == null) return 0;

            var so = property.serializedObject;
            var nodeProp = so.FindProperty(nodePath);
            if (nodeProp == null) return 0;

            float h = 0;
            float lineH = EditorGUIUtility.singleLineHeight + 2;
            var taskTree = GetTaskTreeFromProperty(property);
            bool isNonRoot = taskTree != null && s.selected != taskTree.root &&
                             nodePath.EndsWith(".taskNode");

            // Row 1: [Enabled + Name] hoặc [Name]
            h += lineH;

            // Row 2: SubTaskValue (non-root)
            if (isNonRoot) h += lineH;

            // Remaining properties (skip name, children, taskDefinition)
            var iter = nodeProp.Copy();
            var endProp = nodeProp.GetEndProperty();
            bool enterChildren = true;
            while (iter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iter, endProp))
            {
                enterChildren = false;
                if (iter.name == "name" || iter.name == "children" || iter.name == "taskDefinition") continue;
                h += EditorGUI.GetPropertyHeight(iter, true) + 2;
            }

            // TaskDefinition popup + child fields
            if (s.selected is MonoTaskNode mono)
            {
                h += lineH; // popup
                var tdProp = nodeProp.FindPropertyRelative("taskDefinition");
                if (tdProp != null && mono.taskDefinition != null)
                {
                    var tdIter = tdProp.Copy();
                    var tdEnd = tdProp.GetEndProperty();
                    bool tdEnter = true;
                    while (tdIter.NextVisible(tdEnter) && !SerializedProperty.EqualContents(tdIter, tdEnd))
                    {
                        tdEnter = false;
                        h += EditorGUI.GetPropertyHeight(tdIter, true) + 2;
                    }
                }
            }

            // Runtime inspector (Play Mode)
            if (Application.isPlaying)
                h += lineH * 4 + 8;

            // Nút Add Child cho CompositeTaskNode
            if (s.selected is CompositeTaskNode)
                h += EditorGUIUtility.singleLineHeight + 6;

            return h + 8;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  ONGUI
        // ══════════════════════════════════════════════════════════════════

        #region OnGUI

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var s = GetState(property);
            BuildStyles(s);

            var rootProp = property.FindPropertyRelative("root");
            var taskTree = GetTaskTreeFromProperty(property);
            var targetObj = property.serializedObject.targetObject;

            RebuildEnabledCache(s, taskTree);

            EditorGUI.BeginProperty(position, label, property);

            float y = position.y;

            // Main foldout
            var mainRect = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(mainRect, property.isExpanded, label, true);
            y += EditorGUIUtility.singleLineHeight + 2;

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            // Dùng indented rect để khớp indent level của Inspector
            EditorGUI.indentLevel++;
            var indentedRef = EditorGUI.IndentedRect(new Rect(position.x, y, position.width, 0));
            EditorGUI.indentLevel--;
            float contentX = indentedRef.x;
            float contentW = position.xMax - contentX;

            // ── Import/Export Foldout ──
            var ieHeaderRect = new Rect(contentX, y, contentW, EditorGUIUtility.singleLineHeight);
            SetFoldout(s, FoldoutImportExport, EditorGUI.Foldout(ieHeaderRect, GetFoldout(s, FoldoutImportExport), "Import / Export", true));
            y += EditorGUIUtility.singleLineHeight + 2;

            if (GetFoldout(s, FoldoutImportExport))
            {
                var ieRect = new Rect(contentX, y, contentW, EditorGUIUtility.singleLineHeight * 3 + 8);
                DrawImportExport(s, ieRect, property, taskTree);
                y += ieRect.height;
            }

            // ── Hierarchy Foldout ──
            var hHeaderRect = new Rect(contentX, y, contentW, EditorGUIUtility.singleLineHeight);
            SetFoldout(s, FoldoutHierarchy, EditorGUI.Foldout(hHeaderRect, GetFoldout(s, FoldoutHierarchy), "Hierarchy", true));
            y += EditorGUIUtility.singleLineHeight + 2;

            if (GetFoldout(s, FoldoutHierarchy))
            {
                var hierarchyRect = new Rect(contentX, y, contentW, HierarchyHeight + SearchHeight);

                // Process events BEFORE drawing
                HandleKeyboard(s, taskTree, targetObj);
                HandleDrag(s, taskTree, targetObj);

                DrawHierarchy(s, hierarchyRect, taskTree, targetObj, property);
                y += hierarchyRect.height;
            }

            // ── Execute button (play mode) ──
            if (Application.isPlaying && taskTree != null)
            {
                y += 2;
                var btnRect = new Rect(contentX, y, contentW, EditorGUIUtility.singleLineHeight);
                if (GUI.Button(btnRect, "▶  Execute"))
                    taskTree.Execute();
                y += EditorGUIUtility.singleLineHeight + 2;
            }

            // ── Inspector Foldout ──
            if (s.selected != null)
            {
                var inspFoldRect = new Rect(contentX, y, contentW, EditorGUIUtility.singleLineHeight);
                SetFoldout(s, FoldoutInspector, EditorGUI.Foldout(inspFoldRect, GetFoldout(s, FoldoutInspector), "Inspector", true));
                y += EditorGUIUtility.singleLineHeight + 2;

                if (GetFoldout(s, FoldoutInspector))
                {
                    string nodePath = FindNodePropertyPath(property, s.selected);
                    if (nodePath != null)
                    {
                        var so = property.serializedObject;
                        so.Update();
                        var nodeProp = so.FindProperty(nodePath);
                        if (nodeProp != null)
                        {
                            y = DrawNodeProperties(s, so, nodeProp, nodePath,
                                                   s.selected, taskTree, targetObj,
                                                   contentX, y, contentW);
                            if (so.ApplyModifiedProperties())
                                MarkDirty(targetObj);
                        }
                    }

                    // Runtime inspector (Play Mode)
                    if (Application.isPlaying)
                        y = DrawRuntimeInspector(s.selected, contentX, y, contentW);

                    // Nút thêm child cho CompositeTaskNode
                    if (s.selected is CompositeTaskNode comp)
                    {
                        y += 4;
                        float halfW = (contentW - 4) / 2f;
                        float lineH = EditorGUIUtility.singleLineHeight;
                        if (GUI.Button(new Rect(contentX, y, halfW, lineH), "+ Mono Task"))
                            AddMonoChild(s, comp, targetObj);
                        if (GUI.Button(new Rect(contentX + halfW + 4, y, halfW, lineH), "+ Composite Task"))
                            AddCompositeChild(s, comp, ExecutionMode.Sequential, targetObj);
                        y += lineH + 2;
                    }
                }
            }

            EditorGUI.EndProperty();
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  IMPORT / EXPORT
        // ══════════════════════════════════════════════════════════════════

        #region ImportExport

        static void DrawImportExport(DrawerState s, Rect rect, SerializedProperty property, TaskTree taskTree)
        {
            float lineH = EditorGUIUtility.singleLineHeight;
            float y = rect.y;

            s.importJson = (TextAsset)EditorGUI.ObjectField(
                new Rect(rect.x, y, rect.width, lineH),
                "Import JSON", s.importJson, typeof(TextAsset), false);
            y += lineH + 2;

            s.exportFolder = (DefaultAsset)EditorGUI.ObjectField(
                new Rect(rect.x, y, rect.width, lineH),
                "Export Folder", s.exportFolder, typeof(DefaultAsset), false);
            y += lineH + 2;

            float halfW = (rect.width - 4) / 2f;
            if (GUI.Button(new Rect(rect.x, y, halfW, lineH), "Import"))
                ImportFromJson(s, property, taskTree);
            if (GUI.Button(new Rect(rect.x + halfW + 4, y, halfW, lineH), "Export"))
                ExportToJson(s, property, taskTree);
        }

        static JsonSerializerSettings GetJsonSettings(DrawerState s)
        {
            return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new TaskTreeSerializationBinder(),
                Formatting = Formatting.Indented,
            };
        }

        internal static void ImportFromJson(DrawerState s, SerializedProperty property, TaskTree taskTree)
        {
            if (s.importJson == null)
            { EditorUtility.DisplayDialog("Import", "Import TextAsset is not assigned.", "OK"); return; }

            var json = s.importJson.text;
            if (string.IsNullOrWhiteSpace(json))
            { EditorUtility.DisplayDialog("Import", "TextAsset is empty.", "OK"); return; }

            CompositeTaskNode newRoot;
            try { newRoot = JsonConvert.DeserializeObject<CompositeTaskNode>(json, GetJsonSettings(s)); }
            catch (JsonException e)
            {
                Debug.LogError($"Failed to deserialize: {e.Message}");
                EditorUtility.DisplayDialog("Import", "Failed to deserialize JSON.", "OK");
                return;
            }

            if (newRoot == null)
            { EditorUtility.DisplayDialog("Import", "Deserialized root is null.", "OK"); return; }

            var target = property.serializedObject.targetObject;
            Undo.RecordObject(target, "Import TaskTree JSON");
            taskTree.root = newRoot;
            MarkDirty(target);
        }

        internal static void ExportToJson(DrawerState s, SerializedProperty property, TaskTree taskTree)
        {
            if (taskTree == null)
            { EditorUtility.DisplayDialog("Export", "TaskTree is null.", "OK"); return; }

            var folderRelative = "Assets";
            if (s.exportFolder != null)
            {
                var p = AssetDatabase.GetAssetPath(s.exportFolder);
                if (!string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p)) folderRelative = p;
            }

            var projectRoot = Application.dataPath[..^"Assets".Length];
            var fullFolder = Path.Combine(projectRoot, folderRelative);
            try { if (!Directory.Exists(fullFolder)) Directory.CreateDirectory(fullFolder); }
            catch (IOException e)
            {
                Debug.LogError($"Failed to create folder: {e.Message}");
                return;
            }

            var target = property.serializedObject.targetObject;
            var fileName = $"{target.name}_TaskTree.json";
            var fullPath = Path.Combine(fullFolder, fileName);
            File.WriteAllText(fullPath, JsonConvert.SerializeObject(taskTree.root, GetJsonSettings(s)));
            Debug.Log($"TaskTree exported to: {fullPath}");
            AssetDatabase.Refresh();
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  HIERARCHY
        // ══════════════════════════════════════════════════════════════════

        #region Hierarchy

        internal static void DrawHierarchy(DrawerState s, Rect rect, TaskTree taskTree, UnityEngine.Object targetObj,
                           SerializedProperty property)
        {
            EditorGUI.DrawRect(rect, HierarchyBg);

            if (taskTree == null || taskTree.root == null)
            {
                EditorGUI.LabelField(rect, "TaskTree not initialized");
                return;
            }

            // Search bar
            var searchRect = new Rect(rect.x + 2, rect.y + 1, rect.width - 4, SearchHeight - 2);
            s.searchFilter = EditorGUI.TextField(searchRect, s.searchFilter, EditorStyles.toolbarSearchField);
            float contentTop = rect.y + SearchHeight;

            // Scroll area
            var scrollRect = new Rect(rect.x, contentTop, rect.width, rect.height - SearchHeight);
            s.hierarchyScrollRect = scrollRect;

            float contentH = GetTreeContentHeight(s, taskTree.root);
            s.hierarchyScroll = GUI.BeginScrollView(scrollRect, s.hierarchyScroll,
                new Rect(0, 0, scrollRect.width - 16, contentH));

            // Bên trong ScrollView, e.mousePosition tự động ở content-local coords
            s.contentLocalMouseY = Event.current.mousePosition.y;

            float y = 0;
            DrawNodeRow(s, taskTree.root, null, -1, 0, ref y, scrollRect.width, taskTree, targetObj, property);

            // Drop target detection BÊN TRONG scroll view
            // CHỈ update khi HandleDrag đã xử lý mouse event (dropNeedsUpdate flag)
            if (s.dragActive && s.dropNeedsUpdate)
            {
                s.dropNeedsUpdate = false;
                UpdateDropTarget(s, taskTree);
            }

            GUI.EndScrollView();

            // Auto-scroll khi drag gần cạnh trên/dưới scroll view
            if (s.dragActive)
            {
                float mouseScreenY = Event.current.mousePosition.y;
                float edgeZone = 20f;       // px từ cạnh
                float scrollSpeed = 150f;   // px/s

                if (mouseScreenY < scrollRect.y + edgeZone && mouseScreenY >= scrollRect.y)
                {
                    // Gần cạnh trên → scroll lên
                    float t = 1f - (mouseScreenY - scrollRect.y) / edgeZone;
                    s.hierarchyScroll.y -= scrollSpeed * t * 0.016f; // ~60fps
                    s.hierarchyScroll.y = Mathf.Max(0, s.hierarchyScroll.y);
                    HandleUtility.Repaint();
                }
                else if (mouseScreenY > scrollRect.yMax - edgeZone && mouseScreenY <= scrollRect.yMax)
                {
                    // Gần cạnh dưới → scroll xuống
                    float t = 1f - (scrollRect.yMax - mouseScreenY) / edgeZone;
                    float maxScroll = Mathf.Max(0, contentH - scrollRect.height);
                    s.hierarchyScroll.y += scrollSpeed * t * 0.016f;
                    s.hierarchyScroll.y = Mathf.Min(maxScroll, s.hierarchyScroll.y);
                    HandleUtility.Repaint();
                }
            }

            // Context menu
            var e = Event.current;
            if (e.type == EventType.ContextClick && scrollRect.Contains(e.mousePosition))
            {
                var local = ScreenToScrollLocal(s, e.mousePosition);
                ShowContextMenu(s, HitTestNode(s, taskTree.root, local), taskTree, targetObj, property);
                e.Use();
            }

            // Deselect on empty space click
            if (e.type == EventType.MouseDown && e.button == 0 && scrollRect.Contains(e.mousePosition))
            {
                var local = ScreenToScrollLocal(s, e.mousePosition);
                if (HitTestNode(s, taskTree.root, local) == null)
                {
                    CommitRename(s, taskTree, targetObj);
                    s.selected = null;
                    s.multiSelected.Clear();
                    ClearTextFieldFocus();
                    HandleUtility.Repaint();
                }
            }

            // Drop indicator
            if (s.dragActive && s.dropValid)
                DrawDropIndicator(s, taskTree, scrollRect);
        }

        static float GetTreeContentHeight(DrawerState s, ATaskNode node)
        {
            if (node == null) return RowHeight;
            if (!IsVisibleBySearch(s, node)) return 0;
            float h = RowHeight;
            if (node is CompositeTaskNode comp && IsExpanded(s, comp) && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch?.taskNode != null)
                        h += GetTreeContentHeight(s, ch.taskNode);
            return h;
        }

        static void DrawNodeRow(DrawerState s, ATaskNode node, CompositeTaskNode parent, int indexInParent,
                         int depth, ref float y, float width, TaskTree taskTree,
                         UnityEngine.Object targetObj, SerializedProperty property)
        {
            if (!IsVisibleBySearch(s, node)) return;

            var e       = Event.current;
            var rowRect = new Rect(0, y, width, RowHeight);
            float indent = RowPadLeft + depth * IndentStep;
            bool isSelected = s.multiSelected.Contains(node);
            bool isEnabled  = IsNodeEnabled(s, node);
            bool isRoot     = taskTree.root == node;

            // Background
            if (isSelected)
                EditorGUI.DrawRect(rowRect, SelectionHighlight);
            else if (rowRect.Contains(e.mousePosition) && e.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, HoverHighlight);

            float cx = indent;

            // Foldout
            if (node is CompositeTaskNode compNode)
            {
                bool expanded = IsExpanded(s, compNode);
                var foldRect = new Rect(cx, y + 2, FoldoutW, RowHeight - 2);
                if (e.type == EventType.MouseDown && e.button == 0 && foldRect.Contains(e.mousePosition))
                {
                    SetExpanded(s, compNode, !expanded);
                    CommitRename(s, taskTree, targetObj);
                    e.Use();
                }
                if (e.type == EventType.Repaint)
                    GUI.Label(foldRect, expanded ? "▼" : "▶", s.foldoutArrowStyle);
                cx += FoldoutW;
            }
            else cx += FoldoutW;

            // Status dot (Play Mode)
            if (Application.isPlaying)
            {
                Color dot = node.Status switch
                {
                    TaskNodeStatus.Running   => StatusRunning,
                    TaskNodeStatus.Completed => StatusCompleted,
                    TaskNodeStatus.Failed    => StatusFailed,
                    _                        => StatusPending,
                };
                if (e.type == EventType.Repaint)
                {
                    s.statusDotStyle.normal.textColor = dot;
                    GUI.Label(new Rect(cx, y + 3, 12, 14), "●", s.statusDotStyle);
                }
                cx += StatusDotW;
            }

            // Badge
            string badge = node switch
            {
                CompositeTaskNode c => c.executionMode == ExecutionMode.Sequential ? "[Seq]" : "[Par]",
                MonoTaskNode        => "[Mono]",
                _                   => "[?]",
            };
            if (isRoot) badge = "[Root]";
            if (e.type == EventType.Repaint)
                GUI.Label(new Rect(cx, y, BadgeW, RowHeight), badge, s.dimLabelStyle);
            cx += BadgeW + BadgePadding;

            // Name / Rename
            float nameX = cx;
            float nameW = width - cx - 4;

            if (s.renamingNode == node)
            {
                var renameRect = new Rect(nameX, y + 1, nameW, RowHeight - 2);
                GUI.SetNextControlName(RenameControlName);
                s.renameBuffer = GUI.TextField(renameRect, s.renameBuffer, s.renameStyle);
                if (s.focusRenameField && e.type == EventType.Repaint)
                {
                    EditorGUI.FocusTextInControl(RenameControlName);
                    s.focusRenameField = false;
                }
                if (e.type == EventType.MouseDown && !renameRect.Contains(e.mousePosition))
                    CommitRename(s, taskTree, targetObj);
            }
            else if (e.type == EventType.Repaint)
            {
                if (!isEnabled || isRoot)
                    s.nodeNameStyle.normal.textColor = DisabledTextColor;
                else if (node is MonoTaskNode mono && mono.taskDefinition == null)
                    s.nodeNameStyle.normal.textColor = ErrorTextColor;
                else if (isSelected)
                    s.nodeNameStyle.normal.textColor = Color.white;
                else
                    s.nodeNameStyle.normal.textColor = s.labelStyle.normal.textColor;
                GUI.Label(new Rect(nameX, y, nameW, RowHeight), node.name ?? "", s.nodeNameStyle);
            }

            // Click
            if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
            {
                if (e.button == 0)
                {
                    if (s.renamingNode != node) CommitRename(s, taskTree, targetObj);

                    if (e.clickCount == 2 && node == s.selected)
                    {
                        StartRename(s, node);
                        e.Use();
                    }
                    else
                    {
                        bool ctrl  = e.control || e.command;
                        bool shift = e.shift;

                        if (ctrl)
                        {
                            // Ctrl+Click: toggle in/out of multi-select
                            if (s.multiSelected.Contains(node))
                            {
                                s.multiSelected.Remove(node);
                                s.selected = s.multiSelected.Count > 0 ? FirstOf(s.multiSelected) : null;
                            }
                            else
                            {
                                s.multiSelected.Add(node);
                                s.selected = node;
                            }
                            s.lastClicked = node;
                        }
                        else if (shift && s.lastClicked != null)
                        {
                            // Shift+Click: range select
                            var visible = new List<ATaskNode>();
                            BuildVisibleList(s, taskTree.root, visible);
                            int a = visible.IndexOf(s.lastClicked);
                            int b = visible.IndexOf(node);
                            if (a >= 0 && b >= 0)
                            {
                                int from = Mathf.Min(a, b);
                                int to   = Mathf.Max(a, b);
                                s.multiSelected.Clear();
                                for (int vi = from; vi <= to; vi++)
                                    s.multiSelected.Add(visible[vi]);
                                s.selected = node;
                            }
                        }
                        else if (s.multiSelected.Contains(node))
                        {
                            // Click on already-selected node in multi: keep multi, change primary
                            s.selected    = node;
                            s.lastClicked = node;
                        }
                        else
                        {
                            // Plain click: single select
                            s.multiSelected.Clear();
                            s.multiSelected.Add(node);
                            s.selected    = node;
                            s.lastClicked = node;
                        }

                        ClearTextFieldFocus();
                        // Drag tracking: DON'T set dragStartPos here (scroll-local coords).
                        // HandleDrag sẽ capture ở screen coords khi MouseDrag đầu tiên.
                        s.dragActive  = false;
                        s.dragNeedCaptureStart = true;
                        s.draggedNode = node;
                        e.Use();
                    }
                }
                else if (e.button == 1)
                {
                    // Right-click: if not in multi-select, single select
                    if (!s.multiSelected.Contains(node))
                    {
                        s.multiSelected.Clear();
                        s.multiSelected.Add(node);
                        s.selected = node;
                    }
                }
            }

            y += RowHeight;

            // Children
            if (node is CompositeTaskNode composite && IsExpanded(s, composite) && composite.children != null)
                for (int i = 0; i < composite.children.Count; i++)
                {
                    var child = composite.children[i];
                    if (child?.taskNode != null)
                        DrawNodeRow(s, child.taskNode, composite, i, depth + 1, ref y, width,
                                    taskTree, targetObj, property);
                }
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  DRAG & DROP
        // ══════════════════════════════════════════════════════════════════

        #region DragDrop

        internal static void HandleDrag(DrawerState s, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            if (s.draggedNode == null) return;
            var e = Event.current;

            // MouseUp khi chưa drag (click thường) → clear tracking
            if (!s.dragActive && e.type == EventType.MouseUp)
            { CancelDrag(s); return; }

            // Phase 1: Detect drag start
            // Không check scrollRect.Contains vì rect có thể stale (Odin coordinate shift).
            // Click đã register đúng trong DrawNodeRow. Drop validity do UpdateDropTarget xử lý.
            if (!s.dragActive && e.type == EventType.MouseDrag)
            {
                if (s.dragNeedCaptureStart)
                {
                    s.dragStartPos = e.mousePosition;
                    s.dragNeedCaptureStart = false;
                    return;
                }

                if (((Vector2)e.mousePosition - s.dragStartPos).sqrMagnitude > DragThreshSq)
                { s.dragActive = true; s.dropValid = false; }
            }

            if (!s.dragActive) return;

            // Phase 2: Active drag
            if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove)
            {
                s.dropNeedsUpdate = true; // signal DrawHierarchy to update drop target
                HandleUtility.Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                if (e.button == 0 && s.dropValid)
                    PerformDrop(s, taskTree, targetObj);
                CancelDrag(s);
                HandleUtility.Repaint();
                e.Use();
            }
        }

        static void CancelDrag(DrawerState s)
        {
            s.dragActive = false;
            s.draggedNode = null;
            s.dropValid = false;
            s.dropAsChild = false;
            s.dragNeedCaptureStart = false;
            s.dropNeedsUpdate = false;
        }

        /// <summary>
        /// Flat entry cho mỗi visible node: node, parent, index, contentY.
        /// </summary>
        struct DropSlot
        {
            public ATaskNode node;
            public CompositeTaskNode parent;
            public int indexInParent;
            public float y;
        }

        /// <summary>
        /// Build flat list of visible nodes với Y positions (khớp với DrawNodeRow).
        /// </summary>
        static void BuildDropSlots(DrawerState s, ATaskNode node, CompositeTaskNode parent, int idx,
                                    ref float y, List<DropSlot> slots)
        {
            if (node == null || !IsVisibleBySearch(s, node)) return;
            slots.Add(new DropSlot { node = node, parent = parent, indexInParent = idx, y = y });
            y += RowHeight;
            if (node is CompositeTaskNode comp && IsExpanded(s, comp) && comp.children != null)
                for (int i = 0; i < comp.children.Count; i++)
                    if (comp.children[i]?.taskNode != null)
                        BuildDropSlots(s, comp.children[i].taskNode, comp, i, ref y, slots);
        }

        /// <summary>
        /// Detect drop target. Phải gọi BÊN TRONG GUI.BeginScrollView để mouseY đúng content-local.
        /// </summary>
        /// <summary>
        /// Detect drop target. Gọi BÊN TRONG GUI.BeginScrollView.
        /// Chia mỗi row thành 3 zone:
        ///   Top 25%:    insert BEFORE (sibling)
        ///   Middle 50%: insert INTO (child) — chỉ cho CompositeTaskNode
        ///   Bottom 25%: insert AFTER (sibling)
        /// Leaf nodes: chỉ có top 50% (before) / bottom 50% (after).
        /// </summary>
        static void UpdateDropTarget(DrawerState s, TaskTree taskTree)
        {
            if (taskTree == null) return;
            float localY = s.contentLocalMouseY;
            s.dropValid = false;
            s.dropAsChild = false;

            var slots = new List<DropSlot>();
            float y = 0;
            BuildDropSlots(s, taskTree.root, null, -1, ref y, slots);
            if (slots.Count == 0) return;

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (localY < slot.y || localY >= slot.y + RowHeight) continue;
                if (s.draggedNode == slot.node) break; // can't drop on self

                bool isComp = slot.node is CompositeTaskNode;
                float relY = localY - slot.y; // 0..RowHeight within row

                // Determine zone: 0=before, 1=into, 2=after
                int zone;
                if (isComp)
                {
                    // 3 zones: top 25% = before, middle 50% = into, bottom 25% = after
                    if (relY < RowHeight * 0.25f) zone = 0;
                    else if (relY < RowHeight * 0.75f) zone = 1;
                    else zone = 2;
                }
                else
                {
                    // 2 zones: top 50% = before, bottom 50% = after
                    zone = relY < RowHeight * 0.5f ? 0 : 2;
                }

                if (zone == 0) // BEFORE
                {
                    if (slot.parent != null && !IsAncestorOrSelf(s.draggedNode, slot.parent))
                    {
                        s.dropParentTarget = slot.parent;
                        s.dropInsertIndex = slot.indexInParent;
                        s.dropValid = true;
                    }
                }
                else if (zone == 1) // INTO (composite only)
                {
                    var comp = (CompositeTaskNode)slot.node;
                    if (!IsAncestorOrSelf(s.draggedNode, comp))
                    {
                        s.dropParentTarget = comp;
                        s.dropInsertIndex = comp.children?.Count ?? 0;
                        s.dropAsChild = true;
                        s.dropValid = true;
                    }
                }
                else // AFTER
                {
                    if (slot.parent != null && !IsAncestorOrSelf(s.draggedNode, slot.parent))
                    {
                        s.dropParentTarget = slot.parent;
                        s.dropInsertIndex = slot.indexInParent + 1;
                        s.dropValid = true;
                    }
                }
                break;
            }
        }

        static void PerformDrop(DrawerState s, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            if (s.dropParentTarget == null || s.draggedNode == null) return;

            // Determine nodes to move
            var nodesToMove = s.multiSelected.Contains(s.draggedNode)
                ? new List<ATaskNode>(s.multiSelected)
                : new List<ATaskNode> { s.draggedNode };

            // Filter: can't move root, can't move ancestor of drop target
            nodesToMove.RemoveAll(n => n == null || n == taskTree.root || IsAncestorOrSelf(n, s.dropParentTarget));

            // Filter: nếu cả parent và child đều selected, chỉ giữ parent (child đi theo tự động)
            nodesToMove.RemoveAll(n => nodesToMove.Any(other => other != n && IsAncestorOrSelf(other, n)));

            if (nodesToMove.Count == 0) return;

            Undo.RegisterCompleteObjectUndo(targetObj, "Move Nodes");

            int adjustedInsert = s.dropInsertIndex;

            // Phase 1: Remove all nodes, track subTaskValues, adjust insert index
            var svMap = new Dictionary<ATaskNode, float>();
            foreach (var node in nodesToMove)
            {
                FindParent(taskTree.root, node, out var oldParent, out int oldIdx);
                if (oldParent == null || oldParent.children == null || oldIdx < 0 || oldIdx >= oldParent.children.Count) continue;

                svMap[node] = oldParent.children[oldIdx].subTaskValue;

                if (oldParent == s.dropParentTarget && oldIdx < adjustedInsert)
                    adjustedInsert--;

                oldParent.children.RemoveAt(oldIdx);
            }

            // Phase 2: Insert at adjusted position
            if (s.dropParentTarget.children == null)
                s.dropParentTarget.children = new List<CompositeTaskNode.Child>();
            adjustedInsert = Mathf.Clamp(adjustedInsert, 0, s.dropParentTarget.children.Count);

            foreach (var node in nodesToMove)
            {
                float sv = svMap.TryGetValue(node, out float v) ? v : 1f;
                s.dropParentTarget.children.Insert(adjustedInsert, new CompositeTaskNode.Child
                { subTaskValue = sv, taskNode = node });
                adjustedInsert++;
            }

            PurgeExpanded(s, taskTree);
            MarkDirty(targetObj);
        }

        static void DrawDropIndicator(DrawerState s, TaskTree taskTree, Rect scrollRect)
        {
            var slots = new List<DropSlot>();
            float y = 0;
            BuildDropSlots(s, taskTree.root, null, -1, ref y, slots);

            if (s.dropAsChild)
            {
                // "Insert as child" → highlight the target composite row
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].node == s.dropParentTarget)
                    {
                        float absY = scrollRect.y + slots[i].y - s.hierarchyScroll.y;
                        if (absY >= scrollRect.y && absY + RowHeight <= scrollRect.yMax)
                        {
                            // Draw highlight box around composite row
                            var hlRect = new Rect(scrollRect.x + 2, absY, scrollRect.width - 4, RowHeight);
                            EditorGUI.DrawRect(hlRect, new Color(0.35f, 0.8f, 1f, 0.2f));
                            // Draw border
                            EditorGUI.DrawRect(new Rect(hlRect.x, hlRect.y, hlRect.width, 1), DropLineColor);
                            EditorGUI.DrawRect(new Rect(hlRect.x, hlRect.yMax - 1, hlRect.width, 1), DropLineColor);
                            EditorGUI.DrawRect(new Rect(hlRect.x, hlRect.y, 1, hlRect.height), DropLineColor);
                            EditorGUI.DrawRect(new Rect(hlRect.xMax - 1, hlRect.y, 1, hlRect.height), DropLineColor);
                        }
                        break;
                    }
                }
            }
            else
            {
                // "Insert between siblings" → horizontal line
                float lineY = -1;

                // Tìm slot tại dropInsertIndex
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].parent == s.dropParentTarget && slots[i].indexInParent == s.dropInsertIndex)
                    { lineY = slots[i].y; break; } // line ở top of this slot (= insert before)
                }

                // Insert at end → line ở bottom of previous slot
                if (lineY < 0 && s.dropParentTarget != null)
                {
                    for (int i = slots.Count - 1; i >= 0; i--)
                    {
                        if (slots[i].parent == s.dropParentTarget &&
                            slots[i].indexInParent == s.dropInsertIndex - 1)
                        { lineY = slots[i].y + RowHeight; break; }
                    }
                    // fallback: line sau parent row
                    if (lineY < 0)
                    {
                        for (int i = 0; i < slots.Count; i++)
                        {
                            if (slots[i].node == s.dropParentTarget)
                            { lineY = slots[i].y + RowHeight; break; }
                        }
                    }
                }

                if (lineY < 0) return;
                float absY = scrollRect.y + lineY - s.hierarchyScroll.y;
                if (absY < scrollRect.y || absY > scrollRect.yMax) return;
                EditorGUI.DrawRect(new Rect(scrollRect.x + 4, absY - 1, scrollRect.width - 8, 2), DropLineColor);
            }
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  CONTEXT MENU
        // ══════════════════════════════════════════════════════════════════

        #region ContextMenu

        static void ShowContextMenu(DrawerState s, ATaskNode hitNode, TaskTree taskTree,
                             UnityEngine.Object targetObj, SerializedProperty property)
        {
            var menu = new GenericMenu();
            var target = hitNode ?? s.selected;

            if (target is CompositeTaskNode comp)
            {
                menu.AddItem(new GUIContent("Add Child/Mono Task"), false,
                    () => AddMonoChild(s, comp, targetObj));
                menu.AddItem(new GUIContent("Add Child/Composite (Sequential)"), false,
                    () => AddCompositeChild(s, comp, ExecutionMode.Sequential, targetObj));
                menu.AddItem(new GUIContent("Add Child/Composite (Parallel)"), false,
                    () => AddCompositeChild(s, comp, ExecutionMode.Parallel, targetObj));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Expand All Children"), false, () => ExpandAll(s, comp));
                menu.AddItem(new GUIContent("Collapse All Children"), false, () => CollapseAll(s, comp));
                menu.AddSeparator("");
            }

            if (target != null)
            {
                bool isRoot = target == taskTree.root;
                int multiCount = s.multiSelected.Count;
                bool isMulti = multiCount > 1;

                // Rename only for single select
                if (!isMulti)
                    menu.AddItem(new GUIContent("Rename        F2"), false, () => StartRename(s, target));
                else
                    menu.AddDisabledItem(new GUIContent("Rename        F2"));
                menu.AddSeparator("");

                // Duplicate
                if (isMulti)
                    menu.AddItem(new GUIContent($"Duplicate {multiCount} nodes  Ctrl+D"), false,
                        () => DuplicateMultipleNodes(s, taskTree, targetObj));
                else
                    menu.AddItem(new GUIContent("Duplicate     Ctrl+D"), false,
                        () => DuplicateNode(s, target, taskTree, targetObj));

                // Copy
                if (isMulti)
                    menu.AddItem(new GUIContent($"Copy {multiCount} nodes  Ctrl+C"), false,
                        () => { s.clipboard = new List<ATaskNode>(s.multiSelected); });
                else
                    menu.AddItem(new GUIContent("Copy          Ctrl+C"), false,
                        () => { s.clipboard = new List<ATaskNode> { target }; });

                // Paste
                if (s.clipboard != null && s.clipboard.Count > 0 && target is CompositeTaskNode cp)
                    menu.AddItem(new GUIContent($"Paste {s.clipboard.Count} as Child  Ctrl+V"), false,
                        () => PasteChildren(s, cp, taskTree, targetObj));
                else
                    menu.AddDisabledItem(new GUIContent("Paste as Child  Ctrl+V"));

                menu.AddSeparator("");

                // Delete
                if (isMulti)
                    menu.AddItem(new GUIContent($"Delete {multiCount} nodes  Del"), false,
                        () => DeleteMultipleNodes(s, taskTree, targetObj));
                else if (isRoot)
                    menu.AddDisabledItem(new GUIContent("Delete        Del"));
                else
                    menu.AddItem(new GUIContent("Delete        Del"), false,
                        () => DeleteNode(s, target, taskTree, targetObj));
            }

            menu.ShowAsContext();
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  KEYBOARD
        // ══════════════════════════════════════════════════════════════════

        #region Keyboard

        internal static void HandleKeyboard(DrawerState s, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            if (taskTree == null) return;
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;

            if (s.renamingNode != null)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                { CommitRename(s, taskTree, targetObj); e.Use(); }
                else if (e.keyCode == KeyCode.Escape)
                { CancelRename(s); e.Use(); }
                return;
            }

            if (EditorGUIUtility.editingTextField) return;

            bool ctrl = e.control || e.command;
            if (s.selected == null) { s.selected = taskTree.root; return; }

            var visible = new List<ATaskNode>();
            BuildVisibleList(s, taskTree.root, visible);
            int cur = visible.IndexOf(s.selected);

            if (e.keyCode == KeyCode.Delete)
            {
                if (s.multiSelected.Count > 1)
                    DeleteMultipleNodes(s, taskTree, targetObj);
                else
                    DeleteNode(s, s.selected, taskTree, targetObj);
                e.Use(); return;
            }

            if (ctrl && e.keyCode == KeyCode.D)
            {
                if (s.multiSelected.Count > 1)
                    DuplicateMultipleNodes(s, taskTree, targetObj);
                else
                    DuplicateNode(s, s.selected, taskTree, targetObj);
                e.Use(); return;
            }
            if (ctrl && e.keyCode == KeyCode.C)
            {
                s.clipboard = new List<ATaskNode>(s.multiSelected);
                if (s.clipboard.Count == 0 && s.selected != null)
                    s.clipboard.Add(s.selected);
                e.Use(); return;
            }
            if (ctrl && e.keyCode == KeyCode.V && s.selected is CompositeTaskNode cv)
            { PasteChildren(s, cv, taskTree, targetObj); e.Use(); return; }

            // F2 rename only for single selection
            if (e.keyCode == KeyCode.F2 && s.multiSelected.Count <= 1)
            { StartRename(s, s.selected); e.Use(); return; }

            if (e.alt && e.keyCode == KeyCode.LeftArrow)
            { CollapseAll(s, taskTree.root); e.Use(); return; }
            if (e.alt && e.keyCode == KeyCode.RightArrow)
            { ExpandAll(s, taskTree.root); e.Use(); return; }

            if (cur >= 0)
            {
                if (e.keyCode == KeyCode.UpArrow)
                {
                    if (cur > 0) s.selected = visible[cur - 1];
                    s.multiSelected.Clear();
                    if (s.selected != null) s.multiSelected.Add(s.selected);
                    s.lastClicked = s.selected;
                    e.Use(); return;
                }
                if (e.keyCode == KeyCode.DownArrow)
                {
                    if (cur < visible.Count - 1) s.selected = visible[cur + 1];
                    s.multiSelected.Clear();
                    if (s.selected != null) s.multiSelected.Add(s.selected);
                    s.lastClicked = s.selected;
                    e.Use(); return;
                }
            }

            if (e.keyCode == KeyCode.LeftArrow)
            {
                if (s.selected is CompositeTaskNode comp && IsExpanded(s, comp))
                    SetExpanded(s, comp, false);
                else { FindParent(taskTree.root, s.selected, out var p, out _); if (p != null) s.selected = p; }
                s.multiSelected.Clear();
                if (s.selected != null) s.multiSelected.Add(s.selected);
                e.Use(); return;
            }
            if (e.keyCode == KeyCode.RightArrow)
            {
                if (s.selected is CompositeTaskNode comp)
                {
                    if (!IsExpanded(s, comp)) SetExpanded(s, comp, true);
                    else if (comp.children?.Count > 0 && comp.children[0].taskNode != null)
                        s.selected = comp.children[0].taskNode;
                }
                s.multiSelected.Clear();
                if (s.selected != null) s.multiSelected.Add(s.selected);
                e.Use();
            }
        }

        static void BuildVisibleList(DrawerState s, ATaskNode node, List<ATaskNode> list)
        {
            if (node == null || !IsVisibleBySearch(s, node)) return;
            list.Add(node);
            if (node is CompositeTaskNode comp && IsExpanded(s, comp) && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch?.taskNode != null)
                        BuildVisibleList(s, ch.taskNode, list);
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  RENAME
        // ══════════════════════════════════════════════════════════════════

        #region Rename

        static void StartRename(DrawerState s, ATaskNode node)
        {
            s.renamingNode = node;
            s.renameBuffer = node.name ?? "";
            s.focusRenameField = true;
        }

        static void CommitRename(DrawerState s, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            if (s.renamingNode == null) return;
            var trimmed = s.renameBuffer?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                Undo.RegisterCompleteObjectUndo(targetObj, "Rename Node");
                s.renamingNode.name = trimmed;
                MarkDirty(targetObj);
            }
            s.renamingNode = null;
            ClearTextFieldFocus();
        }

        static void CancelRename(DrawerState s)
        {
            s.renamingNode = null;
            ClearTextFieldFocus();
        }

        static void ClearTextFieldFocus()
        {
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  MUTATIONS
        // ══════════════════════════════════════════════════════════════════

        #region Mutations

        internal static void AddMonoChild(DrawerState s, CompositeTaskNode parent, UnityEngine.Object targetObj)
        {
            Undo.RegisterCompleteObjectUndo(targetObj, "Add Mono Task");
            if (parent.children == null) parent.children = new List<CompositeTaskNode.Child>();
            parent.children.Add(new CompositeTaskNode.Child
            { subTaskValue = 1f, taskNode = new MonoTaskNode { name = "New Mono Task" } });
            SetExpanded(s, parent, true);
            MarkDirty(targetObj);
        }

        internal static void AddCompositeChild(DrawerState s, CompositeTaskNode parent, ExecutionMode mode,
                               UnityEngine.Object targetObj)
        {
            Undo.RegisterCompleteObjectUndo(targetObj, "Add Composite Task");
            if (parent.children == null) parent.children = new List<CompositeTaskNode.Child>();
            var node = new CompositeTaskNode
            { name = "New Composite", executionMode = mode, children = new List<CompositeTaskNode.Child>() };
            parent.children.Add(new CompositeTaskNode.Child { subTaskValue = 1f, taskNode = node });
            SetExpanded(s, parent, true);
            SetExpanded(s, node, true);
            MarkDirty(targetObj);
        }

        static void DeleteNode(DrawerState s, ATaskNode node, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            if (node == null || node == taskTree.root) return;
            FindParent(taskTree.root, node, out var parent, out int idx);
            if (parent == null) return;
            Undo.RegisterCompleteObjectUndo(targetObj, "Delete Node");
            parent.children.RemoveAt(idx);
            s.multiSelected.Remove(node);
            if (s.selected == node || (s.selected != null && IsDescendant(node, s.selected)))
                s.selected = null;
            PurgeExpanded(s, taskTree);
            MarkDirty(targetObj);
        }

        static void DuplicateNode(DrawerState s, ATaskNode node, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            if (node == null || node == taskTree.root) return;
            FindParent(taskTree.root, node, out var parent, out int idx);
            if (parent == null) return;
            var clone = DeepClone(node);
            Undo.RegisterCompleteObjectUndo(targetObj, "Duplicate Node");
            parent.children.Insert(idx + 1, new CompositeTaskNode.Child
            { enabled = parent.children[idx].enabled, subTaskValue = parent.children[idx].subTaskValue,
              taskNode = clone });
            clone.name = MakeUniqueSiblingName(parent, node.name);
            s.multiSelected.Clear();
            s.multiSelected.Add(clone);
            s.selected = clone;
            MarkDirty(targetObj);
        }

        /// <summary>Multi-delete: xóa tất cả nodes trong multiSelected (skip root).</summary>
        static void DeleteMultipleNodes(DrawerState s, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            var toDelete = new List<ATaskNode>(s.multiSelected);
            toDelete.RemoveAll(n => n == null || n == taskTree.root);
            if (toDelete.Count == 0) return;

            Undo.RegisterCompleteObjectUndo(targetObj, "Delete Nodes");
            foreach (var node in toDelete)
            {
                FindParent(taskTree.root, node, out var parent, out int idx);
                if (parent != null && idx >= 0 && idx < parent.children.Count)
                    parent.children.RemoveAt(idx);
            }
            s.selected = null;
            s.multiSelected.Clear();
            PurgeExpanded(s, taskTree);
            MarkDirty(targetObj);
        }

        /// <summary>Multi-duplicate: duplicate tất cả nodes trong multiSelected.</summary>
        static void DuplicateMultipleNodes(DrawerState s, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            var toDuplicate = new List<ATaskNode>(s.multiSelected);
            toDuplicate.RemoveAll(n => n == null || n == taskTree.root);
            if (toDuplicate.Count == 0) return;

            Undo.RegisterCompleteObjectUndo(targetObj, "Duplicate Nodes");
            s.multiSelected.Clear();
            foreach (var node in toDuplicate)
            {
                FindParent(taskTree.root, node, out var parent, out int idx);
                if (parent == null) continue;
                var clone = DeepClone(node);
                clone.name = MakeUniqueSiblingName(parent, node.name);
                parent.children.Insert(idx + 1, new CompositeTaskNode.Child
                { enabled = parent.children[idx].enabled, subTaskValue = parent.children[idx].subTaskValue,
                  taskNode = clone });
                s.multiSelected.Add(clone);
            }
            s.selected = s.multiSelected.Count > 0 ? FirstOf(s.multiSelected) : null;
            MarkDirty(targetObj);
        }

        static void PasteChildren(DrawerState s, CompositeTaskNode parent, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            if (s.clipboard == null || s.clipboard.Count == 0) return;
            Undo.RegisterCompleteObjectUndo(targetObj, "Paste Nodes");
            if (parent.children == null) parent.children = new List<CompositeTaskNode.Child>();

            s.multiSelected.Clear();
            foreach (var src in s.clipboard)
            {
                var clone = DeepClone(src);
                clone.name = MakeUniqueSiblingName(parent, src.name);
                parent.children.Add(new CompositeTaskNode.Child { enabled = true, subTaskValue = 1f, taskNode = clone });
                s.multiSelected.Add(clone);
            }
            SetExpanded(s, parent, true);
            s.selected = s.multiSelected.Count > 0 ? FirstOf(s.multiSelected) : null;
            MarkDirty(targetObj);
        }

        static void ExpandAll(DrawerState s, ATaskNode node)
        {
            if (node is CompositeTaskNode comp)
            {
                SetExpanded(s, comp, true);
                if (comp.children != null)
                    foreach (var ch in comp.children)
                        if (ch?.taskNode != null) ExpandAll(s, ch.taskNode);
            }
        }

        static void CollapseAll(DrawerState s, ATaskNode node)
        {
            if (node is CompositeTaskNode comp)
            {
                SetExpanded(s, comp, false);
                if (comp.children != null)
                    foreach (var ch in comp.children)
                        if (ch?.taskNode != null) CollapseAll(s, ch.taskNode);
            }
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  INSPECTOR CONTENT
        // ══════════════════════════════════════════════════════════════════

        #region Inspector

        /// <summary>
        /// Vẽ tất cả properties của node bằng rect-based EditorGUI.
        /// Thứ tự: [Enabled + Name] → SubTaskValue → các field còn lại → TaskDefinition type popup + fields.
        /// </summary>
        static float DrawNodeProperties(DrawerState s, SerializedObject so, SerializedProperty nodeProp,
                                 string nodePath, ATaskNode node, TaskTree taskTree,
                                 UnityEngine.Object targetObj, float x, float y, float w)
        {
            float lineH = EditorGUIUtility.singleLineHeight;
            bool isNonRoot = node != taskTree.root && nodePath != null && nodePath.EndsWith(".taskNode");
            string childWrapperPath = isNonRoot ? nodePath[..^".taskNode".Length] : null;

            // ── Row 1: [Enabled toggle] [Name field] ──
            if (isNonRoot)
            {
                var enabledProp = so.FindProperty(childWrapperPath + ".enabled");
                float toggleW = 18f;
                if (enabledProp != null)
                    EditorGUI.PropertyField(new Rect(x, y, toggleW, lineH), enabledProp, GUIContent.none);

                var nameProp = nodeProp.FindPropertyRelative("name");
                if (nameProp != null)
                    EditorGUI.PropertyField(new Rect(x + toggleW + 2, y, w - toggleW - 2, lineH), nameProp, GUIContent.none);
                y += lineH + 2;
            }
            else
            {
                // Root: chỉ name
                var nameProp = nodeProp.FindPropertyRelative("name");
                if (nameProp != null)
                {
                    EditorGUI.PropertyField(new Rect(x, y, w, lineH), nameProp, new GUIContent("Name"));
                    y += lineH + 2;
                }
            }

            // ── Row 2: SubTaskValue (non-root only) ──
            if (isNonRoot)
            {
                var svProp = so.FindProperty(childWrapperPath + ".subTaskValue");
                if (svProp != null)
                {
                    EditorGUI.PropertyField(new Rect(x, y, w, lineH), svProp, new GUIContent("Sub Task Value"));
                    y += lineH + 2;
                }
            }

            // ── Remaining properties (skip name, children, taskDefinition) ──
            var iter = nodeProp.Copy();
            var endProp = nodeProp.GetEndProperty();
            bool enterChildren = true;
            while (iter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iter, endProp))
            {
                enterChildren = false;
                if (iter.name == "name" || iter.name == "children" || iter.name == "taskDefinition") continue;

                float propH = EditorGUI.GetPropertyHeight(iter, true);
                EditorGUI.PropertyField(new Rect(x, y, w, propH), iter, true);
                y += propH + 2;
            }

            // ── TaskDefinition: custom type popup + property fields ──
            if (node is MonoTaskNode mono)
                y = DrawTaskDefinitionSection(s, so, nodeProp, mono, targetObj, x, y, w);

            return y;
        }

        /// <summary>
        /// Vẽ TaskDefinition section: type popup từ [TaskDefinition] registry + child fields.
        /// </summary>
        static float DrawTaskDefinitionSection(DrawerState s, SerializedObject so, SerializedProperty nodeProp,
                                                MonoTaskNode mono, UnityEngine.Object targetObj,
                                                float x, float y, float w)
        {
            float lineH = EditorGUIUtility.singleLineHeight;

            // Build type list from [TaskDefinition] attribute registry
            var types = new List<Type>();
            var names = new List<string>();
            foreach (var entry in s.RegistryEntries)
            {
                types.Add(entry.Type);
                names.Add(entry.DisplayName);
            }

            // Current index
            int curIdx = -1;
            if (mono.taskDefinition != null)
            {
                var curType = mono.taskDefinition.GetType();
                for (int i = 0; i < types.Count; i++)
                    if (types[i] == curType) { curIdx = i; break; }
            }

            // Searchable popup button
            var options = new string[names.Count + 1];
            options[0] = "None";
            for (int i = 0; i < names.Count; i++) options[i + 1] = names[i];

            int popupIdx = mono.taskDefinition == null ? 0 : (curIdx >= 0 ? curIdx + 1 : 0);
            string currentLabel = popupIdx >= 0 && popupIdx < options.Length ? options[popupIdx] : "None";

            // Label + Button layout
            float labelW = EditorGUIUtility.labelWidth;
            GUI.Label(new Rect(x, y, labelW, lineH), "Task Definition");
            var btnRect = new Rect(x + labelW, y, w - labelW, lineH);
            if (EditorGUI.DropdownButton(btnRect, new GUIContent(currentLabel), FocusType.Passive))
            {
                // Capture locals for callback closure
                var capturedTypes = types;
                var capturedMono = mono;
                var capturedTarget = targetObj;
                var capturedSo = so;

                SearchablePopup.Show(btnRect, options, popupIdx, newIdx =>
                {
                    Undo.RegisterCompleteObjectUndo(capturedTarget, "Change TaskDefinition");
                    if (newIdx == 0)
                        capturedMono.taskDefinition = null;
                    else
                    {
                        int typeIdx = newIdx - 1;
                        if (typeIdx >= 0 && typeIdx < capturedTypes.Count)
                        {
                            try { capturedMono.taskDefinition = (ITaskDefinition)Activator.CreateInstance(capturedTypes[typeIdx]); }
                            catch (Exception ex) { Debug.LogError($"Failed to create {capturedTypes[typeIdx].Name}: {ex.Message}"); }
                        }
                    }
                    MarkDirty(capturedTarget);
                    capturedSo.Update();
                });
            }
            y += lineH + 2;

            // Draw taskDefinition child properties
            var taskDefProp = nodeProp.FindPropertyRelative("taskDefinition");
            if (taskDefProp != null && mono.taskDefinition != null)
            {
                // Iterate child properties of taskDefinition (skip the managed ref type header)
                var tdIter = taskDefProp.Copy();
                var tdEnd = taskDefProp.GetEndProperty();
                bool tdEnter = true;
                while (tdIter.NextVisible(tdEnter) && !SerializedProperty.EqualContents(tdIter, tdEnd))
                {
                    tdEnter = false;
                    float propH = EditorGUI.GetPropertyHeight(tdIter, true);
                    EditorGUI.PropertyField(new Rect(x, y, w, propH), tdIter, true);
                    y += propH + 2;
                }
            }

            return y;
        }

        /// <summary>
        /// Vẽ runtime controls: Status, Progress bar, ForceComplete/ForceImmediate/Reset.
        /// Chỉ gọi khi Application.isPlaying.
        /// </summary>
        static float DrawRuntimeInspector(ATaskNode node, float x, float y, float w)
        {
            float lineH = EditorGUIUtility.singleLineHeight;

            GUI.Label(new Rect(x, y, w, lineH), "Runtime", EditorStyles.boldLabel);
            y += lineH + 2;

            Color statusColor = node.Status switch
            {
                TaskNodeStatus.Running   => StatusRunning,
                TaskNodeStatus.Completed => StatusCompleted,
                _                        => StatusPending,
            };
            var oldColor = GUI.contentColor;
            GUI.contentColor = statusColor;
            GUI.Label(new Rect(x, y, w, lineH), $"Status: {node.Status}");
            GUI.contentColor = oldColor;
            y += lineH + 2;

            EditorGUI.ProgressBar(new Rect(x, y, w, lineH), node.Progress,
                $"{node.Progress * 100f:F1}%");
            y += lineH + 4;

            float btnW = (w - 8) / 3f;
            if (GUI.Button(new Rect(x, y, btnW, lineH), "ForceComplete"))
                node.ForceComplete();
            if (GUI.Button(new Rect(x + btnW + 4, y, btnW, lineH), "ForceImmediate"))
                node.ForceComplete(true);
            if (GUI.Button(new Rect(x + 2 * (btnW + 4), y, btnW, lineH), "Reset"))
                node.Reset();
            y += lineH + 2;

            return y;
        }

        static string FindNodePropertyPath(SerializedProperty taskTreeProp, ATaskNode target)
        {
            var taskTree = GetTaskTreeFromProperty(taskTreeProp);
            if (taskTree == null) return null;

            string rootPath = taskTreeProp.propertyPath + ".root";
            if (target == taskTree.root) return rootPath;

            var parts = new List<string>();
            if (FindNodePathRec(taskTree.root, target, parts))
                return rootPath + string.Join("", parts);
            return null;
        }

        static bool FindNodePathRec(ATaskNode current, ATaskNode target, List<string> parts)
        {
            if (current is not CompositeTaskNode comp || comp.children == null) return false;
            for (int i = 0; i < comp.children.Count; i++)
            {
                var ch = comp.children[i];
                if (ch?.taskNode == null) continue;
                parts.Add($".children.Array.data[{i}].taskNode");
                if (ch.taskNode == target) return true;
                if (FindNodePathRec(ch.taskNode, target, parts)) return true;
                parts.RemoveAt(parts.Count - 1);
            }
            return false;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  DEEP CLONE
        // ══════════════════════════════════════════════════════════════════

        #region DeepClone

        static ATaskNode DeepClone(ATaskNode src)
        {
            if (src is MonoTaskNode m)
                return new MonoTaskNode
                { name = m.name, targetProgressToComplete = m.targetProgressToComplete,
                  taskDefinition = CopyTaskDef(m.taskDefinition) };
            if (src is CompositeTaskNode c)
            {
                var clone = new CompositeTaskNode
                { name = c.name, targetProgressToComplete = c.targetProgressToComplete,
                  executionMode = c.executionMode, children = new List<CompositeTaskNode.Child>() };
                if (c.children != null)
                    foreach (var ch in c.children)
                        if (ch?.taskNode != null)
                            clone.children.Add(new CompositeTaskNode.Child
                            { enabled = ch.enabled, subTaskValue = ch.subTaskValue,
                              taskNode = DeepClone(ch.taskNode) });
                return clone;
            }
            return null;
        }

        static ITaskDefinition CopyTaskDef(ITaskDefinition source)
        {
            if (source == null) return null;
            var type = source.GetType();
            ITaskDefinition clone;
            try { clone = (ITaskDefinition)Activator.CreateInstance(type); }
            catch (Exception ex) { Debug.LogError($"CopyTaskDef: Failed to create {type.Name}: {ex.Message}"); return null; }

            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var v = f.GetValue(source);
                if (v == null) { f.SetValue(clone, null); continue; }
                if (f.FieldType.IsArray && v is Array arr) f.SetValue(clone, arr.Clone());
                else if (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var lt = typeof(List<>).MakeGenericType(f.FieldType.GetGenericArguments());
                    var nl = (System.Collections.IList)Activator.CreateInstance(lt);
                    foreach (var item in (System.Collections.IEnumerable)v) nl.Add(item);
                    f.SetValue(clone, nl);
                }
                else f.SetValue(clone, v);
            }
            return clone;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════

        #region Helpers

        static TaskTree GetTaskTreeFromProperty(SerializedProperty property)
        {
            // Dùng boxedValue (Unity 2022.1+) — handle mọi trường hợp kể cả nested trong array
            try
            {
                return property.boxedValue as TaskTree;
            }
            catch
            {
                return null;
            }
        }

        static ATaskNode FirstOf(HashSet<ATaskNode> set)
        {
            foreach (var n in set) return n;
            return null;
        }

        static bool IsAncestorOrSelf(ATaskNode candidate, ATaskNode target)
        {
            if (candidate == target) return true;
            if (candidate is CompositeTaskNode c && c.children != null)
                foreach (var ch in c.children)
                    if (ch?.taskNode != null && IsAncestorOrSelf(ch.taskNode, target)) return true;
            return false;
        }

        static bool IsDescendant(ATaskNode root, ATaskNode target)
        {
            if (root == target) return true;
            if (root is CompositeTaskNode comp && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch?.taskNode != null && IsDescendant(ch.taskNode, target)) return true;
            return false;
        }

        internal static void FindParent(ATaskNode root, ATaskNode target,
                                out CompositeTaskNode parent, out int index)
        {
            parent = null; index = -1;
            FindParentRec(root, target, ref parent, ref index);
        }

        static bool FindParentRec(ATaskNode node, ATaskNode target,
                                   ref CompositeTaskNode parent, ref int index)
        {
            if (node is CompositeTaskNode comp && comp.children != null)
                for (int i = 0; i < comp.children.Count; i++)
                {
                    var ch = comp.children[i];
                    if (ch?.taskNode == null) continue;
                    if (ch.taskNode == target) { parent = comp; index = i; return true; }
                    if (FindParentRec(ch.taskNode, target, ref parent, ref index)) return true;
                }
            return false;
        }

        static string MakeUniqueSiblingName(CompositeTaskNode parent, string original)
        {
            if (parent?.children == null) return original;
            if (string.IsNullOrEmpty(original)) original = "New Task";
            var names = new List<string>();
            foreach (var ch in parent.children)
                if (ch?.taskNode != null && !string.IsNullOrEmpty(ch.taskNode.name))
                    names.Add(ch.taskNode.name);
            return ObjectNames.GetUniqueName(names.ToArray(), original);
        }

        static bool IsExpanded(DrawerState s, CompositeTaskNode node)
        {
            if (!s.expanded.TryGetValue(node, out bool v)) { s.expanded[node] = true; return true; }
            return v;
        }

        static void SetExpanded(DrawerState s, CompositeTaskNode node, bool value) => s.expanded[node] = value;

        static Vector2 ScreenToScrollLocal(DrawerState s, Vector2 mousePos)
        {
            return new Vector2(
                mousePos.x - s.hierarchyScrollRect.x,
                mousePos.y - s.hierarchyScrollRect.y + s.hierarchyScroll.y);
        }

        static ATaskNode HitTestNode(DrawerState s, ATaskNode node, Vector2 localPos)
        {
            float y = 0;
            return HitTestRec(s, node, localPos, ref y);
        }

        static ATaskNode HitTestRec(DrawerState s, ATaskNode node, Vector2 pos, ref float y)
        {
            if (node == null || !IsVisibleBySearch(s, node)) return null;
            var r = new Rect(0, y, 10000, RowHeight);
            y += RowHeight;
            if (r.Contains(pos)) return node;
            if (node is CompositeTaskNode comp && IsExpanded(s, comp) && comp.children != null)
                foreach (var ch in comp.children)
                {
                    if (ch?.taskNode == null) continue;
                    var result = HitTestRec(s, ch.taskNode, pos, ref y);
                    if (result != null) return result;
                }
            return null;
        }

        static bool IsVisibleBySearch(DrawerState s, ATaskNode node)
        {
            if (string.IsNullOrEmpty(s.searchFilter)) return true;
            return SubtreeMatch(node, s.searchFilter);
        }

        static bool SubtreeMatch(ATaskNode node, string filter)
        {
            if (node == null) return false;
            if (!string.IsNullOrEmpty(node.name) &&
                node.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (node is CompositeTaskNode comp && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch?.taskNode != null && SubtreeMatch(ch.taskNode, filter)) return true;
            return false;
        }

        static bool IsNodeEnabled(DrawerState s, ATaskNode node)
        {
            return s.enabledCache.TryGetValue(node, out bool v) ? v : true;
        }

        internal static void RebuildEnabledCache(DrawerState s, TaskTree taskTree)
        {
            s.enabledCache.Clear();
            if (taskTree == null) return;
            CacheEnabledRec(s, taskTree.root, null, -1, true);
        }

        static void CacheEnabledRec(DrawerState s, ATaskNode node, CompositeTaskNode parent, int idx, bool parentEnabled)
        {
            bool self = parentEnabled;
            if (parent?.children != null && idx >= 0 && idx < parent.children.Count)
                self = parentEnabled && parent.children[idx].enabled;
            s.enabledCache[node] = self;
            if (node is CompositeTaskNode comp && comp.children != null)
                for (int i = 0; i < comp.children.Count; i++)
                    if (comp.children[i]?.taskNode != null)
                        CacheEnabledRec(s, comp.children[i].taskNode, comp, i, self);
        }

        static void PurgeExpanded(DrawerState s, TaskTree taskTree)
        {
            if (taskTree == null) { s.expanded.Clear(); return; }
            var alive = new HashSet<ATaskNode>();
            CollectAll(taskTree.root, alive);
            var remove = new List<ATaskNode>();
            foreach (var k in s.expanded.Keys) if (!alive.Contains(k)) remove.Add(k);
            foreach (var k in remove) s.expanded.Remove(k);
        }

        static void CollectAll(ATaskNode node, HashSet<ATaskNode> set)
        {
            if (node == null) return;
            set.Add(node);
            if (node is CompositeTaskNode comp && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch?.taskNode != null) CollectAll(ch.taskNode, set);
        }


        internal static void MarkDirty(UnityEngine.Object target)
        {
            if (target == null) return;
            EditorUtility.SetDirty(target);
            if (target is Component comp)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  STYLES
        // ══════════════════════════════════════════════════════════════════

        #region Styles

        internal static void BuildStyles(DrawerState s)
        {
            if (s.stylesBuilt && s.labelStyle != null) return;
            s.stylesBuilt = true;

            s.labelStyle = new GUIStyle(EditorStyles.label)
            { normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }, alignment = TextAnchor.MiddleLeft };
            s.dimLabelStyle = new GUIStyle(EditorStyles.label)
            { normal = { textColor = DisabledTextColor }, fontSize = 10 };
            s.headerStyle = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 12, normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
              alignment = TextAnchor.MiddleLeft, padding = new RectOffset(4, 4, 2, 2) };
            s.sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 11, normal = { textColor = new Color(0.75f, 0.75f, 0.75f) } };
            s.renameStyle = new GUIStyle(EditorStyles.textField)
            { padding = new RectOffset(2, 2, 1, 1), fontSize = EditorStyles.label.fontSize };
            s.foldoutArrowStyle = new GUIStyle(EditorStyles.label)
            { normal = { textColor = FoldoutArrowColor } };
            s.statusDotStyle = new GUIStyle(EditorStyles.label) { fontSize = 9 };
            s.nodeNameStyle = new GUIStyle(s.labelStyle);
        }

        #endregion
    }

#if ODIN_INSPECTOR
    /// <summary>
    /// OdinValueDrawer cho TaskTree — Odin ưu tiên dùng drawer này thay vì wrap PropertyDrawer.
    /// Hierarchy vẽ bằng IMGUI rect-based, Inspector dùng Odin InspectorProperty.Draw().
    /// </summary>
    public class TaskTreeOdinDrawer : Sirenix.OdinInspector.Editor.OdinValueDrawer<TaskTree>
    {
        // Reuse DrawerState from PropertyDrawer
        TaskTreePropertyDrawer.DrawerState _state;

        TaskTreePropertyDrawer.DrawerState GetState()
        {
            if (_state == null)
            {
                _state = new TaskTreePropertyDrawer.DrawerState();
                var target = this.Property.Tree.UnitySerializedObject?.targetObject;
                string key = this.Property.Path + "_" + (target != null ? target.GetInstanceID().ToString() : "0");
                _state.foldoutKeyPrefix = "TaskTreePD_" + key + "_";
            }
            return _state;
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            var s = GetState();
            TaskTreePropertyDrawer.BuildStyles(s);

            var taskTree = this.ValueEntry.SmartValue;
            var targetObj = this.Property.Tree.UnitySerializedObject?.targetObject;

            TaskTreePropertyDrawer.RebuildEnabledCache(s, taskTree);

            // Main foldout
            this.Property.State.Expanded = Sirenix.Utilities.Editor.SirenixEditorGUI.Foldout(
                this.Property.State.Expanded, label ?? new GUIContent("Task Tree"));

            if (!this.Property.State.Expanded) return;

            EditorGUI.indentLevel++;

            // ── Import/Export Foldout ──
            TaskTreePropertyDrawer.SetFoldout(s, TaskTreePropertyDrawer.FoldoutImportExport,
                EditorGUILayout.Foldout(
                    TaskTreePropertyDrawer.GetFoldout(s, TaskTreePropertyDrawer.FoldoutImportExport),
                    "Import / Export", true));
            if (TaskTreePropertyDrawer.GetFoldout(s, TaskTreePropertyDrawer.FoldoutImportExport))
            {
                EditorGUI.indentLevel++;
                DrawImportExportLayout(s, taskTree, targetObj, this.Property.Tree.UnitySerializedObject?.FindProperty(this.Property.UnityPropertyPath));
                EditorGUI.indentLevel--;
            }

            // ── Hierarchy Foldout ──
            TaskTreePropertyDrawer.SetFoldout(s, TaskTreePropertyDrawer.FoldoutHierarchy,
                EditorGUILayout.Foldout(
                    TaskTreePropertyDrawer.GetFoldout(s, TaskTreePropertyDrawer.FoldoutHierarchy),
                    "Hierarchy", true));
            if (TaskTreePropertyDrawer.GetFoldout(s, TaskTreePropertyDrawer.FoldoutHierarchy))
            {
                float hierarchyH = TaskTreePropertyDrawer.HierarchyHeight + TaskTreePropertyDrawer.SearchHeight;
                var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, hierarchyH));

                TaskTreePropertyDrawer.HandleKeyboard(s, taskTree, targetObj);
                TaskTreePropertyDrawer.HandleDrag(s, taskTree, targetObj);
                var serializedProp = this.Property.Tree.UnitySerializedObject?.FindProperty(this.Property.UnityPropertyPath);
                TaskTreePropertyDrawer.DrawHierarchy(s, rect, taskTree, targetObj, serializedProp);
            }

            // ── Execute button (play mode) ──
            if (Application.isPlaying && taskTree != null)
            {
                if (GUILayout.Button("▶  Execute"))
                    taskTree.Execute();
            }

            // ── Inspector Foldout ──
            if (s.selected != null)
            {
                TaskTreePropertyDrawer.SetFoldout(s, TaskTreePropertyDrawer.FoldoutInspector,
                    EditorGUILayout.Foldout(
                        TaskTreePropertyDrawer.GetFoldout(s, TaskTreePropertyDrawer.FoldoutInspector),
                        "Inspector", true));

                if (TaskTreePropertyDrawer.GetFoldout(s, TaskTreePropertyDrawer.FoldoutInspector))
                {
                    EditorGUI.indentLevel++;

                    // ── Row 1: [Enabled toggle] [Name] (non-root) ──
                    bool isNonRoot = s.selected != taskTree.root;
                    TaskTreePropertyDrawer.FindParent(taskTree.root, s.selected,
                        out var parentComp, out int childIdx);

                    if (isNonRoot && parentComp != null && childIdx >= 0 && childIdx < parentComp.children.Count)
                    {
                        var childData = parentComp.children[childIdx];
                        EditorGUILayout.BeginHorizontal();

                        // Enabled toggle
                        EditorGUI.BeginChangeCheck();
                        bool newEnabled = EditorGUILayout.Toggle(GUIContent.none, childData.enabled, GUILayout.Width(18));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RegisterCompleteObjectUndo(targetObj, "Toggle Enabled");
                            parentComp.children[childIdx].enabled = newEnabled;
                            TaskTreePropertyDrawer.MarkDirty(targetObj);
                        }

                        // Name
                        EditorGUI.BeginChangeCheck();
                        string newName = EditorGUILayout.TextField(s.selected.name ?? "");
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RegisterCompleteObjectUndo(targetObj, "Rename");
                            s.selected.name = newName;
                            TaskTreePropertyDrawer.MarkDirty(targetObj);
                        }

                        EditorGUILayout.EndHorizontal();

                        // SubTaskValue
                        EditorGUI.BeginChangeCheck();
                        float newSv = EditorGUILayout.FloatField("Sub Task Value", childData.subTaskValue);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RegisterCompleteObjectUndo(targetObj, "Edit SubTaskValue");
                            parentComp.children[childIdx].subTaskValue = Mathf.Max(0f, newSv);
                            TaskTreePropertyDrawer.MarkDirty(targetObj);
                        }
                    }
                    else
                    {
                        // Root: chỉ name
                        EditorGUI.BeginChangeCheck();
                        string newName = EditorGUILayout.TextField("Name", s.selected.name ?? "");
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RegisterCompleteObjectUndo(targetObj, "Rename");
                            s.selected.name = newName;
                            TaskTreePropertyDrawer.MarkDirty(targetObj);
                        }
                    }

                    // ── Remaining Odin properties (skip name, children, taskDefinition) ──
                    var nodeProp = FindOdinProperty(this.Property, taskTree, s.selected);
                    if (nodeProp != null)
                    {
                        for (int i = 0; i < nodeProp.Children.Count; i++)
                        {
                            var child = nodeProp.Children[i];
                            if (child.Name == "name" || child.Name == "children" || child.Name == "taskDefinition")
                                continue;
                            child.Draw();
                        }
                    }

                    // ── TaskDefinition: custom type popup + Odin-drawn fields ──
                    if (s.selected is MonoTaskNode mono)
                    {
                        DrawTaskDefinitionOdin(s, this.Property, taskTree, mono, targetObj);
                    }

                    EditorGUI.indentLevel--;

                    // Runtime inspector (Play Mode)
                    if (Application.isPlaying && s.selected != null)
                    {
                        GUILayout.Space(4);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);

                        Color statusColor = s.selected.Status switch
                        {
                            TaskNodeStatus.Running   => TaskTreePropertyDrawer.StatusRunning,
                            TaskNodeStatus.Completed => TaskTreePropertyDrawer.StatusCompleted,
                            TaskNodeStatus.Failed    => TaskTreePropertyDrawer.StatusFailed,
                            _                        => TaskTreePropertyDrawer.StatusPending,
                        };
                        var oldColor = GUI.contentColor;
                        GUI.contentColor = statusColor;
                        EditorGUILayout.LabelField("Status", s.selected.Status.ToString());
                        GUI.contentColor = oldColor;

                        var progRect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, 16));
                        EditorGUI.ProgressBar(progRect, s.selected.Progress,
                            $"{s.selected.Progress * 100f:F1}%");

                        var btnRect2 = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
                        float btnW = (btnRect2.width - 8) / 3f;
                        if (GUI.Button(new Rect(btnRect2.x, btnRect2.y, btnW, btnRect2.height), "ForceComplete"))
                            s.selected.ForceComplete();
                        if (GUI.Button(new Rect(btnRect2.x + btnW + 4, btnRect2.y, btnW, btnRect2.height), "ForceImmediate"))
                            s.selected.ForceComplete(true);
                        if (GUI.Button(new Rect(btnRect2.x + 2*(btnW+4), btnRect2.y, btnW, btnRect2.height), "Reset"))
                            s.selected.Reset();
                        EditorGUI.indentLevel--;
                    }

                    // Nút thêm child cho CompositeTaskNode
                    if (s.selected is CompositeTaskNode comp)
                    {
                        GUILayout.Space(4);
                        var btnRect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
                        float halfW = (btnRect.width - 4) / 2f;
                        if (GUI.Button(new Rect(btnRect.x, btnRect.y, halfW, btnRect.height), "+ Mono Task"))
                            TaskTreePropertyDrawer.AddMonoChild(s, comp, targetObj);
                        if (GUI.Button(new Rect(btnRect.x + halfW + 4, btnRect.y, halfW, btnRect.height), "+ Composite Task"))
                            TaskTreePropertyDrawer.AddCompositeChild(s, comp, ExecutionMode.Sequential, targetObj);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Vẽ TaskDefinition section trong Odin path: custom type popup + Odin property drawing.
        /// Ẩn Odin polymorphism selector, chỉ show popup từ [TaskDefinition] registry.
        /// </summary>
        void DrawTaskDefinitionOdin(TaskTreePropertyDrawer.DrawerState s,
                                     Sirenix.OdinInspector.Editor.InspectorProperty taskTreeProp,
                                     TaskTree taskTree, MonoTaskNode mono, UnityEngine.Object targetObj)
        {
            // Build type list from [TaskDefinition] attribute registry
            var types = new List<Type>();
            var names = new List<string>();
            foreach (var entry in s.RegistryEntries)
            {
                types.Add(entry.Type);
                names.Add(entry.DisplayName);
            }

            // Current index
            int curIdx = -1;
            if (mono.taskDefinition != null)
            {
                var curType = mono.taskDefinition.GetType();
                for (int i = 0; i < types.Count; i++)
                    if (types[i] == curType) { curIdx = i; break; }
            }

            // Searchable popup button
            var options = new string[names.Count + 1];
            options[0] = "None";
            for (int i = 0; i < names.Count; i++) options[i + 1] = names[i];

            int popupIdx = mono.taskDefinition == null ? 0 : (curIdx >= 0 ? curIdx + 1 : 0);
            string currentLabel = popupIdx >= 0 && popupIdx < options.Length ? options[popupIdx] : "None";

            var btnRect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
            float labelW = EditorGUIUtility.labelWidth;
            GUI.Label(new Rect(btnRect.x, btnRect.y, labelW, btnRect.height), "Task Definition");
            var dropRect = new Rect(btnRect.x + labelW, btnRect.y, btnRect.width - labelW, btnRect.height);
            if (EditorGUI.DropdownButton(dropRect, new GUIContent(currentLabel), FocusType.Passive))
            {
                var capturedTypes = types;
                var capturedMono = mono;
                var capturedTarget = targetObj;

                SearchablePopup.Show(dropRect, options, popupIdx, newIdx =>
                {
                    Undo.RegisterCompleteObjectUndo(capturedTarget, "Change TaskDefinition");
                    if (newIdx == 0)
                        capturedMono.taskDefinition = null;
                    else
                    {
                        int typeIdx = newIdx - 1;
                        if (typeIdx >= 0 && typeIdx < capturedTypes.Count)
                        {
                            try { capturedMono.taskDefinition = (ITaskDefinition)Activator.CreateInstance(capturedTypes[typeIdx]); }
                            catch (Exception ex) { Debug.LogError($"Failed to create {capturedTypes[typeIdx].Name}: {ex.Message}"); }
                        }
                    }
                    TaskTreePropertyDrawer.MarkDirty(capturedTarget);
                });
            }

            // Draw taskDefinition fields via Odin (skip the polymorphism selector)
            if (mono.taskDefinition != null)
            {
                var nodeProp = FindOdinProperty(taskTreeProp, taskTree, s.selected);
                if (nodeProp != null)
                {
                    // Find taskDefinition property
                    Sirenix.OdinInspector.Editor.InspectorProperty tdProp = null;
                    for (int i = 0; i < nodeProp.Children.Count; i++)
                        if (nodeProp.Children[i].Name == "taskDefinition") { tdProp = nodeProp.Children[i]; break; }

                    if (tdProp != null)
                    {
                        // Draw each child of taskDefinition (the actual fields, not the type selector)
                        for (int i = 0; i < tdProp.Children.Count; i++)
                            tdProp.Children[i].Draw();
                    }
                }
            }
        }

        /// <summary>
        /// GUILayout-based import/export (dùng cho Odin drawer).
        /// Gọi shared static methods từ PropertyDrawer.
        /// </summary>
        void DrawImportExportLayout(TaskTreePropertyDrawer.DrawerState s, TaskTree taskTree,
                                    UnityEngine.Object targetObj, SerializedProperty property)
        {
            s.importJson = (TextAsset)EditorGUILayout.ObjectField("Import JSON", s.importJson, typeof(TextAsset), false);
            s.exportFolder = (DefaultAsset)EditorGUILayout.ObjectField("Export Folder", s.exportFolder, typeof(DefaultAsset), false);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Import"))
                TaskTreePropertyDrawer.ImportFromJson(s, property, taskTree);
            if (GUILayout.Button("Export"))
                TaskTreePropertyDrawer.ExportToJson(s, property, taskTree);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Tìm InspectorProperty trong Odin property tree tương ứng với selected ATaskNode.
        /// </summary>
        static Sirenix.OdinInspector.Editor.InspectorProperty FindOdinProperty(
            Sirenix.OdinInspector.Editor.InspectorProperty taskTreeProp,
            TaskTree taskTree, ATaskNode target)
        {
            if (taskTreeProp == null || taskTree == null) return null;
            Sirenix.OdinInspector.Editor.InspectorProperty rootProp = null;
            for (int i = 0; i < taskTreeProp.Children.Count; i++)
                if (taskTreeProp.Children[i].Name == "root") { rootProp = taskTreeProp.Children[i]; break; }
            if (rootProp == null) return null;
            if (target == taskTree.root) return rootProp;
            return FindOdinPropertyRec(rootProp, taskTree.root, target);
        }

        static Sirenix.OdinInspector.Editor.InspectorProperty FindOdinPropertyRec(
            Sirenix.OdinInspector.Editor.InspectorProperty prop,
            ATaskNode current, ATaskNode target)
        {
            if (current is not CompositeTaskNode comp || comp.children == null) return null;

            Sirenix.OdinInspector.Editor.InspectorProperty childrenProp = null;
            for (int i = 0; i < prop.Children.Count; i++)
                if (prop.Children[i].Name == "children") { childrenProp = prop.Children[i]; break; }
            if (childrenProp == null) return null;

            int count = Mathf.Min(comp.children.Count, childrenProp.Children.Count);
            for (int i = 0; i < count; i++)
            {
                var childEntry = childrenProp.Children[i];
                if (childEntry == null) continue;

                Sirenix.OdinInspector.Editor.InspectorProperty taskNodeProp = null;
                for (int j = 0; j < childEntry.Children.Count; j++)
                    if (childEntry.Children[j].Name == "taskNode") { taskNodeProp = childEntry.Children[j]; break; }
                if (taskNodeProp == null) continue;

                if (comp.children[i]?.taskNode == target) return taskNodeProp;

                var result = FindOdinPropertyRec(taskNodeProp, comp.children[i].taskNode, target);
                if (result != null) return result;
            }
            return null;
        }
    }
#endif
}
