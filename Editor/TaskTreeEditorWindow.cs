// =============================================================================
//  TaskTreeEditorWindow.cs
//  EditorWindow gồm hai panel: Hierarchy (trái) + Inspector (phải).
//  Giống Unity Hierarchy / Inspector cho TaskTree.
//
//  Event flow trong OnGUI():
//    1. BuildStyles()          — lazy-init GUIStyle
//    2. RebuildEnabledCache()  — O(n) cache enabled state cho mỗi node
//    3. HandleKeyboard()       — keyboard shortcuts (trước drawing để consume event)
//    4. HandleDragInHierarchy()— drag threshold + drag movement + drop commit
//    5. HandleTaskTreeSelector()— toolbar (TaskTree + Database ObjectField)
//    6. DrawHierarchy()        — left panel (scroll → recursive DrawNodeRow)
//    7. DrawDivider()          — resizable divider (only when a node is selected)
//    8. DrawInspector()        — right panel (only when a node is selected)
//
//  Coordinate systems:
//    - Screen coords: Event.current.mousePosition (relative to window)
//    - Scroll-local coords: relative to ScrollView content (dùng ScreenToScrollLocal())
//
//  Odin Inspector:
//    Khi ODIN_INSPECTOR được define, taskDefinition fields được vẽ bằng Odin PropertyTree
//    để tận dụng custom drawer, attribute (ShowIf, FoldoutGroup...).
//    Window vẫn kế thừa EditorWindow (không dùng OdinEditorWindow) để tránh
//    conflict event handling.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Hlight.Structures.CompositeTask.Runtime;
using UnityEditor;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Editor
{
    public class TaskTreeEditorWindow : EditorWindow
    {
        // ══════════════════════════════════════════════════════════════════
        //  CONSTANTS
        // ══════════════════════════════════════════════════════════════════

        #region Constants

        // Layout
        const float MinWindowWidth      = 600f;
        const float MinWindowHeight     = 400f;
        const float ToolbarObjectFieldW = 240f;
        const float ExecuteButtonW      = 90f;
        const float MinHierarchyWidth   = 150f;
        const float MinInspectorWidth   = 200f;
        const float DividerWidth        = 4f;
        const float PanelHeaderHeight   = 22f;
        const float SearchBarHeight     = 20f;
        const float ToolbarExtraHeight  = 6f;

        // Hierarchy rows
        const float RowHeight    = 20f;
        const float IndentStep   = 14f;
        const float FoldoutW     = 14f;
        const float StatusDotW   = 14f;
        const float BadgeW       = 38f;
        const float BadgePadding = 2f;
        const float RowPadLeft   = 4f;

        // Drag
        const float DragThreshSq = 64f; // 8px threshold

        // Repaint
        const double RepaintInterval = 0.05;

        // Colors
        static readonly Color HierarchyBg        = new(0.19f, 0.19f, 0.19f);
        static readonly Color PanelHeaderBg      = new(0.15f, 0.15f, 0.15f);
        static readonly Color InspectorBg        = new(0.22f, 0.22f, 0.22f);
        static readonly Color SelectionHighlight = new(0.24f, 0.49f, 0.91f, 0.85f);
        static readonly Color HoverHighlight     = new(0.3f, 0.3f, 0.3f, 0.4f);
        static readonly Color DropLineColor      = new(0.35f, 0.8f, 1f);
        static readonly Color DropLineInRowColor = new(0.3f, 0.75f, 1f);
        static readonly Color DividerColor       = new(0.1f, 0.1f, 0.1f);
        static readonly Color SeparatorColor     = new(0.13f, 0.13f, 0.13f);
        static readonly Color StatusRunning      = new(0.2f, 0.8f, 1f);
        static readonly Color StatusCompleted    = new(0.2f, 0.85f, 0.3f);
        static readonly Color StatusPending      = new(0.45f, 0.45f, 0.45f);
        static readonly Color DisabledTextColor  = new(0.5f, 0.5f, 0.5f);
        static readonly Color ErrorTextColor     = new(1f, 0.25f, 0.25f);
        static readonly Color DeleteBtnColor     = new(1f, 0.4f, 0.4f);
        static readonly Color FoldoutArrowColor  = new(0.8f, 0.8f, 0.8f, 0.8f);

        // Strings
        const string RenameControlName = "RenameField";
        const string WindowTitle       = "Task Tree";
        const string MenuPath          = "Window/Task Tree Editor";

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  STATE
        // ══════════════════════════════════════════════════════════════════

        #region State

        TaskTree   _taskTree;
        [SerializeField] TaskDefinitionDatabase _taskDefinitionDatabase;
        ATaskNode  _selected;

        // Hierarchy
        float      _hierarchyWidth   = 300f;
        bool       _resizingDivider;
        Vector2    _hierarchyScroll;
        Rect       _hierarchyScrollRect;
        Dictionary<ATaskNode, bool> _expanded = new();

        // Search
        string _searchFilter = "";

        // Rename
        ATaskNode  _renamingNode;
        string     _renameBuffer;
        bool       _focusRenameField;

        // Drag-drop
        ATaskNode  _draggedNode;
        bool       _dragActive;
        Vector2    _dragStartPos;
        CompositeTaskNode _dropParentTarget;
        int        _dropInsertIndex;
        bool       _dropValid;

        // Clipboard
        ATaskNode  _clipboard;

        // Inspector
        bool       _showInspector;
        Vector2    _inspectorScroll;
        SerializedObject _serializedTaskTree;

        // Repaint throttle (Play Mode)
        double _lastRepaint;

        // Styles
        GUIStyle _labelStyle;
        GUIStyle _dimLabelStyle;
        GUIStyle _headerStyle;
        GUIStyle _sectionStyle;
        GUIStyle _renameStyle;
        GUIStyle _foldoutArrowStyle;
        GUIStyle _statusDotStyle;
        GUIStyle _nodeNameStyle;
        bool     _stylesBuilt;

        // Caches
        Dictionary<ATaskNode, bool> _enabledCache = new();

#if ODIN_INSPECTOR
        // Odin PropertyTree để vẽ taskDefinition theo style Odin
        Sirenix.OdinInspector.Editor.PropertyTree _odinTaskDefTree;
        object _odinTaskDefTarget;
#endif

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  OPEN
        // ══════════════════════════════════════════════════════════════════

        #region Open

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var win = GetWindow<TaskTreeEditorWindow>(WindowTitle);
            win.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
            win.Show();
        }

        public static void OpenWith(TaskTree tree)
        {
            var win = GetWindow<TaskTreeEditorWindow>(WindowTitle);
            win.minSize = new Vector2(MinWindowWidth, MinWindowHeight);
            win._taskTree = tree;
            win.RebuildSerializedObject();
            win.ClearEditState();
            win.Show();
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  UNITY LIFECYCLE
        // ══════════════════════════════════════════════════════════════════

        #region Lifecycle

        void OnEnable()
        {
            EditorApplication.update               += OnUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Undo.undoRedoPerformed                 += OnUndoRedo;
            EnsureTaskDefinitionDatabase();
            RebuildSerializedObject();
        }

        void OnDisable()
        {
            EditorApplication.update               -= OnUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Undo.undoRedoPerformed                 -= OnUndoRedo;
#if ODIN_INSPECTOR
            DisposeOdinTaskDefTree();
#endif
        }

        void OnSelectionChange()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var tree = go.GetComponent<TaskTree>();
            if (tree != null && tree != _taskTree)
            {
                _taskTree = tree;
                RebuildSerializedObject();
                ClearEditState();
                Repaint();
            }
        }

        void OnUpdate()
        {
            if (!Application.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaint < RepaintInterval) return;
            _lastRepaint = now;
            Repaint();
        }

        void OnPlayModeChanged(PlayModeStateChange _) => Repaint();

        void OnUndoRedo()
        {
            // Undo có thể thay đổi data bên dưới → refresh SerializedObject
            RebuildSerializedObject();
#if ODIN_INSPECTOR
            DisposeOdinTaskDefTree();
#endif
            Repaint();
        }

        void ClearEditState()
        {
            _selected         = null;
            _renamingNode     = null;
            _renameBuffer     = null;
            _focusRenameField = false;
            _dragActive       = false;
            _draggedNode      = null;
            _dropValid        = false;
            _clipboard        = null;
            _searchFilter     = "";
            _expanded.Clear();
            _enabledCache.Clear();
            _inspectorScroll  = Vector2.zero;
            _hierarchyScroll  = Vector2.zero;
#if ODIN_INSPECTOR
            DisposeOdinTaskDefTree();
#endif
        }

        void RebuildSerializedObject()
        {
            _serializedTaskTree = _taskTree != null ? new SerializedObject(_taskTree) : null;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  ONGUI
        // ══════════════════════════════════════════════════════════════════

        #region OnGUI

        void OnGUI()
        {
            BuildStyles();
            RebuildEnabledCache();
            HandleKeyboard();
            HandleDragInHierarchy();
            HandleTaskTreeSelector();

            if (_taskTree == null)
            {
                DrawNoTarget();
                return;
            }

            var totalRect = new Rect(0, EditorGUIUtility.singleLineHeight + ToolbarExtraHeight,
                position.width,
                position.height - EditorGUIUtility.singleLineHeight - ToolbarExtraHeight);

            // Capture layout mode at Layout event to keep GUILayout groups consistent
            // between Layout and Repaint passes within the same frame.
            if (Event.current.type == EventType.Layout)
                _showInspector = _selected != null;

            if (_showInspector)
            {
                _hierarchyWidth = Mathf.Clamp(_hierarchyWidth, MinHierarchyWidth,
                    totalRect.width - MinInspectorWidth - DividerWidth);
                var hierarchyRect = new Rect(totalRect.x, totalRect.y, _hierarchyWidth, totalRect.height);
                var dividerRect   = new Rect(_hierarchyWidth, totalRect.y, DividerWidth, totalRect.height);
                var inspectorRect = new Rect(_hierarchyWidth + DividerWidth, totalRect.y,
                    totalRect.width - _hierarchyWidth - DividerWidth, totalRect.height);

                DrawHierarchy(hierarchyRect);
                DrawDivider(dividerRect);
                DrawInspector(inspectorRect);
                HandleDividerResize(dividerRect);
            }
            else
            {
                DrawHierarchy(totalRect);
            }
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  TOOLBAR
        // ══════════════════════════════════════════════════════════════════

        #region Toolbar

        void HandleTaskTreeSelector()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();
            var newTree = (TaskTree)EditorGUILayout.ObjectField(
                _taskTree, typeof(TaskTree), allowSceneObjects: true,
                GUILayout.Width(ToolbarObjectFieldW));
            if (EditorGUI.EndChangeCheck())
            {
                _taskTree = newTree;
                RebuildSerializedObject();
                ClearEditState();
            }

            EditorGUI.BeginChangeCheck();
            var newDatabase = (TaskDefinitionDatabase)EditorGUILayout.ObjectField(
                _taskDefinitionDatabase, typeof(TaskDefinitionDatabase), allowSceneObjects: false,
                GUILayout.Width(ToolbarObjectFieldW));
            if (EditorGUI.EndChangeCheck())
                _taskDefinitionDatabase = newDatabase;

            GUILayout.FlexibleSpace();

            if (_taskTree != null && Application.isPlaying)
            {
                if (GUILayout.Button("▶  Execute", EditorStyles.toolbarButton, GUILayout.Width(ExecuteButtonW)))
                    _taskTree.Execute();
            }

            GUILayout.EndHorizontal();
        }

        void DrawNoTarget()
        {
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Select a TaskTree component above.", EditorStyles.largeLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  HIERARCHY PANEL
        // ══════════════════════════════════════════════════════════════════

        #region Hierarchy

        void DrawHierarchy(Rect rect)
        {
            EditorGUI.DrawRect(rect, HierarchyBg);

            // Header
            var headerRect = new Rect(rect.x, rect.y, rect.width, PanelHeaderHeight);
            EditorGUI.DrawRect(headerRect, PanelHeaderBg);
            GUI.Label(headerRect, "  Hierarchy", _headerStyle);

            float contentTop = rect.y + PanelHeaderHeight;

            // Search bar (EditorGUI để set editingTextField, ngăn HandleKeyboard ăn phím)
            var searchRect = new Rect(rect.x + 2, contentTop + 1, rect.width - 4, SearchBarHeight - 2);
            _searchFilter = EditorGUI.TextField(searchRect, _searchFilter, EditorStyles.toolbarSearchField);
            contentTop += SearchBarHeight;

            // Scroll area
            var scrollRect = new Rect(rect.x, contentTop, rect.width, rect.height - PanelHeaderHeight - SearchBarHeight);
            _hierarchyScrollRect = scrollRect;
            _hierarchyScroll = GUI.BeginScrollView(scrollRect, _hierarchyScroll,
                new Rect(0, 0, scrollRect.width - 16, GetTreeContentHeight(_taskTree?.Root, 0)));

            float y = 0;
            if (_taskTree.Root != null)
                DrawNodeRow(_taskTree.Root, null, -1, 0, ref y, scrollRect.width);
            else
                GUI.Label(new Rect(8, y, scrollRect.width, RowHeight),
                    "(empty — right-click to create root)", _dimLabelStyle);

            GUI.EndScrollView();

            // Context menu on right-click
            var e = Event.current;
            if (e.type == EventType.ContextClick && scrollRect.Contains(e.mousePosition))
            {
                var localPos = ScreenToScrollLocal(e.mousePosition);
                ShowContextMenu(HitTestNode(_taskTree?.Root, localPos, 0));
                e.Use();
            }

            // Click on empty space → deselect
            if (e.type == EventType.MouseDown && e.button == 0 && scrollRect.Contains(e.mousePosition))
            {
                var localPos = ScreenToScrollLocal(e.mousePosition);
                var hit = HitTestNode(_taskTree?.Root, localPos, 0);
                if (hit == null)
                {
                    CommitRename();
                    _selected = null;
                    ClearTextFieldFocus();
                    Repaint();
                }
            }

            // Drop indicator line
            if (_dragActive && _dropValid)
                DrawDropIndicator(rect, scrollRect);
        }

        // ── Row drawing (recursive) ──

        float GetTreeContentHeight(ATaskNode node, int depth)
        {
            if (node == null) return RowHeight;
            if (!IsVisibleBySearch(node)) return 0;
            float h = RowHeight;
            if (node is CompositeTaskNode comp && IsExpanded(comp) && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch?.taskNode != null)
                        h += GetTreeContentHeight(ch.taskNode, depth + 1);
            return h;
        }

        void DrawNodeRow(ATaskNode node, CompositeTaskNode parent, int indexInParent,
                         int depth, ref float y, float width)
        {
            if (!IsVisibleBySearch(node)) return;

            var e        = Event.current;
            var rowRect  = new Rect(0, y, width, RowHeight);
            float indent = RowPadLeft + depth * IndentStep;

            bool isSelected = node == _selected;
            bool isEnabled  = IsNodeHierarchyEnabled(node);
            bool isRoot     = _taskTree != null && node == _taskTree.Root;

            // Selection/hover background
            if (isSelected)
                EditorGUI.DrawRect(rowRect, SelectionHighlight);
            else if (rowRect.Contains(e.mousePosition) && e.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, HoverHighlight);

            // Drop highlight
            if (_dragActive && _dropValid && _dropParentTarget == parent &&
                _dropInsertIndex == indexInParent)
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y - 1, rowRect.width, 2), DropLineInRowColor);

            float cx = indent;

            // Foldout triangle
            if (node is CompositeTaskNode compNode)
            {
                bool expanded = IsExpanded(compNode);
                var  foldRect = new Rect(cx, y + 2, FoldoutW, RowHeight - 2);
                if (e.type == EventType.MouseDown && e.button == 0 && foldRect.Contains(e.mousePosition))
                {
                    SetExpanded(compNode, !expanded);
                    CommitRename();
                    e.Use();
                }
                if (e.type == EventType.Repaint)
                    GUI.Label(foldRect, expanded ? "▼" : "▶", _foldoutArrowStyle);
                cx += FoldoutW;
            }
            else cx += FoldoutW;

            // Status dot (Play Mode)
            if (Application.isPlaying)
            {
                Color dotColor = node.Status switch
                {
                    TaskNodeStatus.Running   => StatusRunning,
                    TaskNodeStatus.Completed => StatusCompleted,
                    _                        => StatusPending,
                };
                if (e.type == EventType.Repaint)
                {
                    _statusDotStyle.normal.textColor = dotColor;
                    GUI.Label(new Rect(cx, y + 3, 12, 14), "●", _statusDotStyle);
                }
                cx += StatusDotW;
            }

            // Type badge
            string badge = node switch
            {
                CompositeTaskNode c => c.executionMode == ExecutionMode.Sequential ? "[Seq]" : "[Par]",
                MonoTaskNode        => "[Mono]",
                _                   => "[?]",
            };
            if (isRoot) badge = "[Root]";
            if (e.type == EventType.Repaint)
                GUI.Label(new Rect(cx, y, BadgeW, RowHeight), badge, _dimLabelStyle);
            cx += BadgeW + BadgePadding;

            // Name (label or rename field)
            float nameX = cx;
            float nameW = width - cx - 4;

            if (_renamingNode == node)
            {
                var renameRect = new Rect(nameX, y + 1, nameW, RowHeight - 2);
                GUI.SetNextControlName(RenameControlName);
                _renameBuffer = GUI.TextField(renameRect, _renameBuffer, _renameStyle);

                if (_focusRenameField && e.type == EventType.Repaint)
                {
                    EditorGUI.FocusTextInControl(RenameControlName);
                    _focusRenameField = false;
                }

                // Click outside rename field → commit
                if (e.type == EventType.MouseDown && !renameRect.Contains(e.mousePosition))
                    CommitRename();
            }
            else
            {
                string displayName = string.IsNullOrEmpty(node.name) ? string.Empty : node.name;
                if (e.type == EventType.Repaint)
                {
                    if (!isEnabled || isRoot)
                        _nodeNameStyle.normal.textColor = DisabledTextColor;
                    else if (node is MonoTaskNode mono && mono.taskDefinition == null)
                        _nodeNameStyle.normal.textColor = ErrorTextColor;
                    else if (isSelected)
                        _nodeNameStyle.normal.textColor = Color.white;
                    else
                        _nodeNameStyle.normal.textColor = _labelStyle.normal.textColor;

                    GUI.Label(new Rect(nameX, y, nameW, RowHeight), displayName, _nodeNameStyle);
                }
            }

            // Mouse events on row
            if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
            {
                if (e.button == 0)
                {
                    if (_renamingNode != node) CommitRename();

                    if (e.clickCount == 2 && node == _selected)
                    {
                        StartRename(node);
                        e.Use();
                    }
                    else
                    {
                        _selected = node;
                        ClearTextFieldFocus();
                        // Bắt đầu track drag (threshold check ở HandleDragInHierarchy)
                        _dragActive   = false;
                        _dragStartPos = e.mousePosition;
                        _draggedNode  = node;
                        e.Use();
                        Repaint();
                    }
                }
                else if (e.button == 1)
                {
                    if (_selected != node) { _selected = node; Repaint(); }
                }
            }

            y += RowHeight;

            // Children (recursive)
            if (node is CompositeTaskNode composite && IsExpanded(composite) && composite.children != null)
            {
                for (int i = 0; i < composite.children.Count; i++)
                {
                    var child = composite.children[i];
                    if (child?.taskNode != null)
                        DrawNodeRow(child.taskNode, composite, i, depth + 1, ref y, width);
                }
            }
        }

        // ── Hit test ──

        ATaskNode HitTestNode(ATaskNode node, Vector2 localPos, float startY)
        {
            return HitTestRec(node, localPos, ref startY);
        }

        ATaskNode HitTestRec(ATaskNode node, Vector2 pos, ref float y)
        {
            if (node == null) return null;
            if (!IsVisibleBySearch(node)) return null;
            var rowRect = new Rect(0, y, 10000, RowHeight);
            y += RowHeight;
            if (rowRect.Contains(pos)) return node;
            if (node is CompositeTaskNode comp && IsExpanded(comp) && comp.children != null)
                foreach (var ch in comp.children)
                {
                    if (ch?.taskNode == null) continue;
                    var result = HitTestRec(ch.taskNode, pos, ref y);
                    if (result != null) return result;
                }
            return null;
        }

        // ── Drop indicator ──

        void DrawDropIndicator(Rect hierarchyRect, Rect scrollRect)
        {
            float y    = 0;
            float lineY = ComputeDropLineY(_taskTree.Root, null, -1, ref y);
            if (lineY < 0) return;
            float absY = scrollRect.y + lineY - _hierarchyScroll.y;
            if (absY < scrollRect.y || absY > scrollRect.yMax) return;
            EditorGUI.DrawRect(new Rect(scrollRect.x + 4, absY - 1, scrollRect.width - 8, 2), DropLineColor);
        }

        float ComputeDropLineY(ATaskNode node, CompositeTaskNode parent, int indexInParent, ref float y)
        {
            if (node == null) return -1;
            float thisY = y;
            y += RowHeight;
            if (_dropParentTarget == parent && _dropInsertIndex == indexInParent) return thisY;
            if (node is CompositeTaskNode comp && IsExpanded(comp) && comp.children != null)
            {
                for (int i = 0; i < comp.children.Count; i++)
                {
                    var ch = comp.children[i];
                    if (ch?.taskNode == null) continue;
                    float result = ComputeDropLineY(ch.taskNode, comp, i, ref y);
                    if (result >= 0) return result;
                }
                if (_dropParentTarget == comp && _dropInsertIndex == (comp.children?.Count ?? 0))
                    return y;
            }
            return -1;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  DRAG & DROP
        // ══════════════════════════════════════════════════════════════════

        #region DragDrop

        void HandleDragInHierarchy()
        {
            if (_draggedNode == null) return;
            var e = Event.current;

            // MouseUp khi chưa drag (click thường) → clear tracking
            if (!_dragActive && e.type == EventType.MouseUp)
            {
                _draggedNode = null;
                return;
            }

            // MouseDown ngoài hierarchy → hủy drag tracking
            if (e.type == EventType.MouseDown && !_hierarchyScrollRect.Contains(e.mousePosition))
            {
                CancelDrag();
                return;
            }

            // Phase 1: Detect drag start (threshold check)
            if (!_dragActive && e.type == EventType.MouseDrag)
            {
                if (!_hierarchyScrollRect.Contains(e.mousePosition))
                {
                    CancelDrag();
                    return;
                }
                if (((Vector2)e.mousePosition - _dragStartPos).sqrMagnitude > DragThreshSq)
                {
                    _dragActive = true;
                    _dropValid  = false;
                }
            }

            if (!_dragActive) return;

            // Phase 2: Active drag
            if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove)
            {
                UpdateDropTarget(e.mousePosition);
                Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                if (e.button == 0 && _dropValid)
                    PerformDrop();
                CancelDrag();
                Repaint();
                e.Use();
            }
        }

        void CancelDrag()
        {
            _dragActive  = false;
            _draggedNode = null;
            _dropValid   = false;
        }

        void UpdateDropTarget(Vector2 mousePos)
        {
            if (_taskTree == null || _taskTree.Root == null) return;
            var localY = ScreenToScrollLocal(mousePos).y;
            float y = 0f;
            _dropValid = false;
            FindDropTarget(_taskTree.Root, null, -1, ref y, localY);
        }

        void FindDropTarget(ATaskNode node, CompositeTaskNode parent, int indexInParent,
                             ref float y, float mouseY)
        {
            if (node == null) return;
            float rowTop = y;
            y += RowHeight;

            // Upper half → insert before
            if (mouseY >= rowTop && mouseY < rowTop + RowHeight * 0.5f)
            {
                if (parent != null && _draggedNode != node && !IsAncestorOrSelf(_draggedNode, parent))
                {
                    _dropParentTarget = parent;
                    _dropInsertIndex  = indexInParent;
                    _dropValid        = true;
                }
            }
            // Lower half → insert into (if composite & expanded) or after
            else if (mouseY >= rowTop + RowHeight * 0.5f && mouseY < rowTop + RowHeight)
            {
                if (node is CompositeTaskNode comp && IsExpanded(comp) && _draggedNode != node &&
                    !IsAncestorOrSelf(_draggedNode, comp))
                {
                    _dropParentTarget = comp;
                    _dropInsertIndex  = 0;
                    _dropValid        = true;
                }
                else if (parent != null && _draggedNode != node && !IsAncestorOrSelf(_draggedNode, parent))
                {
                    _dropParentTarget = parent;
                    _dropInsertIndex  = indexInParent + 1;
                    _dropValid        = true;
                }
            }

            if (node is CompositeTaskNode compNode && IsExpanded(compNode) && compNode.children != null)
                for (int i = 0; i < compNode.children.Count; i++)
                {
                    var ch = compNode.children[i];
                    if (ch?.taskNode != null)
                        FindDropTarget(ch.taskNode, compNode, i, ref y, mouseY);
                }
        }

        void PerformDrop()
        {
            if (_dropParentTarget == null || _draggedNode == null) return;

            FindParent(_taskTree.Root, _draggedNode, out var oldParent, out int oldIdx);
            if (oldParent == _dropParentTarget && oldIdx == _dropInsertIndex) return;

            Undo.RegisterCompleteObjectUndo(_taskTree, "Move Node");

            float sv = 1f;
            if (oldParent != null)
            {
                sv = oldParent.children[oldIdx].subTaskValue;
                oldParent.children.RemoveAt(oldIdx);
                if (oldParent == _dropParentTarget && oldIdx < _dropInsertIndex)
                    _dropInsertIndex--;
            }

            if (_dropParentTarget.children == null)
                _dropParentTarget.children = new List<CompositeTaskNode.Child>();
            _dropInsertIndex = Mathf.Clamp(_dropInsertIndex, 0, _dropParentTarget.children.Count);
            _dropParentTarget.children.Insert(_dropInsertIndex, new CompositeTaskNode.Child
            {
                subTaskValue = sv,
                taskNode     = _draggedNode,
            });

            PurgeExpandedDict();
            SetDirty();
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  CONTEXT MENU
        // ══════════════════════════════════════════════════════════════════

        #region ContextMenu

        void ShowContextMenu(ATaskNode hitNode)
        {
            var menu = new GenericMenu();

            if (_taskTree.Root == null)
            {
                menu.AddItem(new GUIContent("Create Root Composite (Sequential)"), false,
                    () => CreateRoot(ExecutionMode.Sequential));
                menu.AddItem(new GUIContent("Create Root Composite (Parallel)"), false,
                    () => CreateRoot(ExecutionMode.Parallel));
                menu.ShowAsContext();
                return;
            }

            ATaskNode target = hitNode ?? _selected;

            if (target is CompositeTaskNode comp)
            {
                menu.AddItem(new GUIContent("Add Child/Mono Task"), false,
                    () => AddMonoChild(comp));
                menu.AddItem(new GUIContent("Add Child/Composite (Sequential)"), false,
                    () => AddCompositeChild(comp, ExecutionMode.Sequential));
                menu.AddItem(new GUIContent("Add Child/Composite (Parallel)"), false,
                    () => AddCompositeChild(comp, ExecutionMode.Parallel));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Expand All Children"), false, () => ExpandAll(comp));
                menu.AddItem(new GUIContent("Collapse All Children"), false, () => CollapseAll(comp));
                menu.AddSeparator("");
            }

            if (target != null)
            {
                bool isRoot = _taskTree != null && target == _taskTree.Root;

                menu.AddItem(new GUIContent("Rename        F2"), false, () => StartRename(target));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Duplicate     Ctrl+D"), false, () => DuplicateNode(target));
                menu.AddItem(new GUIContent("Copy          Ctrl+C"), false, () => _clipboard = target);
                if (_clipboard != null && target is CompositeTaskNode compPaste)
                    menu.AddItem(new GUIContent("Paste as Child  Ctrl+V"), false, () => PasteChild(compPaste));
                else
                    menu.AddDisabledItem(new GUIContent("Paste as Child  Ctrl+V"));
                menu.AddSeparator("");

                if (isRoot)
                    menu.AddDisabledItem(new GUIContent("Delete        Del"));
                else
                    menu.AddItem(new GUIContent("Delete        Del"), false, () => DeleteNode(target));
            }

            menu.ShowAsContext();
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  KEYBOARD SHORTCUTS
        // ══════════════════════════════════════════════════════════════════

        #region Keyboard

        void HandleKeyboard()
        {
            if (_taskTree == null || _taskTree.Root == null) return;
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;

            // Khi đang rename → chỉ xử lý Enter/Escape, key khác để TextField xử lý
            if (_renamingNode != null)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                { CommitRename(); e.Use(); }
                else if (e.keyCode == KeyCode.Escape)
                { CancelRename(); e.Use(); }
                return;
            }

            // Đang gõ text field (search bar, inspector...) → không xử lý shortcuts
            if (EditorGUIUtility.editingTextField) return;

            bool ctrl = e.control || e.command;

            if (_selected == null)
            {
                _selected = _taskTree.Root;
                Repaint();
            }

            var visible = new List<ATaskNode>();
            BuildVisibleList(_taskTree.Root, visible);
            int curIndex = visible.IndexOf(_selected);

            // Delete (chỉ phím Delete)
            if (e.keyCode == KeyCode.Delete)
            { DeleteNode(_selected); e.Use(); return; }

            // Duplicate
            if (ctrl && e.keyCode == KeyCode.D)
            { DuplicateNode(_selected); e.Use(); return; }

            // Copy / Paste
            if (ctrl && e.keyCode == KeyCode.C)
            { _clipboard = _selected; e.Use(); return; }

            if (ctrl && e.keyCode == KeyCode.V && _selected is CompositeTaskNode cv)
            { PasteChild(cv); e.Use(); return; }

            // Rename (F2)
            if (e.keyCode == KeyCode.F2)
            { StartRename(_selected); e.Use(); return; }

            // Expand/Collapse All: Alt+Left / Alt+Right
            if (e.alt && e.keyCode == KeyCode.LeftArrow)
            { CollapseAll(_taskTree.Root); e.Use(); return; }
            if (e.alt && e.keyCode == KeyCode.RightArrow)
            { ExpandAll(_taskTree.Root); e.Use(); return; }

            // Navigate Up/Down
            if (curIndex >= 0)
            {
                if (e.keyCode == KeyCode.UpArrow)
                {
                    if (curIndex > 0) { _selected = visible[curIndex - 1]; Repaint(); }
                    e.Use(); return;
                }
                if (e.keyCode == KeyCode.DownArrow)
                {
                    if (curIndex < visible.Count - 1) { _selected = visible[curIndex + 1]; Repaint(); }
                    e.Use(); return;
                }
            }

            // Left: collapse or go to parent
            if (e.keyCode == KeyCode.LeftArrow)
            {
                if (_selected is CompositeTaskNode comp && IsExpanded(comp))
                    SetExpanded(comp, false);
                else
                {
                    FindParent(_taskTree.Root, _selected, out var parent, out _);
                    if (parent != null) _selected = parent;
                }
                Repaint(); e.Use(); return;
            }

            // Right: expand or go to first child
            if (e.keyCode == KeyCode.RightArrow)
            {
                if (_selected is CompositeTaskNode comp)
                {
                    if (!IsExpanded(comp))
                        SetExpanded(comp, true);
                    else if (comp.children != null && comp.children.Count > 0)
                    {
                        var firstChild = comp.children[0].taskNode;
                        if (firstChild != null) _selected = firstChild;
                    }
                }
                Repaint(); e.Use();
            }
        }

        void BuildVisibleList(ATaskNode node, List<ATaskNode> list)
        {
            if (node == null || !IsVisibleBySearch(node)) return;
            list.Add(node);
            if (node is CompositeTaskNode comp && IsExpanded(comp) && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch?.taskNode != null)
                        BuildVisibleList(ch.taskNode, list);
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  RENAME
        // ══════════════════════════════════════════════════════════════════

        #region Rename

        void StartRename(ATaskNode node)
        {
            CommitRename();
            _renamingNode     = node;
            _renameBuffer     = node.name ?? "";
            _focusRenameField = true;
            Repaint();
        }

        void CommitRename()
        {
            if (_renamingNode == null) return;

            var trimmed = _renameBuffer?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                Undo.RegisterCompleteObjectUndo(_taskTree, "Rename Node");
                _renamingNode.name = trimmed;
                SetDirty();
            }

            _renamingNode = null;
            ClearTextFieldFocus();
            Repaint();
        }

        void CancelRename()
        {
            _renamingNode = null;
            ClearTextFieldFocus();
            Repaint();
        }

        /// <summary>
        /// Clear keyboard control ID + editingTextField flag.
        /// Cần clear thủ công cả hai vì khi TextField bị xóa (không còn được vẽ),
        /// Unity IMGUI không tự reset editingTextField.
        /// </summary>
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

        void CreateRoot(ExecutionMode mode)
        {
            Undo.RegisterCompleteObjectUndo(_taskTree, "Create Root");
            _taskTree.Root = new CompositeTaskNode
            {
                name          = "Root",
                executionMode = mode,
                children      = new List<CompositeTaskNode.Child>(),
            };
            _selected = _taskTree.Root;
            SetExpanded(_taskTree.Root, true);
            SetDirty();
        }

        void AddMonoChild(CompositeTaskNode parent)
        {
            Undo.RegisterCompleteObjectUndo(_taskTree, "Add Mono Task");
            if (parent.children == null) parent.children = new List<CompositeTaskNode.Child>();
            var node = new MonoTaskNode { name = "New Mono Task" };
            parent.children.Add(new CompositeTaskNode.Child { subTaskValue = 1f, taskNode = node });
            SetExpanded(parent, true);
            SetDirty();
        }

        void AddCompositeChild(CompositeTaskNode parent, ExecutionMode mode)
        {
            Undo.RegisterCompleteObjectUndo(_taskTree, "Add Composite Task");
            if (parent.children == null) parent.children = new List<CompositeTaskNode.Child>();
            var node = new CompositeTaskNode
            {
                name          = "New Composite",
                executionMode = mode,
                children      = new List<CompositeTaskNode.Child>(),
            };
            parent.children.Add(new CompositeTaskNode.Child { subTaskValue = 1f, taskNode = node });
            SetExpanded(parent, true);
            SetExpanded(node, true);
            SetDirty();
        }

        void DeleteNode(ATaskNode node)
        {
            if (node == null || node == _taskTree.Root) return;

            FindParent(_taskTree.Root, node, out var parent, out int idx);
            if (parent == null) return;

            Undo.RegisterCompleteObjectUndo(_taskTree, "Delete Node");
            parent.children.RemoveAt(idx);

            if (_selected == node || (_selected != null && IsDescendant(node, _selected)))
                _selected = parent;

            PurgeExpandedDict();
            SetDirty();
        }

        void DuplicateNode(ATaskNode node)
        {
            if (node == null || node == _taskTree.Root) return;

            FindParent(_taskTree.Root, node, out var parent, out int idx);
            if (parent == null) return;

            var clone = DeepClone(node);
            Undo.RegisterCompleteObjectUndo(_taskTree, "Duplicate Node");
            parent.children.Insert(idx + 1, new CompositeTaskNode.Child
            {
                enabled      = parent.children[idx].enabled,
                subTaskValue = parent.children[idx].subTaskValue,
                taskNode     = clone,
            });
            clone.name = MakeUniqueSiblingName(parent, node.name);
            _selected = clone;
            SetDirty();
        }

        void PasteChild(CompositeTaskNode parent)
        {
            if (_clipboard == null) return;
            var clone = DeepClone(_clipboard);

            Undo.RegisterCompleteObjectUndo(_taskTree, "Paste Node");
            if (parent.children == null) parent.children = new List<CompositeTaskNode.Child>();

            bool enabled = true;
            if (_taskTree?.Root != null)
            {
                FindParent(_taskTree.Root, _clipboard, out var oldParent, out int oldIdx);
                if (oldParent?.children != null && oldIdx >= 0 && oldIdx < oldParent.children.Count)
                    enabled = oldParent.children[oldIdx].enabled;
            }

            parent.children.Add(new CompositeTaskNode.Child
            {
                enabled      = enabled,
                subTaskValue = 1f,
                taskNode     = clone,
            });
            SetExpanded(parent, true);
            clone.name = MakeUniqueSiblingName(parent, _clipboard.name);
            _selected = clone;
            SetDirty();
        }

        void ExpandAll(ATaskNode node)
        {
            if (node is CompositeTaskNode comp)
            {
                SetExpanded(comp, true);
                if (comp.children != null)
                    foreach (var ch in comp.children)
                        if (ch?.taskNode != null)
                            ExpandAll(ch.taskNode);
            }
            Repaint();
        }

        void CollapseAll(ATaskNode node)
        {
            if (node is CompositeTaskNode comp)
            {
                SetExpanded(comp, false);
                if (comp.children != null)
                    foreach (var ch in comp.children)
                        if (ch?.taskNode != null)
                            CollapseAll(ch.taskNode);
            }
            Repaint();
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  INSPECTOR PANEL
        // ══════════════════════════════════════════════════════════════════

        #region Inspector

        void DrawInspector(Rect rect)
        {
            // Validate selection vẫn còn trong tree
            if (_selected != null && _taskTree?.Root != null)
            {
                if (_selected != _taskTree.Root && !IsDescendant(_taskTree.Root, _selected))
                    _selected = null;
            }

            if (_selected == null) return;

            EditorGUI.DrawRect(rect, InspectorBg);

            var headerRect = new Rect(rect.x, rect.y, rect.width, PanelHeaderHeight);
            EditorGUI.DrawRect(headerRect, PanelHeaderBg);
            GUI.Label(headerRect, "  Inspector", _headerStyle);

            var bodyRect = new Rect(rect.x, rect.y + PanelHeaderHeight, rect.width, rect.height - PanelHeaderHeight);
            GUILayout.BeginArea(bodyRect);
            _inspectorScroll = GUILayout.BeginScrollView(_inspectorScroll);

            DrawInspectorContent(_selected);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawInspectorContent(ATaskNode node)
        {
            string typeLabel = node switch
            {
                CompositeTaskNode => "[Composite]",
                MonoTaskNode      => "[MonoTask]",
                _                 => "[Unknown]",
            };
            EditorGUILayout.LabelField($"{typeLabel}  {node.name}", EditorStyles.boldLabel);
            GUILayout.Space(4);
            SeparatorLine();
            GUILayout.Space(6);

            if (node is MonoTaskNode mono)
                DrawMonoInspector(mono);
            else if (node is CompositeTaskNode comp)
                DrawCompositeInspector(comp);

            // Runtime (Play Mode)
            if (Application.isPlaying)
            {
                GUILayout.Space(8);
                SeparatorLine();
                GUILayout.Space(6);
                SectionHeader("Runtime");
                GUILayout.Space(4);
                DrawRuntimeInspector(node);
            }

            GUILayout.Space(16);
        }

        void DrawMonoInspector(MonoTaskNode mono)
        {
            var types = GetTaskDefinitionTypes(out var names);

            int curIdx = IndexOfType(types, mono.taskDefinition?.GetType());

            EditorGUILayout.LabelField("Task Definition:", EditorStyles.boldLabel);

            if (types.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No ITaskDefinition implementations found in current TaskDefinitionDatabase.",
                    MessageType.Info);
                return;
            }

            var options = new string[names.Length + 1];
            options[0] = "None";
            Array.Copy(names, 0, options, 1, names.Length);

            int popupIndex = mono.taskDefinition == null ? 0 : (curIdx >= 0 ? curIdx + 1 : 0);
            int newPopupIndex = EditorGUILayout.Popup("Type", popupIndex, options);

            if (newPopupIndex != popupIndex)
            {
                Undo.RegisterCompleteObjectUndo(_taskTree, "Change taskDefinition");

                if (newPopupIndex == 0)
                {
                    mono.taskDefinition = null;
                }
                else
                {
                    int typeIndex = newPopupIndex - 1;
                    if (typeIndex >= 0 && typeIndex < types.Length)
                    {
                        try
                        {
                            mono.taskDefinition = (ITaskDefinition)Activator.CreateInstance(types[typeIndex]);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Failed to create ITaskDefinition of type {types[typeIndex].Name}: {ex.Message}");
                            mono.taskDefinition = null;
                        }
                    }
                }
                SetDirty();
                // Refresh vì data thay đổi ngoài SerializedProperty
                RebuildSerializedObject();
#if ODIN_INSPECTOR
                DisposeOdinTaskDefTree();
#endif
            }

            if (mono.taskDefinition != null)
            {
                GUILayout.Space(6);
                DrawTaskDefinitionFields(mono);
            }
        }

        Type[] GetTaskDefinitionTypes(out string[] displayNames)
        {
            displayNames = Array.Empty<string>();
            if (_taskDefinitionDatabase == null || _taskDefinitionDatabase.entries == null)
                return Array.Empty<Type>();

            var typeList = new List<Type>();
            var nameList = new List<string>();

            foreach (var entry in _taskDefinitionDatabase.entries)
            {
                if (entry?.script == null) continue;
                var type = entry.script.GetClass();
                if (type == null || type.IsAbstract || type.IsInterface) continue;
                if (!typeof(ITaskDefinition).IsAssignableFrom(type)) continue;

                typeList.Add(type);
                nameList.Add(string.IsNullOrEmpty(entry.displayName) ? type.Name : entry.displayName);
            }

            displayNames = nameList.ToArray();
            return typeList.ToArray();
        }

        static int IndexOfType(Type[] types, Type t)
        {
            if (types == null || t == null) return -1;
            for (int i = 0; i < types.Length; i++)
                if (types[i] == t) return i;
            return -1;
        }

        void DrawCompositeInspector(CompositeTaskNode comp)
        {
            EditorGUI.BeginChangeCheck();
            var newMode = (ExecutionMode)EditorGUILayout.EnumPopup("Execution Mode", comp.executionMode);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(_taskTree, "Change executionMode");
                comp.executionMode = newMode;
                SetDirty();
            }

            GUILayout.Space(8);
            EditorGUILayout.LabelField($"Children ({comp.children?.Count ?? 0})", EditorStyles.boldLabel);

            if (comp.children != null)
            {
                for (int i = 0; i < comp.children.Count; i++)
                {
                    var child = comp.children[i];
                    if (child?.taskNode == null) continue;

                    int ci = i;
                    GUILayout.BeginHorizontal();

                    // Enabled toggle
                    EditorGUI.BeginChangeCheck();
                    bool newEnabled = GUILayout.Toggle(child.enabled, GUIContent.none, GUILayout.Width(18));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RegisterCompleteObjectUndo(_taskTree, "Toggle Child Enabled");
                        comp.children[ci].enabled = newEnabled;
                        SetDirty();
                    }

                    // Name
                    EditorGUI.BeginChangeCheck();
                    string childName = EditorGUILayout.TextField(child.taskNode.name ?? string.Empty);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RegisterCompleteObjectUndo(_taskTree, "Rename Child Node");
                        child.taskNode.name = childName;
                        SetDirty();
                    }

                    // subTaskValue
                    EditorGUI.BeginChangeCheck();
                    float newSv = EditorGUILayout.FloatField(child.subTaskValue, GUILayout.Width(48));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RegisterCompleteObjectUndo(_taskTree, "Edit subTaskValue");
                        comp.children[ci].subTaskValue = Mathf.Max(0f, newSv);
                        SetDirty();
                    }

                    // Delete
                    var oldColor = GUI.color;
                    GUI.color = DeleteBtnColor;
                    if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        Undo.RegisterCompleteObjectUndo(_taskTree, "Remove Child");
                        if (_selected == comp.children[ci].taskNode) _selected = comp;
                        comp.children.RemoveAt(ci);
                        PurgeExpandedDict();
                        SetDirty();
                        GUILayout.EndHorizontal();
                        GUI.color = oldColor;
                        break;
                    }
                    GUI.color = oldColor;
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Mono Task"))        AddMonoChild(comp);
            if (GUILayout.Button("+ Composite Task"))   AddCompositeChild(comp, ExecutionMode.Sequential);
            GUILayout.EndHorizontal();
        }

        void DrawRuntimeInspector(ATaskNode node)
        {
            Color statusColor = node.Status switch
            {
                TaskNodeStatus.Running   => StatusRunning,
                TaskNodeStatus.Completed => StatusCompleted,
                _                        => new Color(0.6f, 0.6f, 0.6f),
            };
            var oldColor = GUI.contentColor;
            GUI.contentColor = statusColor;
            EditorGUILayout.LabelField("Status", node.Status.ToString(), EditorStyles.boldLabel);
            GUI.contentColor = oldColor;

            EditorGUILayout.LabelField("Progress", $"{node.Progress * 100f:F1} %");
            var progressRect = EditorGUILayout.GetControlRect(false, 16);
            progressRect = EditorGUI.IndentedRect(progressRect);
            EditorGUI.DrawRect(progressRect, PanelHeaderBg);
            if (node.Progress > 0)
                EditorGUI.DrawRect(
                    new Rect(progressRect.x, progressRect.y,
                             progressRect.width * node.Progress, progressRect.height),
                    StatusRunning);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Force Complete"))           node.ForceComplete();
            if (GUILayout.Button("Force Complete Immediate")) node.ForceCompleteImmediate();
            if (GUILayout.Button("Reset"))                    node.Reset();
            GUILayout.EndHorizontal();
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  TASK DEFINITION FIELD DRAWING
        // ══════════════════════════════════════════════════════════════════

        #region TaskDefinitionDrawing

        /// <summary>
        /// Vẽ các field của taskDefinition.
        /// Odin (khi có): dùng PropertyTree để tận dụng Odin drawer.
        /// Không Odin: dùng SerializedProperty / PropertyField mặc định của Unity.
        /// </summary>
        void DrawTaskDefinitionFields(MonoTaskNode mono)
        {
            if (mono.taskDefinition == null)
            {
#if ODIN_INSPECTOR
                DisposeOdinTaskDefTree();
#endif
                return;
            }

#if ODIN_INSPECTOR
            DrawTaskDefinitionFieldsOdin(mono);
#else
            DrawTaskDefinitionFieldsDefault(mono);
#endif
        }

#if ODIN_INSPECTOR
        void DrawTaskDefinitionFieldsOdin(MonoTaskNode mono)
        {
            // Tạo lại PropertyTree khi target thay đổi (chọn node khác hoặc đổi type)
            if (_odinTaskDefTree == null || !ReferenceEquals(_odinTaskDefTarget, mono.taskDefinition))
            {
                DisposeOdinTaskDefTree();
                _odinTaskDefTree = Sirenix.OdinInspector.Editor.PropertyTree.Create(mono.taskDefinition);
                _odinTaskDefTarget = mono.taskDefinition;
            }

            EditorGUI.BeginChangeCheck();
            _odinTaskDefTree.Draw(applyUndo: false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(_taskTree, "Edit TaskDefinition");
                _odinTaskDefTree.ApplyChanges();
                SetDirty();
            }
        }

        void DisposeOdinTaskDefTree()
        {
            _odinTaskDefTree?.Dispose();
            _odinTaskDefTree = null;
            _odinTaskDefTarget = null;
        }
#endif

        void DrawTaskDefinitionFieldsDefault(MonoTaskNode mono)
        {
            if (_serializedTaskTree == null) return;

            _serializedTaskTree.Update();

            string nodePath = FindNodePropertyPath(mono);
            if (nodePath == null) return;

            var taskDefProp = _serializedTaskTree.FindProperty(nodePath + ".taskDefinition");
            if (taskDefProp == null) return;

            EditorGUILayout.PropertyField(taskDefProp, true);

            if (_serializedTaskTree.ApplyModifiedProperties())
                SetDirty();
        }

        /// <summary>
        /// Tìm property path từ root SerializedObject đến một ATaskNode.
        /// Ví dụ: "&lt;Root&gt;k__BackingField.children.Array.data[0].taskNode"
        /// </summary>
        string FindNodePropertyPath(ATaskNode target)
        {
            if (_taskTree == null || _taskTree.Root == null) return null;

            const string rootPath = "<Root>k__BackingField";
            if (target == _taskTree.Root) return rootPath;

            var pathParts = new List<string>();
            if (FindNodePathRecursive(_taskTree.Root, target, pathParts))
                return rootPath + string.Join("", pathParts);

            return null;
        }

        bool FindNodePathRecursive(ATaskNode current, ATaskNode target, List<string> pathParts)
        {
            if (current is not CompositeTaskNode comp || comp.children == null) return false;

            for (int i = 0; i < comp.children.Count; i++)
            {
                var child = comp.children[i];
                if (child?.taskNode == null) continue;

                pathParts.Add($".children.Array.data[{i}].taskNode");

                if (child.taskNode == target) return true;
                if (FindNodePathRecursive(child.taskNode, target, pathParts)) return true;

                pathParts.RemoveAt(pathParts.Count - 1);
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
                {
                    name                      = m.name,
                    targetProgressToComplete   = m.targetProgressToComplete,
                    taskDefinition             = CopyTaskDefinition(m.taskDefinition),
                };
            if (src is CompositeTaskNode c)
            {
                var clone = new CompositeTaskNode
                {
                    name                      = c.name,
                    targetProgressToComplete   = c.targetProgressToComplete,
                    executionMode              = c.executionMode,
                    children                   = new List<CompositeTaskNode.Child>(),
                };
                if (c.children != null)
                    foreach (var ch in c.children)
                        if (ch?.taskNode != null)
                            clone.children.Add(new CompositeTaskNode.Child
                            {
                                enabled      = ch.enabled,
                                subTaskValue = ch.subTaskValue,
                                taskNode     = DeepClone(ch.taskNode),
                            });
                return clone;
            }
            return null;
        }

        static ITaskDefinition CopyTaskDefinition(ITaskDefinition source)
        {
            if (source == null) return null;
            var type = source.GetType();

            ITaskDefinition clone;
            try { clone = (ITaskDefinition)Activator.CreateInstance(type); }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to create instance of {type.Name}: {ex.Message}");
                return null;
            }

            foreach (var field in type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var value = field.GetValue(source);
                if (value == null) { field.SetValue(clone, null); continue; }

                if (field.FieldType.IsArray && value is Array arr)
                {
                    field.SetValue(clone, arr.Clone());
                }
                else if (field.FieldType.IsGenericType &&
                         field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var listType = typeof(List<>).MakeGenericType(field.FieldType.GetGenericArguments());
                    var newList = (System.Collections.IList)Activator.CreateInstance(listType);
                    foreach (var item in (System.Collections.IEnumerable)value)
                        newList.Add(item);
                    field.SetValue(clone, newList);
                }
                else
                {
                    field.SetValue(clone, value);
                }
            }
            return clone;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  TREE TRAVERSAL HELPERS
        // ══════════════════════════════════════════════════════════════════

        #region Helpers

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
                    if (ch?.taskNode != null && IsDescendant(ch.taskNode, target))
                        return true;
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

        static string MakeUniqueSiblingName(CompositeTaskNode parent, string originalName)
        {
            if (parent?.children == null) return originalName;
            if (string.IsNullOrEmpty(originalName)) originalName = "New Task";

            var names = new List<string>();
            foreach (var ch in parent.children)
                if (ch?.taskNode != null && !string.IsNullOrEmpty(ch.taskNode.name))
                    names.Add(ch.taskNode.name);

            return ObjectNames.GetUniqueName(names.ToArray(), originalName);
        }

        bool IsExpanded(CompositeTaskNode node)
        {
            if (!_expanded.TryGetValue(node, out bool v)) { _expanded[node] = true; return true; }
            return v;
        }

        void SetExpanded(CompositeTaskNode node, bool value) => _expanded[node] = value;

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  COORDINATE HELPERS
        // ══════════════════════════════════════════════════════════════════

        #region Coordinates

        Vector2 ScreenToScrollLocal(Vector2 mousePos)
        {
            return new Vector2(
                mousePos.x - _hierarchyScrollRect.x,
                mousePos.y - _hierarchyScrollRect.y + _hierarchyScroll.y
            );
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  SEARCH FILTER
        // ══════════════════════════════════════════════════════════════════

        #region Search

        bool IsVisibleBySearch(ATaskNode node)
        {
            if (string.IsNullOrEmpty(_searchFilter)) return true;
            return DoesSubtreeMatch(node, _searchFilter);
        }

        static bool DoesSubtreeMatch(ATaskNode node, string filter)
        {
            if (node == null) return false;
            if (!string.IsNullOrEmpty(node.name) &&
                node.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (node is CompositeTaskNode comp && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch?.taskNode != null && DoesSubtreeMatch(ch.taskNode, filter))
                        return true;
            return false;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  ENABLED CACHE
        // ══════════════════════════════════════════════════════════════════

        #region EnabledCache

        bool IsNodeHierarchyEnabled(ATaskNode node)
        {
            return node != null && _enabledCache.TryGetValue(node, out bool v) ? v : true;
        }

        void RebuildEnabledCache()
        {
            _enabledCache.Clear();
            if (_taskTree?.Root == null) return;
            CacheEnabledRec(_taskTree.Root, null, -1, true);
        }

        void CacheEnabledRec(ATaskNode node, CompositeTaskNode parent, int index, bool parentEnabled)
        {
            bool selfEnabled = parentEnabled;
            if (parent?.children != null && index >= 0 && index < parent.children.Count)
                selfEnabled = parentEnabled && parent.children[index].enabled;

            _enabledCache[node] = selfEnabled;

            if (node is CompositeTaskNode comp && comp.children != null)
                for (int i = 0; i < comp.children.Count; i++)
                    if (comp.children[i]?.taskNode != null)
                        CacheEnabledRec(comp.children[i].taskNode, comp, i, selfEnabled);
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  EXPANDED DICT CLEANUP
        // ══════════════════════════════════════════════════════════════════

        #region ExpandedCleanup

        void PurgeExpandedDict()
        {
            if (_taskTree?.Root == null) { _expanded.Clear(); return; }
            var alive = new HashSet<ATaskNode>();
            CollectAllNodes(_taskTree.Root, alive);
            var toRemove = new List<ATaskNode>();
            foreach (var key in _expanded.Keys)
                if (!alive.Contains(key)) toRemove.Add(key);
            foreach (var key in toRemove)
                _expanded.Remove(key);
        }

        static void CollectAllNodes(ATaskNode node, HashSet<ATaskNode> set)
        {
            if (node == null) return;
            set.Add(node);
            if (node is CompositeTaskNode comp && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch?.taskNode != null)
                        CollectAllNodes(ch.taskNode, set);
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  DIVIDER
        // ══════════════════════════════════════════════════════════════════

        #region Divider

        void DrawDivider(Rect rect)
        {
            EditorGUI.DrawRect(rect, DividerColor);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
        }

        void HandleDividerResize(Rect dividerRect)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && dividerRect.Contains(e.mousePosition))
            { _resizingDivider = true; e.Use(); }
            if (_resizingDivider)
            {
                if (e.type == EventType.MouseDrag)
                {
                    _hierarchyWidth = Mathf.Clamp(e.mousePosition.x, MinHierarchyWidth,
                        position.width - MinInspectorWidth);
                    Repaint();
                    e.Use();
                }
                if (e.type == EventType.MouseUp)
                { _resizingDivider = false; e.Use(); }
            }
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  STYLES
        // ══════════════════════════════════════════════════════════════════

        #region Styles

        void BuildStyles()
        {
            // Rebuild nếu chưa build hoặc sau domain reload (styles bị null)
            if (_stylesBuilt && _labelStyle != null) return;
            _stylesBuilt = true;

            _labelStyle = new GUIStyle(EditorStyles.label)
            {
                normal    = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                alignment = TextAnchor.MiddleLeft,
            };

            _dimLabelStyle = new GUIStyle(EditorStyles.label)
            {
                normal   = { textColor = DisabledTextColor },
                fontSize = 10,
            };

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 12,
                normal    = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                alignment = TextAnchor.MiddleLeft,
                padding   = new RectOffset(4, 4, 2, 2),
            };

            _sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal   = { textColor = new Color(0.75f, 0.75f, 0.75f) },
            };

            _renameStyle = new GUIStyle(EditorStyles.textField)
            {
                padding  = new RectOffset(2, 2, 1, 1),
                fontSize = EditorStyles.label.fontSize,
            };

            _foldoutArrowStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = FoldoutArrowColor }
            };

            _statusDotStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 9,
            };

            _nodeNameStyle = new GUIStyle(_labelStyle);
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  UTILITY
        // ══════════════════════════════════════════════════════════════════

        #region Utility

        void SetDirty()
        {
            if (_taskTree == null) return;
            EditorUtility.SetDirty(_taskTree);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(_taskTree.gameObject.scene);
            Repaint();
        }

        void EnsureTaskDefinitionDatabase()
        {
            if (_taskDefinitionDatabase != null) return;
            string[] guids = AssetDatabase.FindAssets("t:TaskDefinitionDatabase");
            if (guids == null || guids.Length == 0) return;
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            _taskDefinitionDatabase = AssetDatabase.LoadAssetAtPath<TaskDefinitionDatabase>(path);
        }

        void SectionHeader(string title) => GUILayout.Label(title, _sectionStyle);

        void SeparatorLine()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, SeparatorColor);
        }

        #endregion
    }
}
