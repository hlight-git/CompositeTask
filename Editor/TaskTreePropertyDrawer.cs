// =============================================================================
//  TaskTreePropertyDrawer.cs
//  PropertyDrawer cho TaskTree (pure serializable class).
//  Vẽ inline trong Unity Inspector: Import/Export → Hierarchy → Inspector.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
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
        const float DragThreshSq    = 64f;

        static readonly Color HierarchyBg        = new(0.19f, 0.19f, 0.19f);
        static readonly Color SelectionHighlight  = new(0.24f, 0.49f, 0.91f, 0.85f);
        static readonly Color HoverHighlight      = new(0.3f, 0.3f, 0.3f, 0.4f);
        static readonly Color DropLineColor       = new(0.35f, 0.8f, 1f);
        static readonly Color DividerColor        = new(0.1f, 0.1f, 0.1f);
        static readonly Color StatusRunning       = new(0.2f, 0.8f, 1f);
        static readonly Color StatusCompleted     = new(0.2f, 0.85f, 0.3f);
        static readonly Color StatusPending       = new(0.45f, 0.45f, 0.45f);
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

            // Hierarchy
            public ATaskNode selected;
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
            public Vector2 dragStartPos;
            public CompositeTaskNode dropParentTarget;
            public int dropInsertIndex;
            public bool dropValid;

            // Clipboard
            public ATaskNode clipboard;


            // Enabled cache
            public Dictionary<ATaskNode, bool> enabledCache = new();

            // Database
            public TaskDefinitionDatabase database;

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

            var iter = nodeProp.Copy();
            var endProp = nodeProp.GetEndProperty();
            bool enterChildren = true;

            while (iter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iter, endProp))
            {
                enterChildren = false;

                if (iter.name == "children") continue;

                h += EditorGUI.GetPropertyHeight(iter, true) + 2;
            }

            // Nút Add Child cho CompositeTaskNode
            if (s.selected is CompositeTaskNode)
                h += EditorGUIUtility.singleLineHeight + 6;

            return h + 8; // padding
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
            EnsureDatabase(s);

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
                SerializationBinder = new TaskTreeSerializationBinder(s.database),
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

            float y = 0;
            DrawNodeRow(s, taskTree.root, null, -1, 0, ref y, scrollRect.width, taskTree, targetObj, property);

            GUI.EndScrollView();

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
            bool isSelected = node == s.selected;
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
                    { StartRename(s, node); e.Use(); }
                    else
                    {
                        s.selected = node;
                        ClearTextFieldFocus();
                        s.dragActive = false;
                        s.dragStartPos = e.mousePosition;
                        s.draggedNode = node;
                        e.Use();
                    }
                }
                else if (e.button == 1 && s.selected != node)
                { s.selected = node; }
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

            if (!s.dragActive && e.type == EventType.MouseUp)
            { s.draggedNode = null; return; }

            if (e.type == EventType.MouseDown && !s.hierarchyScrollRect.Contains(e.mousePosition))
            { CancelDrag(s); return; }

            if (!s.dragActive && e.type == EventType.MouseDrag)
            {
                if (!s.hierarchyScrollRect.Contains(e.mousePosition))
                { CancelDrag(s); return; }
                if (((Vector2)e.mousePosition - s.dragStartPos).sqrMagnitude > DragThreshSq)
                { s.dragActive = true; s.dropValid = false; }
            }

            if (!s.dragActive) return;

            if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove)
            {
                UpdateDropTarget(s, taskTree, e.mousePosition);
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
        }

        static void UpdateDropTarget(DrawerState s, TaskTree taskTree, Vector2 mousePos)
        {
            if (taskTree == null) return;
            float localY = ScreenToScrollLocal(s, mousePos).y;
            float y = 0f;
            s.dropValid = false;
            FindDropTarget(s, taskTree.root, null, -1, ref y, localY);
        }

        static void FindDropTarget(DrawerState s, ATaskNode node, CompositeTaskNode parent, int idx,
                             ref float y, float mouseY)
        {
            if (node == null) return;
            float rowTop = y;
            y += RowHeight;

            if (mouseY >= rowTop && mouseY < rowTop + RowHeight * 0.5f)
            {
                if (parent != null && s.draggedNode != node && !IsAncestorOrSelf(s.draggedNode, parent))
                { s.dropParentTarget = parent; s.dropInsertIndex = idx; s.dropValid = true; }
            }
            else if (mouseY >= rowTop + RowHeight * 0.5f && mouseY < rowTop + RowHeight)
            {
                if (node is CompositeTaskNode comp && IsExpanded(s, comp) && s.draggedNode != node &&
                    !IsAncestorOrSelf(s.draggedNode, comp))
                { s.dropParentTarget = comp; s.dropInsertIndex = 0; s.dropValid = true; }
                else if (parent != null && s.draggedNode != node && !IsAncestorOrSelf(s.draggedNode, parent))
                { s.dropParentTarget = parent; s.dropInsertIndex = idx + 1; s.dropValid = true; }
            }

            if (node is CompositeTaskNode cn && IsExpanded(s, cn) && cn.children != null)
                for (int i = 0; i < cn.children.Count; i++)
                    if (cn.children[i]?.taskNode != null)
                        FindDropTarget(s, cn.children[i].taskNode, cn, i, ref y, mouseY);
        }

        static void PerformDrop(DrawerState s, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            if (s.dropParentTarget == null || s.draggedNode == null) return;
            FindParent(taskTree.root, s.draggedNode, out var oldParent, out int oldIdx);
            if (oldParent == s.dropParentTarget && oldIdx == s.dropInsertIndex) return;

            Undo.RegisterCompleteObjectUndo(targetObj, "Move Node");
            float sv = 1f;
            if (oldParent != null && oldIdx >= 0 && oldIdx < oldParent.children.Count)
            {
                sv = oldParent.children[oldIdx].subTaskValue;
                oldParent.children.RemoveAt(oldIdx);
                if (oldParent == s.dropParentTarget && oldIdx < s.dropInsertIndex) s.dropInsertIndex--;
            }
            if (s.dropParentTarget.children == null)
                s.dropParentTarget.children = new List<CompositeTaskNode.Child>();
            s.dropInsertIndex = Mathf.Clamp(s.dropInsertIndex, 0, s.dropParentTarget.children.Count);
            s.dropParentTarget.children.Insert(s.dropInsertIndex, new CompositeTaskNode.Child
            { subTaskValue = sv, taskNode = s.draggedNode });
            PurgeExpanded(s, taskTree);
            MarkDirty(targetObj);
        }

        static void DrawDropIndicator(DrawerState s, TaskTree taskTree, Rect scrollRect)
        {
            float y = 0;
            float lineY = ComputeDropLineY(s, taskTree.root, null, -1, ref y);
            if (lineY < 0) return;
            float absY = scrollRect.y + lineY - s.hierarchyScroll.y;
            if (absY < scrollRect.y || absY > scrollRect.yMax) return;
            EditorGUI.DrawRect(new Rect(scrollRect.x + 4, absY - 1, scrollRect.width - 8, 2), DropLineColor);
        }

        static float ComputeDropLineY(DrawerState s, ATaskNode node, CompositeTaskNode parent, int idx, ref float y)
        {
            if (node == null) return -1;
            float thisY = y; y += RowHeight;
            if (s.dropParentTarget == parent && s.dropInsertIndex == idx) return thisY;
            if (node is CompositeTaskNode comp && IsExpanded(s, comp) && comp.children != null)
            {
                for (int i = 0; i < comp.children.Count; i++)
                {
                    if (comp.children[i]?.taskNode == null) continue;
                    float r = ComputeDropLineY(s, comp.children[i].taskNode, comp, i, ref y);
                    if (r >= 0) return r;
                }
                if (s.dropParentTarget == comp && s.dropInsertIndex == comp.children.Count)
                    return y;
            }
            return -1;
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
                menu.AddItem(new GUIContent("Rename        F2"), false, () => StartRename(s, target));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Duplicate     Ctrl+D"), false,
                    () => DuplicateNode(s, target, taskTree, targetObj));
                menu.AddItem(new GUIContent("Copy          Ctrl+C"), false, () => s.clipboard = target);
                if (s.clipboard != null && target is CompositeTaskNode cp)
                    menu.AddItem(new GUIContent("Paste as Child  Ctrl+V"), false,
                        () => PasteChild(s, cp, taskTree, targetObj));
                else
                    menu.AddDisabledItem(new GUIContent("Paste as Child  Ctrl+V"));
                menu.AddSeparator("");
                if (isRoot)
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
            { DeleteNode(s, s.selected, taskTree, targetObj); e.Use(); return; }

            if (ctrl && e.keyCode == KeyCode.D)
            { DuplicateNode(s, s.selected, taskTree, targetObj); e.Use(); return; }
            if (ctrl && e.keyCode == KeyCode.C)
            { s.clipboard = s.selected; e.Use(); return; }
            if (ctrl && e.keyCode == KeyCode.V && s.selected is CompositeTaskNode cv)
            { PasteChild(s, cv, taskTree, targetObj); e.Use(); return; }

            if (e.keyCode == KeyCode.F2)
            { StartRename(s, s.selected); e.Use(); return; }

            if (e.alt && e.keyCode == KeyCode.LeftArrow)
            { CollapseAll(s, taskTree.root); e.Use(); return; }
            if (e.alt && e.keyCode == KeyCode.RightArrow)
            { ExpandAll(s, taskTree.root); e.Use(); return; }

            if (cur >= 0)
            {
                if (e.keyCode == KeyCode.UpArrow)
                { if (cur > 0) s.selected = visible[cur - 1]; e.Use(); return; }
                if (e.keyCode == KeyCode.DownArrow)
                { if (cur < visible.Count - 1) s.selected = visible[cur + 1]; e.Use(); return; }
            }

            if (e.keyCode == KeyCode.LeftArrow)
            {
                if (s.selected is CompositeTaskNode comp && IsExpanded(s, comp))
                    SetExpanded(s, comp, false);
                else { FindParent(taskTree.root, s.selected, out var p, out _); if (p != null) s.selected = p; }
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
            if (s.selected == node || (s.selected != null && IsDescendant(node, s.selected)))
                s.selected = parent;
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
            s.selected = clone;
            MarkDirty(targetObj);
        }

        static void PasteChild(DrawerState s, CompositeTaskNode parent, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            if (s.clipboard == null) return;
            var clone = DeepClone(s.clipboard);
            Undo.RegisterCompleteObjectUndo(targetObj, "Paste Node");
            if (parent.children == null) parent.children = new List<CompositeTaskNode.Child>();
            parent.children.Add(new CompositeTaskNode.Child { enabled = true, subTaskValue = 1f, taskNode = clone });
            SetExpanded(s, parent, true);
            clone.name = MakeUniqueSiblingName(parent, s.clipboard.name);
            s.selected = clone;
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
        /// taskDefinition (SerializeReference interface) được vẽ đặc biệt: type popup + child fields.
        /// </summary>
        static float DrawNodeProperties(DrawerState s, SerializedObject so, SerializedProperty nodeProp,
                                 string nodePath, ATaskNode node, TaskTree taskTree,
                                 UnityEngine.Object targetObj, float x, float y, float w)
        {
            float lineH = EditorGUIUtility.singleLineHeight;

            var iter = nodeProp.Copy();
            var endProp = nodeProp.GetEndProperty();
            bool enterChildren = true;

            while (iter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iter, endProp))
            {
                enterChildren = false;

                // taskDefinition + các field khác: vẽ bằng PropertyField.
                // Không cần xử lý đặc biệt — Unity/Odin tự handle [SerializeReference].

                // children: skip — đã hiện trong hierarchy
                if (iter.name == "children") continue;

                // Các field khác: vẽ bình thường
                float propH = EditorGUI.GetPropertyHeight(iter, true);
                EditorGUI.PropertyField(new Rect(x, y, w, propH), iter, true);
                y += propH + 2;
            }

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

        static void FindParent(ATaskNode root, ATaskNode target,
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

        static void EnsureDatabase(DrawerState s)
        {
            if (s.database != null) return;
            string[] guids = AssetDatabase.FindAssets("t:TaskDefinitionDatabase");
            if (guids == null || guids.Length == 0) return;
            s.database = AssetDatabase.LoadAssetAtPath<TaskDefinitionDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
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
                TaskTreePropertyDrawer.DrawHierarchy(s, rect, taskTree, targetObj, null);
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
                    // Tìm InspectorProperty tương ứng với selected node → vẽ bằng Odin
                    var nodeProp = FindOdinProperty(this.Property, taskTree, s.selected);
                    if (nodeProp != null)
                    {
                        EditorGUI.indentLevel++;
                        for (int i = 0; i < nodeProp.Children.Count; i++)
                        {
                            var child = nodeProp.Children[i];
                            if (child.Name == "children") continue;
                            child.Draw();
                        }

                        EditorGUI.indentLevel--;
                    }

                    // Nút thêm child cho CompositeTaskNode (ngoài indent block)
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
