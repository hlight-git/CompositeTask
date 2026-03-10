// =============================================================================
//  TaskTreeEditorWindow.cs
//  EditorWindow gồm hai panel: Hierarchy (trái) + Inspector (phải).
//  Giống Unity Hierarchy / Inspector cho TaskTree.
//
//  Runtime fields (readonly — đúng theo spec):
//    ATaskNode              : string name, TaskNodeStatus Status, float Progress
//                             Reset(), ForceComplete(), ForceCompleteImmediate()
//    MonoTaskNode           : ITaskDefinition taskDefinition   [SerializeReference]
//    CompositeTaskNode      : ExecutionMode executionMode, List<Child> children
//    CompositeTaskNode.Child: float subTaskValue, ATaskNode taskNode
//    TaskTree (MonoBehaviour): CompositeTaskNode rootNode
//    TaskTree.Execute()     → CancellationTokenSource
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
        // ── Open ──────────────────────────────────────────────────────────
        [MenuItem("Window/Task Tree Editor")]
        public static void Open()
        {
            var win = GetWindow<TaskTreeEditorWindow>("Task Tree");
            win.minSize = new Vector2(600, 400);
            win.Show();
        }

        // Open và bind một TaskTree cụ thể
        public static void OpenWith(TaskTree tree)
        {
            var win = GetWindow<TaskTreeEditorWindow>("Task Tree");
            win.minSize = new Vector2(600, 400);
            win._taskTree = tree;
            win.Show();
        }

        // ── State ─────────────────────────────────────────────────────────
        TaskTree   _taskTree;
        [SerializeField] TaskDefinitionDatabase _taskDefinitionDatabase;
        ATaskNode  _selected;

        // Hierarchy
        float      _hierarchyWidth   = 300f;
        bool       _resizingDivider  = false;
        Vector2    _hierarchyScroll;
        Rect       _hierarchyScrollRect;
        Dictionary<ATaskNode, bool> _expanded = new(); // CompositeTaskNode → expanded

        // Rename inline
        ATaskNode  _renamingNode;
        string     _renameBuffer;
        bool       _focusRenameField;

        // Drag-drop
        ATaskNode  _draggedNode;
        bool       _dragActive;
        Vector2    _dragStartPos;
        const float DragThreshSq = 25f;
        // Drop indicator
        CompositeTaskNode  _dropParentTarget;  // composite ta sẽ insert vào
        int        _dropInsertIndex;   // vị trí insert trong dropParentTarget.children
        bool       _dropValid;

        // Copy/Paste clipboard
        ATaskNode  _clipboard;

        // Inspector scroll
        Vector2    _inspectorScroll;

        // Repaint throttle (Play Mode)
        double       _lastRepaint;
        const double RepaintInterval = 0.05;

        // Styles (built lazily)
        GUIStyle _labelStyle;
        GUIStyle _dimLabelStyle;
        GUIStyle _headerStyle;
        GUIStyle _sectionStyle;
        GUIStyle _renameStyle;
        bool     _stylesBuilt;

        // ── Unity lifecycle ───────────────────────────────────────────────
        void OnEnable()
        {
            EditorApplication.update               += OnUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Undo.undoRedoPerformed                 += Repaint;
            EnsureTaskDefinitionDatabase();
        }

        void OnDisable()
        {
            EditorApplication.update               -= OnUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Undo.undoRedoPerformed                 -= Repaint;
        }

        void OnSelectionChange()
        {
            // Tự động target TaskTree theo GameObject đang được chọn trong scene.
            var go = Selection.activeGameObject;
            if (go == null) return;

            var tree = go.GetComponent<TaskTree>();
            if (tree != null && tree != _taskTree)
            {
                _taskTree = tree;
                _selected = null;
                _expanded.Clear();
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

        // ── OnGUI ─────────────────────────────────────────────────────────
        void OnGUI()
        {
            BuildStyles();
            HandleKeyboard();
            HandleDragInHierarchy();
            HandleTaskTreeSelector();

            if (_taskTree == null)
            {
                DrawNoTarget();
                return;
            }

            // Layout: Hierarchy | Divider | Inspector
            var totalRect = new Rect(0, EditorGUIUtility.singleLineHeight + 6,
                                     position.width,
                                     position.height - EditorGUIUtility.singleLineHeight - 6);

            var hierarchyRect = new Rect(totalRect.x, totalRect.y,
                                          _hierarchyWidth, totalRect.height);
            var dividerRect   = new Rect(_hierarchyWidth, totalRect.y, 4, totalRect.height);
            var inspectorRect = new Rect(_hierarchyWidth + 4, totalRect.y,
                                          totalRect.width - _hierarchyWidth - 4, totalRect.height);

            DrawHierarchy(hierarchyRect);
            DrawDivider(dividerRect);
            DrawInspector(inspectorRect);

            HandleDividerResize(dividerRect);

            // Consume rename commit on Enter / Escape outside text field
            if (_renamingNode != null)
            {
                var e = Event.current;
                if (e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    { CommitRename(); e.Use(); }
                    else if (e.keyCode == KeyCode.Escape)
                    { CancelRename(); e.Use(); }
                }
            }
        }

        // ── TaskTree selector toolbar ─────────────────────────────────────
        void HandleTaskTreeSelector()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();
            var newTree = (TaskTree)EditorGUILayout.ObjectField(
                _taskTree, typeof(TaskTree), allowSceneObjects: true,
                GUILayout.Width(240));
            if (EditorGUI.EndChangeCheck())
            {
                _taskTree = newTree;
                _selected  = null;
                _expanded.Clear();
            }

            EditorGUI.BeginChangeCheck();
            var newDatabase = (TaskDefinitionDatabase)EditorGUILayout.ObjectField(
                _taskDefinitionDatabase, typeof(TaskDefinitionDatabase), allowSceneObjects: false,
                GUILayout.Width(240));
            if (EditorGUI.EndChangeCheck())
            {
                _taskDefinitionDatabase = newDatabase;
            }

            GUILayout.FlexibleSpace();

            if (_taskTree != null && Application.isPlaying)
            {
                if (GUILayout.Button("▶  Execute", EditorStyles.toolbarButton, GUILayout.Width(90)))
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

        // ══════════════════════════════════════════════════════════════════
        //  HIERARCHY
        // ══════════════════════════════════════════════════════════════════
        void DrawHierarchy(Rect rect)
        {
            // Background
            EditorGUI.DrawRect(rect, new Color(0.19f, 0.19f, 0.19f));

            // Header
            var headerRect = new Rect(rect.x, rect.y, rect.width, 22);
            EditorGUI.DrawRect(headerRect, new Color(0.15f, 0.15f, 0.15f));
            GUI.Label(headerRect, "  Hierarchy", _headerStyle);

            // Scroll area
            var scrollRect = new Rect(rect.x, rect.y + 22, rect.width, rect.height - 22);
            _hierarchyScrollRect = scrollRect;
            _hierarchyScroll = GUI.BeginScrollView(scrollRect, _hierarchyScroll,
                new Rect(0, 0, scrollRect.width - 16, GetTreeContentHeight(_taskTree?.Root, 0)));

            float y = 0;
            if (_taskTree.Root != null)
                DrawNodeRow(_taskTree.Root, null, -1, 0, ref y, scrollRect.width);
            else
            {
                GUI.Label(new Rect(8, y, scrollRect.width, 20),
                    "(empty — right-click to create root)", _dimLabelStyle);
            }

            GUI.EndScrollView();

            // Right-click context menu on scroll area
            var e = Event.current;
            if (e.type == EventType.ContextClick && scrollRect.Contains(e.mousePosition))
            {
                // Đưa tọa độ màn hình về local trong content của ScrollView
                var localPos = e.mousePosition;
                localPos.x -= scrollRect.x;
                localPos.y -= scrollRect.y;
                localPos.y += _hierarchyScroll.y;
                ShowContextMenu(HitTestNode(_taskTree?.Root, localPos, 0));
                e.Use();
            }

            // Click on empty space → deselect
            if (e.type == EventType.MouseDown && e.button == 0 && scrollRect.Contains(e.mousePosition))
            {
                var localPos = e.mousePosition;
                localPos.x -= scrollRect.x;
                localPos.y -= scrollRect.y;
                localPos.y += _hierarchyScroll.y;
                var hit = HitTestNode(_taskTree?.Root, localPos, 0);
                if (hit == null)
                {
                    CommitRename();
                    _selected = null;
                    GUI.FocusControl(null);
                    Repaint();
                }
            }

            // Drop indicator line
            if (_dragActive && _dropValid)
                DrawDropIndicator(rect, scrollRect);
        }

        // ── Recursive row draw ────────────────────────────────────────────
        const float RowHeight  = 20f;
        const float IndentStep = 14f;
        const float FoldoutW   = 14f;

        float GetTreeContentHeight(ATaskNode node, int depth)
        {
            if (node == null) return 20; // placeholder
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
            var e         = Event.current;
            var rowRect   = new Rect(0, y, width, RowHeight);
            float indent  = 4 + depth * IndentStep;

            // ── Enabled state (giống GameObject: node bị disable sẽ xám) ──
            bool isSelected   = node == _selected;
            bool isHierarchyEnabled = IsNodeHierarchyEnabled(node);

            // ── Selection / hover background ──
            if (isSelected)
                EditorGUI.DrawRect(rowRect, new Color(0.24f, 0.49f, 0.91f, 0.85f));
            else if (rowRect.Contains(e.mousePosition) && e.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, new Color(0.3f, 0.3f, 0.3f, 0.4f));

            // ── Drop highlight ──
            if (_dragActive && _dropValid && _dropParentTarget == parent &&
                _dropInsertIndex == indexInParent)
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y - 1, rowRect.width, 2),
                    new Color(0.3f, 0.75f, 1f));

            float cx = indent;

            // ── Foldout triangle ──
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
                {
                    GUI.Label(foldRect, expanded ? "▼" : "▶",
                        new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.8f, 0.8f, 0.8f, 0.8f) } });
                }
                cx += FoldoutW;
            }
            else cx += FoldoutW; // mono nodes indented same amount

            // ── Status dot (Play Mode) ──
            if (Application.isPlaying && node != null)
            {
                Color dotColor = node.Status switch
                {
                    TaskNodeStatus.Running   => new Color(0.2f, 0.8f, 1f),
                    TaskNodeStatus.Completed => new Color(0.2f, 0.85f, 0.3f),
                    _                        => new Color(0.45f, 0.45f, 0.45f),
                };
                if (e.type == EventType.Repaint)
                {
                    var oldColor = GUI.color;
                    GUI.color = dotColor;
                    GUI.Label(new Rect(cx, y + 3, 12, 14), "●",
                        new GUIStyle(EditorStyles.label) { fontSize = 9, normal = { textColor = dotColor } });
                    GUI.color = oldColor;
                }
                cx += 14;
            }

            // ── Type badge ──
            string badge = node switch
            {
                CompositeTaskNode c => c.executionMode == ExecutionMode.Sequential ? "[Seq]" : "[Par]",
                MonoTaskNode      m => "[Mono]",
                _                   => "[?]",
            };
            bool isRoot = _taskTree != null && node == _taskTree.Root;
            if (isRoot) badge = "[Root]";
            float badgeW = 38;
            if (e.type == EventType.Repaint)
                GUI.Label(new Rect(cx, y, badgeW, RowHeight), badge, _dimLabelStyle);
            cx += badgeW + 2;

            // ── Name (label hoặc rename field) ──
            float nameX = cx;
            float nameW = width - cx - 4;

            if (_renamingNode == node)
            {
                // Inline rename text field
                var renameRect = new Rect(nameX, y + 1, nameW, RowHeight - 2);
                GUI.SetNextControlName("RenameField");
                _renameBuffer = GUI.TextField(renameRect, _renameBuffer, _renameStyle);

                // Đảm bảo chỉ gọi FocusTextInControl trong Repaint của frame đầu tiên
                if (_focusRenameField && e.type == EventType.Repaint)
                {
                    EditorGUI.FocusTextInControl("RenameField");
                    _focusRenameField = false;
                }

                // Commit on click outside
                if (e.type == EventType.MouseDown && !renameRect.Contains(e.mousePosition))
                    CommitRename();
            }
            else
            {
                string displayName = string.IsNullOrEmpty(node.name) ? string.Empty : node.name;

                var labelRect = new Rect(nameX, y, nameW, RowHeight);
                if (e.type == EventType.Repaint)
                {
                    var style = new GUIStyle(_labelStyle);

                    // Ưu tiên trạng thái disabled → xám
                    if (!isHierarchyEnabled || isRoot)
                        style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                    // Node không hợp lệ (MonoTaskNode thiếu taskDefinition) → đỏ
                    else if (node is MonoTaskNode mono && mono.taskDefinition == null)
                        style.normal.textColor = new Color(1f, 0.25f, 0.25f);
                    else if (isSelected)
                        style.normal.textColor = Color.white;

                    GUI.Label(labelRect, displayName, style);
                }
            }

            // ── Mouse events on this row ──
            if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
            {
                if (e.button == 0)
                {
                    if (_renamingNode != node) CommitRename();

                    // Double-click → start rename
                    if (e.clickCount == 2 && node == _selected)
                    { StartRename(node); e.Use(); }
                    else
                    {
                        _selected = node;
                        GUI.FocusControl(null);
                        // Start drag tracking
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
                    // Context menu is handled at scroll-view level
                }
            }

            // Drag start detection — do NOT call e.Use() here so HandleDragInHierarchy()
            // (called before drawing) can also process MouseDrag to update the drop target.
            if (e.type == EventType.MouseDrag && _draggedNode == node && !_dragActive)
            {
                float distSq = ((Vector2)e.mousePosition - _dragStartPos).sqrMagnitude;
                if (distSq > DragThreshSq)
                {
                    _dragActive = true;
                    _dropValid  = false;
                    // Do NOT e.Use() — HandleDragInHierarchy() will handle the drag
                }
            }

            y += RowHeight;

            // ── Children (recursive) ──
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

        // ── Hit test ─────────────────────────────────────────────────────
        ATaskNode HitTestNode(ATaskNode node, Vector2 localPos, float startY)
        {
            return HitTestRec(node, localPos, ref startY);
        }

        ATaskNode HitTestRec(ATaskNode node, Vector2 pos, ref float y)
        {
            if (node == null) return null;
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

        // ── Drop indicator line ───────────────────────────────────────────
        void DrawDropIndicator(Rect hierarchyRect, Rect scrollRect)
        {
            // Compute Y of drop position
            float y    = 0;
            float lineY = ComputeDropLineY(_taskTree.Root, null, -1, ref y);
            if (lineY < 0) return;
            float absY = scrollRect.y + lineY - _hierarchyScroll.y;
            if (absY < scrollRect.y || absY > scrollRect.yMax) return;
            EditorGUI.DrawRect(new Rect(scrollRect.x + 4, absY - 1, scrollRect.width - 8, 2),
                new Color(0.35f, 0.8f, 1f));
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
                // After last child
                if (_dropParentTarget == comp && _dropInsertIndex == (comp.children?.Count ?? 0))
                    return y;
            }
            return -1;
        }

        // ── Drag handling (MouseMove / MouseDrag / MouseUp on whole window) ──
        void HandleDragInHierarchy()
        {
            if (!_dragActive || _draggedNode == null) return;
            var e = Event.current;

            if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove)
            {
                UpdateDropTarget(e.mousePosition);
                Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                if (e.button == 0)
                {
                    if (_dropValid) PerformDrop();
                }
                _dragActive  = false;
                _draggedNode = null;
                _dropValid   = false;
                Repaint();
                e.Use();
            }
        }

        void UpdateDropTarget(Vector2 mousePos)
        {
            if (_taskTree == null || _taskTree.Root == null) return;

            // Chuyển mousePos (screen space) về tọa độ local trong content ScrollView
            var localY = mousePos.y - _hierarchyScrollRect.y + _hierarchyScroll.y;

            float y = 0f;
            _dropValid = false;
            FindDropTarget(_taskTree.Root, null, -1, ref y, localY);
        }

        void FindDropTarget(ATaskNode node, CompositeTaskNode parent, int indexInParent,
                             ref float y, float mouseY)
        {
            if (node == null) return;
            float rowTop = y;
            float rowBot = y + RowHeight;
            y += RowHeight;

            // Upper half of row → insert before this node
            if (mouseY >= rowTop && mouseY < rowTop + RowHeight * 0.5f)
            {
                // Can only drop as sibling (need parent)
                if (parent != null && _draggedNode != node && !IsAncestorOrSelf(_draggedNode, parent))
                {
                    _dropParentTarget = parent;
                    _dropInsertIndex  = indexInParent;
                    _dropValid        = true;
                }
            }
            // Lower half → insert after OR into composite
            else if (mouseY >= rowTop + RowHeight * 0.5f && mouseY < rowBot)
            {
                if (node is CompositeTaskNode comp && IsExpanded(comp) && _draggedNode != node &&
                    !IsAncestorOrSelf(_draggedNode, comp))
                {
                    // Drop into composite as first child
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

            // Find current parent of dragged node
            FindParent(_taskTree.Root, _draggedNode, out var oldParent, out int oldIdx);

            // Avoid no-op
            if (oldParent == _dropParentTarget && oldIdx == _dropInsertIndex) return;

            Undo.RegisterCompleteObjectUndo(_taskTree, "Move Node");

            float sv = 1f;
            if (oldParent != null)
            {
                sv = oldParent.children[oldIdx].subTaskValue;
                oldParent.children.RemoveAt(oldIdx);
                // Adjust insertIndex if same parent and removing shifts target
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

            SetDirty();
            Repaint();
        }

        // ── Expand/collapse helpers ───────────────────────────────────────
        bool IsExpanded(CompositeTaskNode node)
        {
            if (!_expanded.TryGetValue(node, out bool v)) { _expanded[node] = true; return true; }
            return v;
        }
        void SetExpanded(CompositeTaskNode node, bool value) => _expanded[node] = value;

        // ── Rename ────────────────────────────────────────────────────────
        void StartRename(ATaskNode node)
        {
            CommitRename();
            _renamingNode      = node;
            _renameBuffer      = node.name ?? "";
            _focusRenameField  = true;
            Repaint();
        }

        void CommitRename()
        {
            if (_renamingNode == null) return;
            Undo.RegisterCompleteObjectUndo(_taskTree, "Rename Node");
            _renamingNode.name = _renameBuffer;
            SetDirty();
            _renamingNode = null;
            Repaint();
        }

        void CancelRename() { _renamingNode = null; Repaint(); }

        // ── Context menu ──────────────────────────────────────────────────
        void ShowContextMenu(ATaskNode hitNode)
        {
            var menu = new GenericMenu();

            if (_taskTree.Root == null)
            {
                menu.AddItem(new GUIContent("Create Root Composite (Sequential)"), false,
                    () => { CreateRoot(ExecutionMode.Sequential); });
                menu.AddItem(new GUIContent("Create Root Composite (Parallel)"), false,
                    () => { CreateRoot(ExecutionMode.Parallel); });
                menu.ShowAsContext(); return;
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
            }

            if (target != null)
            {
                // Root: không cho Delete
                bool isRoot = _taskTree != null && target == _taskTree.Root;

                menu.AddItem(new GUIContent("Rename"), false, () => StartRename(target));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Duplicate"), false, () => DuplicateNode(target));
                menu.AddItem(new GUIContent("Copy"), false, () => _clipboard = target);
                if (_clipboard != null && target is CompositeTaskNode compPaste)
                    menu.AddItem(new GUIContent("Paste as Child"), false, () => PasteChild(compPaste));
                else
                    menu.AddDisabledItem(new GUIContent("Paste as Child"));
                menu.AddSeparator("");

                if (isRoot)
                    menu.AddDisabledItem(new GUIContent("Delete Root (disabled)"));
                else
                    menu.AddItem(new GUIContent("Delete"), false, () => DeleteNode(target));
            }

            menu.ShowAsContext();
        }

        // ── Mutations ─────────────────────────────────────────────────────
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
            _selected = node;
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
            _selected = node;
            SetDirty();
        }

        void DeleteNode(ATaskNode node)
        {
            if (node == null) return;

            // Find parent
            if (node == _taskTree.Root)
            {
                // Root không thể xóa – giữ nguyên, chỉ repaint để user thấy không có gì xảy ra
                Repaint();
                return;
            }

            FindParent(_taskTree.Root, node, out var parent, out int idx);
            if (parent == null) return;
            Undo.RegisterCompleteObjectUndo(_taskTree, "Delete Node");
            parent.children.RemoveAt(idx);
            if (_selected == node) _selected = parent;
            SetDirty();
        }

        void DuplicateNode(ATaskNode node)
        {
            if (node == null) return;
            if (node == _taskTree.Root) return; // can't duplicate root

            FindParent(_taskTree.Root, node, out var parent, out int idx);
            if (parent == null) return;
            var clone = DeepClone(node);
            Undo.RegisterCompleteObjectUndo(_taskTree, "Duplicate Node");
            parent.children.Insert(idx + 1, new CompositeTaskNode.Child
            {
                enabled     = parent.children[idx].enabled,
                subTaskValue = parent.children[idx].subTaskValue,
                taskNode     = clone,
            });
            // Đặt tên theo format Unity Hierarchy: "Name (1)", "Name (2)", ...
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
            
            // Cố gắng bảo tồn trạng thái enabled từ parent cũ nếu có
            bool enabled = true;
            if (_taskTree != null && _taskTree.Root != null)
            {
                FindParent(_taskTree.Root, _clipboard, out var oldParent, out int oldIdx);
                if (oldParent != null && oldParent.children != null &&
                    oldIdx >= 0 && oldIdx < oldParent.children.Count)
                {
                    enabled = oldParent.children[oldIdx].enabled;
                }
            }

            parent.children.Add(new CompositeTaskNode.Child
            {
                enabled     = enabled,
                subTaskValue = 1f,
                taskNode     = clone
            });
            SetExpanded(parent, true);
            clone.name = MakeUniqueSiblingName(parent, _clipboard.name);
            _selected = clone;
            SetDirty();
        }

        // ── Helpers ───────────────────────────────────────────────────────
        static bool IsAncestorOrSelf(ATaskNode candidate, ATaskNode target)
        {
            if (candidate == target) return true;
            if (candidate is CompositeTaskNode c && c.children != null)
                foreach (var ch in c.children)
                    if (ch?.taskNode != null && IsAncestorOrSelf(ch.taskNode, target)) return true;
            return false;
        }

        // Một node được xem là "enabled" trong Hierarchy nếu toàn bộ chuỗi cha của nó
        // đều có Child.enabled = true (giống cách GameObject bị disable theo cha).
        bool IsNodeHierarchyEnabled(ATaskNode node)
        {
            if (node == null || _taskTree == null || _taskTree.Root == null) return true;
            if (node == _taskTree.Root) return true;

            FindParent(_taskTree.Root, node, out var parent, out int index);
            if (parent == null) return true;

            var child = parent.children != null && index >= 0 && index < parent.children.Count
                ? parent.children[index]
                : null;

            if (child == null || !child.enabled) return false;

            return IsNodeHierarchyEnabled(parent);
        }

        static void FindParent(ATaskNode root, ATaskNode target,
                                out CompositeTaskNode parent, out int index)
        {
            parent = null; index = -1;
            FindParentRec(root, target, ref parent, ref index);
        }

        // Tạo tên unique cho sibling dùng API Unity (ObjectNames.GetUniqueName)
        static string MakeUniqueSiblingName(CompositeTaskNode parent, string originalName)
        {
            if (parent == null || parent.children == null)
                return originalName;

            if (string.IsNullOrEmpty(originalName))
                originalName = "New Task";

            // Thu thập tên sibling hiện có
            var names = new List<string>();
            foreach (var ch in parent.children)
            {
                if (ch?.taskNode != null && !string.IsNullOrEmpty(ch.taskNode.name))
                    names.Add(ch.taskNode.name);
            }

            return ObjectNames.GetUniqueName(names.ToArray(), originalName);
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

        static ATaskNode DeepClone(ATaskNode src)
        {
            if (src is MonoTaskNode m)
                return new MonoTaskNode { name = m.name, taskDefinition = m.taskDefinition };
            if (src is CompositeTaskNode c)
            {
                var clone = new CompositeTaskNode
                {
                    name          = c.name,
                    executionMode = c.executionMode,
                    children      = new List<CompositeTaskNode.Child>(),
                };
                if (c.children != null)
                    foreach (var ch in c.children)
                        if (ch?.taskNode != null)
                            clone.children.Add(new CompositeTaskNode.Child
                            {
                                subTaskValue = ch.subTaskValue,
                                taskNode     = DeepClone(ch.taskNode),
                            });
                return clone;
            }
            return null;
        }

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

        // ── Divider ───────────────────────────────────────────────────────
        void DrawDivider(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f));
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
                { _hierarchyWidth = Mathf.Clamp(e.mousePosition.x, 150, position.width - 200); Repaint(); e.Use(); }
                if (e.type == EventType.MouseUp)
                { _resizingDivider = false; e.Use(); }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  INSPECTOR
        // ══════════════════════════════════════════════════════════════════
        void DrawInspector(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.22f, 0.22f, 0.22f));

            // Header
            var headerRect = new Rect(rect.x, rect.y, rect.width, 22);
            EditorGUI.DrawRect(headerRect, new Color(0.15f, 0.15f, 0.15f));
            GUI.Label(headerRect, "  Inspector", _headerStyle);

            if (_selected == null)
            {
                GUI.Label(new Rect(rect.x + 10, rect.y + 30, rect.width - 20, 20),
                    "Nothing selected.", _dimLabelStyle);
                return;
            }

            var bodyRect = new Rect(rect.x, rect.y + 22, rect.width, rect.height - 22);
            GUILayout.BeginArea(bodyRect);
            _inspectorScroll = GUILayout.BeginScrollView(_inspectorScroll);

            DrawInspectorContent(_selected);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawInspectorContent(ATaskNode node)
        {
            // Không cần header "Node" nữa – chỉ hiển thị nội dung cụ thể theo loại node
            if (node is MonoTaskNode mono)
                DrawMonoInspector(mono);
            else if (node is CompositeTaskNode comp)
                DrawCompositeInspector(comp);

            // ── Runtime ──
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

        // ── MonoTaskNode inspector ────────────────────────────────────────
        void DrawMonoInspector(MonoTaskNode mono)
        {
            // taskDefinition type picker — dùng trực tiếp _taskDefinitionDatabase
            var types = GetTaskDefinitionTypes(out var names);

            int curIdx = IndexOfType(types, mono.taskDefinition?.GetType());

            EditorGUILayout.LabelField("Task Definition:", EditorStyles.boldLabel);

            if (types.Length == 0)
            {
                EditorGUILayout.HelpBox("No ITaskDefinition implementations found in current TaskDefinitionDatabase.", MessageType.Info);
                return;
            }

            // Thêm option "None" cho phép taskDefinition = null
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
                        mono.taskDefinition = (ITaskDefinition)Activator.CreateInstance(types[typeIndex]);
                    }
                }
                SetDirty();
            }

            if (mono.taskDefinition != null)
            {
                GUILayout.Space(6);
                EditorGUI.indentLevel++;
                DrawObjectFields(mono.taskDefinition);
                EditorGUI.indentLevel--;
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
                if (entry == null || entry.script == null) continue;
                var type = entry.script.GetClass();
                if (type == null) continue;
                if (type.IsAbstract || type.IsInterface) continue;
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
            {
                if (types[i] == t) return i;
            }
            return -1;
        }

        // ── CompositeTaskNode inspector ───────────────────────────────────
        void DrawCompositeInspector(CompositeTaskNode comp)
        {
            // executionMode
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

            // Children list: toggle (enabled) + name field + weight + delete
            if (comp.children != null)
            {
                for (int i = 0; i < comp.children.Count; i++)
                {
                    var child = comp.children[i];
                    if (child?.taskNode == null) continue;

                    int ci = i; // capture for lambda
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

                    // Name field giống GameObject: chỉnh trực tiếp name của child node
                    EditorGUI.BeginChangeCheck();
                    string childName = EditorGUILayout.TextField(child.taskNode.name ?? string.Empty);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RegisterCompleteObjectUndo(_taskTree, "Rename Child Node");
                        child.taskNode.name = childName;
                        SetDirty();
                    }

                    // subTaskValue inline
                    EditorGUI.BeginChangeCheck();
                    float newSv = EditorGUILayout.FloatField(child.subTaskValue, GUILayout.Width(48));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RegisterCompleteObjectUndo(_taskTree, "Edit subTaskValue");
                        comp.children[ci].subTaskValue = Mathf.Max(0f, newSv);
                        SetDirty();
                    }

                    // Delete child button
                    var oldColor = GUI.color;
                    GUI.color = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        Undo.RegisterCompleteObjectUndo(_taskTree, "Remove Child");
                        if (_selected == comp.children[ci].taskNode) _selected = comp;
                        comp.children.RemoveAt(ci);
                        SetDirty();
                        GUILayout.EndHorizontal();
                        GUI.color = oldColor;
                        break; // list changed, stop iteration
                    }
                    GUI.color = oldColor;
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Mono Task"))   AddMonoChild(comp);
            if (GUILayout.Button("+ Composite Task"))   AddCompositeChild(comp, ExecutionMode.Sequential);
            GUILayout.EndHorizontal();
        }

        // ── Runtime inspector ─────────────────────────────────────────────
        void DrawRuntimeInspector(ATaskNode node)
        {
            // Status
            Color statusColor = node.Status switch
            {
                TaskNodeStatus.Running   => new Color(0.2f, 0.8f, 1f),
                TaskNodeStatus.Completed => new Color(0.2f, 0.9f, 0.3f),
                _                        => new Color(0.6f, 0.6f, 0.6f),
            };
            var oldColor = GUI.contentColor;
            GUI.contentColor = statusColor;
            EditorGUILayout.LabelField("Status", node.Status.ToString(), EditorStyles.boldLabel);
            GUI.contentColor = oldColor;

            // Progress bar
            EditorGUILayout.LabelField("Progress", $"{node.Progress * 100f:F1} %");
            var progressRect = EditorGUILayout.GetControlRect(false, 16);
            progressRect = EditorGUI.IndentedRect(progressRect);
            EditorGUI.DrawRect(progressRect, new Color(0.15f, 0.15f, 0.15f));
            if (node.Progress > 0)
                EditorGUI.DrawRect(
                    new Rect(progressRect.x, progressRect.y,
                             progressRect.width * node.Progress, progressRect.height),
                    new Color(0.2f, 0.7f, 1f));

            GUILayout.Space(8);

            // Lifecycle buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Force Complete"))          node.ForceComplete();
            if (GUILayout.Button("Force Complete Immediate")) node.ForceCompleteImmediate();
            if (GUILayout.Button("Reset"))                   node.Reset();
            GUILayout.EndHorizontal();
        }

        // ── Reflection field drawing ──────────────────────────────────────
        void DrawObjectFields(object obj)
        {
            if (obj == null) return;
            bool changed = false;
            foreach (var field in obj.GetType().GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null) continue;
                if (field.GetCustomAttribute<NonSerializedAttribute>() != null) continue;

                string niceName = ObjectNames.NicifyVariableName(field.Name);
                object val      = field.GetValue(obj);

                EditorGUI.BeginChangeCheck();
                object newVal   = DrawField(niceName, field.FieldType, val);
                if (EditorGUI.EndChangeCheck())
                {
                    if (!changed)
                    {
                        Undo.RegisterCompleteObjectUndo(_taskTree, "Edit Field");
                        changed = true;
                    }
                    field.SetValue(obj, newVal);
                    SetDirty();
                }
            }
        }

        static object DrawField(string label, Type t, object val)
        {
            if (t == typeof(int))    return EditorGUILayout.IntField(label,   val is int   i  ? i  : 0);
            if (t == typeof(float))  return EditorGUILayout.FloatField(label, val is float f  ? f  : 0f);
            if (t == typeof(string)) return EditorGUILayout.TextField(label,  val as string ?? "");
            if (t == typeof(bool))   return EditorGUILayout.Toggle(label,     val is bool  b  && b);
            if (t == typeof(Vector2)) return EditorGUILayout.Vector2Field(label, val is Vector2 v2 ? v2 : default);
            if (t == typeof(Vector3)) return EditorGUILayout.Vector3Field(label, val is Vector3 v3 ? v3 : default);
            if (t.IsEnum)            return EditorGUILayout.EnumPopup(label,  val is Enum e ? e : (Enum)Enum.GetValues(t).GetValue(0));
            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
                return EditorGUILayout.ObjectField(label, val as UnityEngine.Object, t, allowSceneObjects: true);

            EditorGUILayout.LabelField(label, $"({t.Name}) — unsupported", EditorStyles.miniLabel);
            return val;
        }

        // ── Inspector helpers ─────────────────────────────────────────────
        void SectionHeader(string title)
        {
            GUILayout.Label(title, _sectionStyle);
        }

        void SeparatorLine()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));
        }

        // ══════════════════════════════════════════════════════════════════
        //  STYLES  (built once, lazily)
        // ══════════════════════════════════════════════════════════════════
        void BuildStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            _labelStyle = new GUIStyle(EditorStyles.label)
            {
                normal   = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                alignment = TextAnchor.MiddleLeft,
            };

            _dimLabelStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) },
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
                fontSize  = 11,
                normal    = { textColor = new Color(0.75f, 0.75f, 0.75f) },
            };

            _renameStyle = new GUIStyle(EditorStyles.textField)
            {
                padding  = new RectOffset(2, 2, 1, 1),
                fontSize  = EditorStyles.label.fontSize,
            };
        }

        // ══════════════════════════════════════════════════════════════════
        //  KEYBOARD SHORTCUTS (handled in OnGUI before drawing)
        // ══════════════════════════════════════════════════════════════════
        void HandleKeyboard()
        {
            if (_taskTree == null || _taskTree.Root == null) return;
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;

            // Nếu đang gõ trong một text field bất kỳ (thường là ở Inspector),
            // thì không xử lý phím tắt của Hierarchy, trừ khi đó là ô rename của Hierarchy.
            if (EditorGUIUtility.editingTextField)
            {
                var focused = GUI.GetNameOfFocusedControl();
                if (focused != "RenameField")
                    return;
            }

            bool ctrl = e.control || e.command;

            // Nếu chưa có selection, mặc định chọn Root
            if (_selected == null)
            {
                _selected = _taskTree.Root;
                Repaint();
            }

            // Đang rename: Enter / Escape đã được xử lý trong OnGUI.
            // Các phím điều hướng khác trước tiên sẽ commit rename.
            if (_renamingNode != null &&
                (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow ||
                 e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow))
            {
                CommitRename();
            }

            // Danh sách node đang hiển thị theo thứ tự vẽ trong Hierarchy
            var visible = new List<ATaskNode>();
            BuildVisibleList(_taskTree.Root, visible);
            int curIndex = visible.IndexOf(_selected);

            // Delete
            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                DeleteNode(_selected);
                e.Use();
                return;
            }

            // Duplicate
            if (ctrl && e.keyCode == KeyCode.D)
            {
                DuplicateNode(_selected);
                e.Use();
                return;
            }

            // Copy / Paste
            if (ctrl && e.keyCode == KeyCode.C)
            { _clipboard = _selected; e.Use(); return; }

            if (ctrl && e.keyCode == KeyCode.V && _selected is CompositeTaskNode cv)
            { PasteChild(cv); e.Use(); return; }

            // Rename shortcut — mô phỏng Hierarchy: Windows dùng F2, macOS dùng Enter.
            if (_renamingNode == null && IsRenameKey(e))
            {
                StartRename(_selected);
                e.Use();
                return;
            }

            // Điều hướng lên/xuống giống Hierarchy
            if (curIndex >= 0)
            {
                if (e.keyCode == KeyCode.UpArrow)
                {
                    if (curIndex > 0)
                    {
                        _selected = visible[curIndex - 1];
                        Repaint();
                    }
                    e.Use();
                    return;
                }

                if (e.keyCode == KeyCode.DownArrow)
                {
                    if (curIndex < visible.Count - 1)
                    {
                        _selected = visible[curIndex + 1];
                        Repaint();
                    }
                    e.Use();
                    return;
                }
            }

            // Left / Right: thu gọn / mở rộng giống Hierarchy
            if (e.keyCode == KeyCode.LeftArrow)
            {
                if (_selected is CompositeTaskNode comp)
                {
                    if (IsExpanded(comp))
                    {
                        // Nếu đang mở → gập lại
                        SetExpanded(comp, false);
                        Repaint();
                    }
                    else
                    {
                        // Đang gập → move selection lên parent
                        FindParent(_taskTree.Root, _selected, out var parent, out _);
                        if (parent != null)
                        {
                            _selected = parent;
                            Repaint();
                        }
                    }
                }
                else
                {
                    // Node lá → chuyển selection lên parent
                    FindParent(_taskTree.Root, _selected, out var parent, out _);
                    if (parent != null)
                    {
                        _selected = parent;
                        Repaint();
                    }
                }
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.RightArrow)
            {
                if (_selected is CompositeTaskNode comp)
                {
                    if (!IsExpanded(comp))
                    {
                        // Đang gập → mở ra
                        SetExpanded(comp, true);
                        Repaint();
                    }
                    else if (comp.children != null && comp.children.Count > 0)
                    {
                        // Đã mở → nhảy xuống child đầu tiên
                        var firstChild = comp.children[0].taskNode;
                        if (firstChild != null)
                        {
                            _selected = firstChild;
                            Repaint();
                        }
                    }
                }
                e.Use();
            }
        }

        void BuildVisibleList(ATaskNode node, List<ATaskNode> list)
        {
            if (node == null) return;
            list.Add(node);

            if (node is CompositeTaskNode comp && IsExpanded(comp) && comp.children != null)
            {
                foreach (var ch in comp.children)
                {
                    if (ch?.taskNode != null)
                        BuildVisibleList(ch.taskNode, list);
                }
            }
        }

        bool IsRenameKey(Event e)
        {
            // Cho phép cả F2 và Enter/Return (không modifier) để bắt đầu rename,
            // nhằm mô phỏng Hierarchy trên cả Windows và macOS một cách ổn định.
            if (e.keyCode == KeyCode.F2) return true;
            if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) &&
                !e.alt && !e.control && !e.command)
                return true;
            return false;
        }
    }

}