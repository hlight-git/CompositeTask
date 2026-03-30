// =============================================================================
//  TaskTreePropertyDrawer.cs  (V2)
//  PropertyDrawer cho TaskTree (pure serializable class).
//  V2: ATask replaces MonoTaskNode; Runtime.CompositeTask replaces CompositeTaskNode.
//  Draws inline in Unity Inspector: Import/Export -> Hierarchy -> Inspector.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hlight.Structures.CompositeTask.Editor; // SearchablePopup
using Hlight.Structures.CompositeTask.Runtime;
using Runtime = Hlight.Structures.CompositeTask.Runtime; // ExecutionMode, Runtime.CompositeTask prefix
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
            // Foldout keys
            public string foldoutKeyPrefix;

            // Import/Export
            public TextAsset importJson;
            public DefaultAsset exportFolder;

            // Hierarchy / Selection (NavEntry-based)
            public List<NavEntry> navList = new();         // rebuilt each frame
            public int primaryNavIndex = -1;               // current primary selection
            public int anchorNavIndex = -1;                // shift-select anchor
            public HashSet<int> selectedNavIndices = new(); // multi-selection indices
            public Dictionary<ATask, bool> expanded = new();
            public string searchFilter = "";
            public Vector2 hierarchyScroll;
            public Rect hierarchyScrollRect;

            // Convenience accessors
            public ATask SelectedTask => primaryNavIndex >= 0 && primaryNavIndex < navList.Count
                ? navList[primaryNavIndex].task : null;
            public NavEntry? PrimaryEntry => primaryNavIndex >= 0 && primaryNavIndex < navList.Count
                ? navList[primaryNavIndex] : null;
            public bool IsNavSelected(int idx) => selectedNavIndices.Contains(idx);

            // Deferred selection (resolved after navList rebuild)
            public ATask pendingSelectTask;
            public Runtime.CompositeTask pendingSelectEmptyParent;
            public int pendingSelectEmptyIndex = -1;

            // Rename
            public ATask renamingNode;
            public string renameBuffer;
            public bool focusRenameField;

            // Drag
            public ATask draggedNode;
            public bool dragActive;
            public bool dragNeedCaptureStart;
            public Vector2 dragStartPos;
            public Runtime.CompositeTask dropParentTarget;
            public int dropInsertIndex;
            public bool dropValid;
            public bool dropAsChild;
            public float contentLocalMouseY;
            public bool dropNeedsUpdate;

            // Clipboard (multi)
            public List<ATask> clipboard = new();

            // Focus
            public bool hierarchyFocused;

            // Scroll-to-selection
            public bool needsScrollToSelection;

            // Resize
            public float hierarchyHeight = 200f;
            public bool resizing;
            public float resizeDragStartY;
            public float resizeDragStartHeight;

            // Enabled cache
            public Dictionary<ATask, bool> enabledCache = new();

            // Registry entries (cached from [DefineTask] attribute scan)
            public List<TaskRegistry.Entry> RegistryEntries => TaskRegistry.Entries;

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
                state.foldoutKeyPrefix = "TaskTreePD_V2_" + key + "_";
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
                h += s.hierarchyHeight + SearchHeight + 6; // +6 for resize handle

            // Execute button (play mode)
            if (Application.isPlaying)
                h += EditorGUIUtility.singleLineHeight + 4;

            // Inspector foldout (when anything selected — task or empty child)
            bool hasAnySelection = s.SelectedTask != null || (s.PrimaryEntry?.IsEmpty == true);
            if (hasAnySelection)
            {
                h += EditorGUIUtility.singleLineHeight + 2; // foldout header
                if (GetFoldout(s, FoldoutInspector))
                    h += CalculateInspectorHeight(s, property);
            }

            return h;
        }

        static float CalculateInspectorHeight(DrawerState s, SerializedProperty property)
        {
            float h = 0;
            float lineH = EditorGUIUtility.singleLineHeight + 2;
            var taskTree = GetTaskTreeFromProperty(property);
            bool isNonRoot = true;
            ATask task = null;

            if (s.SelectedTask != null)
            {
                task = s.SelectedTask;
                isNonRoot = task != taskTree?.root;
            }

            // Row 1: Enabled + Name (or just Name for root)
            h += lineH;

            // SubTaskValue (non-root)
            if (isNonRoot) h += lineH;

            // Task type popup (non-root)
            if (isNonRoot) h += lineH;

            // Task-specific fields (only if task != null)
            if (task != null)
            {
                string nodePath = FindNodePropertyPath(property, task);
                if (nodePath != null)
                {
                    var so = property.serializedObject;
                    var nodeProp = so.FindProperty(nodePath);
                    if (nodeProp != null)
                    {
                        var iter = nodeProp.Copy();
                        var endProp = nodeProp.GetEndProperty();
                        bool enterChildren = true;
                        while (iter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iter, endProp))
                        {
                            enterChildren = false;
                            if (iter.name == "name" || iter.name == "children") continue;
                            h += EditorGUI.GetPropertyHeight(iter, true) + 2;
                        }
                    }
                }

                if (Application.isPlaying)
                    h += lineH * 4 + 8;

                if (task is Runtime.CompositeTask)
                    h += EditorGUIUtility.singleLineHeight + 6;
            }

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

            // Rebuild navList each frame
            s.navList.Clear();
            if (taskTree?.root != null)
                BuildNavList(s, taskTree.root, null, -1, s.navList);

            // Resolve deferred selection
            if (s.pendingSelectTask != null)
            {
                int idx = FindNavIndexForTask(s, s.pendingSelectTask);
                if (idx >= 0) SelectSingle(s, idx);
                s.pendingSelectTask = null;
            }
            else if (s.pendingSelectEmptyParent != null && s.pendingSelectEmptyIndex >= 0)
            {
                int idx = FindNavIndexForEmpty(s, s.pendingSelectEmptyParent, s.pendingSelectEmptyIndex);
                if (idx >= 0) SelectSingle(s, idx);
                s.pendingSelectEmptyParent = null;
                s.pendingSelectEmptyIndex = -1;
            }

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

            EditorGUI.indentLevel++;
            var indentedRef = EditorGUI.IndentedRect(new Rect(position.x, y, position.width, 0));
            EditorGUI.indentLevel--;
            float contentX = indentedRef.x;
            float contentW = position.xMax - contentX;

            // -- Import/Export Foldout --
            var ieHeaderRect = new Rect(contentX, y, contentW, EditorGUIUtility.singleLineHeight);
            SetFoldout(s, FoldoutImportExport, EditorGUI.Foldout(ieHeaderRect, GetFoldout(s, FoldoutImportExport), "Import / Export", true));
            y += EditorGUIUtility.singleLineHeight + 2;

            if (GetFoldout(s, FoldoutImportExport))
            {
                var ieRect = new Rect(contentX, y, contentW, EditorGUIUtility.singleLineHeight * 3 + 8);
                DrawImportExport(s, ieRect, property, taskTree);
                y += ieRect.height;
            }

            // -- Hierarchy Foldout --
            var hHeaderRect = new Rect(contentX, y, contentW, EditorGUIUtility.singleLineHeight);
            SetFoldout(s, FoldoutHierarchy, EditorGUI.Foldout(hHeaderRect, GetFoldout(s, FoldoutHierarchy), "Hierarchy", true));
            y += EditorGUIUtility.singleLineHeight + 2;

            if (GetFoldout(s, FoldoutHierarchy))
            {
                var hierarchyRect = new Rect(contentX, y, contentW, s.hierarchyHeight + SearchHeight);

                // Focus/unfocus: click inside → focus, click outside → unfocus
                if (Event.current.type == EventType.MouseDown)
                {
                    bool insideHierarchy = hierarchyRect.Contains(Event.current.mousePosition);
                    if (insideHierarchy && !s.hierarchyFocused)
                    {
                        s.hierarchyFocused = true;
                        HandleUtility.Repaint();
                    }
                    else if (!insideHierarchy && s.hierarchyFocused)
                    {
                        s.hierarchyFocused = false;
                        HandleUtility.Repaint();
                    }
                }

                // Process events BEFORE drawing (only keyboard when focused)
                if (s.hierarchyFocused)
                    HandleKeyboard(s, taskTree, targetObj);
                HandleDrag(s, taskTree, targetObj);

                // Focus border
                if (s.hierarchyFocused && Event.current.type == EventType.Repaint)
                {
                    var borderColor = new Color(0.24f, 0.49f, 0.91f, 0.8f);
                    EditorGUI.DrawRect(new Rect(hierarchyRect.x, hierarchyRect.y, hierarchyRect.width, 1), borderColor);
                    EditorGUI.DrawRect(new Rect(hierarchyRect.x, hierarchyRect.yMax - 1, hierarchyRect.width, 1), borderColor);
                    EditorGUI.DrawRect(new Rect(hierarchyRect.x, hierarchyRect.y, 1, hierarchyRect.height), borderColor);
                    EditorGUI.DrawRect(new Rect(hierarchyRect.xMax - 1, hierarchyRect.y, 1, hierarchyRect.height), borderColor);
                }

                DrawHierarchy(s, hierarchyRect, taskTree, targetObj, property);
                y += hierarchyRect.height;

                // Resize handle
                var resizeHandleRect = new Rect(contentX, y, contentW, 6);
                EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeVertical);
                if (Event.current.type == EventType.Repaint)
                {
                    var gripColor = new Color(0.4f, 0.4f, 0.4f);
                    EditorGUI.DrawRect(new Rect(contentX + contentW * 0.5f - 12, y + 2, 24, 2), gripColor);
                }
                HandleResize(s, resizeHandleRect);
                y += 6;
            }

            // -- Execute button (play mode) --
            if (Application.isPlaying && taskTree != null)
            {
                y += 2;
                var btnRect = new Rect(contentX, y, contentW, EditorGUIUtility.singleLineHeight);
                if (GUI.Button(btnRect, "▶  Execute"))
                    taskTree.Execute();
                y += EditorGUIUtility.singleLineHeight + 2;
            }

            // -- Inspector Foldout --
            bool hasSelection = s.SelectedTask != null;
            bool hasEmptySelection = s.PrimaryEntry?.IsEmpty == true;
            if (hasSelection || hasEmptySelection)
            {
                var inspFoldRect = new Rect(contentX, y, contentW, EditorGUIUtility.singleLineHeight);
                SetFoldout(s, FoldoutInspector, EditorGUI.Foldout(inspFoldRect, GetFoldout(s, FoldoutInspector), "Inspector", true));
                y += EditorGUIUtility.singleLineHeight + 2;

                if (GetFoldout(s, FoldoutInspector))
                {
                    // Resolve which child entry is selected (normal or empty)
                    Runtime.CompositeTask selParent;
                    int selChildIdx;
                    if (hasSelection)
                    {
                        FindParent(taskTree.root, s.SelectedTask, out selParent, out selChildIdx);
                    }
                    else
                    {
                        var entry = s.PrimaryEntry.Value;
                        selParent = entry.parent;
                        selChildIdx = entry.childIndex;
                    }

                    bool isNonRoot = hasSelection ? s.SelectedTask != taskTree.root : true;
                    float lineH = EditorGUIUtility.singleLineHeight;

                    // -- Row 1: [Enabled] [Name] --
                    if (isNonRoot && selParent != null && selChildIdx >= 0 && selChildIdx < selParent.children.Count)
                    {
                        var childData = selParent.children[selChildIdx];
                        float toggleW = 18f;

                        // Enabled
                        EditorGUI.BeginChangeCheck();
                        bool newEnabled = EditorGUI.Toggle(new Rect(contentX, y, toggleW, lineH), childData.enabled);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RegisterCompleteObjectUndo(targetObj, "Toggle Enabled");
                            childData.enabled = newEnabled;
                            MarkDirty(targetObj);
                        }

                        // Name
                        string curName = childData.task?.name ?? "";
                        EditorGUI.BeginChangeCheck();
                        string newName = EditorGUI.TextField(new Rect(contentX + toggleW + 2, y, contentW - toggleW - 2, lineH), curName);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RegisterCompleteObjectUndo(targetObj, "Rename");
                            if (childData.task != null) childData.task.name = newName;
                            MarkDirty(targetObj);
                        }
                        y += lineH + 2;

                        // -- Row 2: SubTaskValue --
                        EditorGUI.BeginChangeCheck();
                        float newSv = EditorGUI.FloatField(new Rect(contentX, y, contentW, lineH),
                            new GUIContent("Sub Task Value"), childData.subTaskValue);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RegisterCompleteObjectUndo(targetObj, "Edit SubTaskValue");
                            childData.subTaskValue = Mathf.Max(0f, newSv);
                            MarkDirty(targetObj);
                        }
                        y += lineH + 2;
                    }
                    else if (!isNonRoot && hasSelection)
                    {
                        // Root: name only
                        EditorGUI.BeginChangeCheck();
                        string newName = EditorGUI.TextField(new Rect(contentX, y, contentW, lineH),
                            new GUIContent("Name"), s.SelectedTask.name ?? "");
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RegisterCompleteObjectUndo(targetObj, "Rename");
                            s.SelectedTask.name = newName;
                            MarkDirty(targetObj);
                        }
                        y += lineH + 2;
                    }

                    // -- Task Type popup (non-root) --
                    if (isNonRoot && selParent != null && selChildIdx >= 0)
                    {
                        y = DrawTaskTypeForChild(s, selParent, selChildIdx, taskTree, targetObj, contentX, y, contentW);
                    }

                    // -- Task-specific fields (only if task != null) --
                    if (hasSelection && s.SelectedTask != null)
                    {
                        string nodePath = FindNodePropertyPath(property, s.SelectedTask);
                        if (nodePath != null)
                        {
                            var so = property.serializedObject;
                            so.Update();
                            var nodeProp = so.FindProperty(nodePath);
                            if (nodeProp != null)
                            {
                                // Draw remaining task properties (skip name, children — already handled above)
                                var iter = nodeProp.Copy();
                                var endProp = nodeProp.GetEndProperty();
                                bool enterChildren = true;
                                while (iter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iter, endProp))
                                {
                                    enterChildren = false;
                                    if (iter.name == "name" || iter.name == "children") continue;
                                    float propH = EditorGUI.GetPropertyHeight(iter, true);
                                    EditorGUI.PropertyField(new Rect(contentX, y, contentW, propH), iter, true);
                                    y += propH + 2;
                                }
                                if (so.ApplyModifiedProperties())
                                    MarkDirty(targetObj);
                            }
                        }

                        // Runtime inspector (Play Mode)
                        if (Application.isPlaying)
                            y = DrawRuntimeInspector(s.SelectedTask, contentX, y, contentW);

                        // Add child button for CompositeTask
                        if (s.SelectedTask is Runtime.CompositeTask comp)
                        {
                            y += 4;
                            var addBtnRect = new Rect(contentX, y, contentW, lineH);
                            if (GUI.Button(addBtnRect, "+ Add Child"))
                                ShowAddChildPopup(s, addBtnRect, comp, taskTree, targetObj);
                            y += lineH + 2;
                        }
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

            Runtime.CompositeTask newRoot;
            try { newRoot = JsonConvert.DeserializeObject<Runtime.CompositeTask>(json, GetJsonSettings(s)); }
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
            ApplyScrollToSelection(s, taskTree);
            s.hierarchyScroll = GUI.BeginScrollView(scrollRect, s.hierarchyScroll,
                new Rect(0, 0, scrollRect.width - 16, contentH));

            s.contentLocalMouseY = Event.current.mousePosition.y;

            float y = 0;
            DrawNodeRow(s, taskTree.root, null, -1, 0, ref y, scrollRect.width, taskTree, targetObj, property);

            // Drop target detection INSIDE scroll view
            if (s.dragActive && s.dropNeedsUpdate)
            {
                s.dropNeedsUpdate = false;
                UpdateDropTarget(s, taskTree);
            }

            GUI.EndScrollView();

            // Auto-scroll when dragging near top/bottom edges
            if (s.dragActive)
            {
                float mouseScreenY = Event.current.mousePosition.y;
                float edgeZone = 20f;
                float scrollSpeed = 150f;

                if (mouseScreenY < scrollRect.y + edgeZone && mouseScreenY >= scrollRect.y)
                {
                    float t = 1f - (mouseScreenY - scrollRect.y) / edgeZone;
                    s.hierarchyScroll.y -= scrollSpeed * t * 0.016f;
                    s.hierarchyScroll.y = Mathf.Max(0, s.hierarchyScroll.y);
                    HandleUtility.Repaint();
                }
                else if (mouseScreenY > scrollRect.yMax - edgeZone && mouseScreenY <= scrollRect.yMax)
                {
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
                    SelectSingle(s, -1);
                    ClearTextFieldFocus();
                    HandleUtility.Repaint();
                }
            }

            // Drop indicator
            if (s.dragActive && s.dropValid)
                DrawDropIndicator(s, taskTree, scrollRect);
        }

        static float GetTreeContentHeight(DrawerState s, ATask node)
        {
            if (node == null) return RowHeight; // null task = 1 empty row
            if (!IsVisibleBySearch(s, node)) return 0;
            float h = RowHeight;
            if (node is Runtime.CompositeTask comp && IsExpanded(s, comp) && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch != null)
                        h += ch.task != null ? GetTreeContentHeight(s, ch.task) : RowHeight;
            return h;
        }

        static void DrawNodeRow(DrawerState s, ATask node, Runtime.CompositeTask parent, int indexInParent,
                         int depth, ref float y, float width, TaskTree taskTree,
                         UnityEngine.Object targetObj, SerializedProperty property)
        {
            if (!IsVisibleBySearch(s, node)) return;

            var e       = Event.current;
            var rowRect = new Rect(0, y, width, RowHeight);
            float indent = RowPadLeft + depth * IndentStep;
            int navIdx  = FindNavIndexForTask(s, node);
            bool isSelected = navIdx >= 0 && s.IsNavSelected(navIdx);
            bool isEnabled  = IsNodeEnabled(s, node);
            bool isRoot     = taskTree.root == node;

            // Background
            if (isSelected)
                EditorGUI.DrawRect(rowRect, SelectionHighlight);
            else if (rowRect.Contains(e.mousePosition) && e.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, HoverHighlight);

            float cx = indent;

            // Foldout
            if (node is Runtime.CompositeTask compNode)
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
                    TaskStatus.Running   => StatusRunning,
                    TaskStatus.Finishing => StatusRunning,
                    TaskStatus.Completed => StatusCompleted,
                    TaskStatus.Failed    => StatusFailed,
                    _                    => StatusPending,
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
                Runtime.CompositeTask c => c.executionMode == ExecutionMode.Sequential ? "[Seq]" : "[Par]",
                _               => "[Task]",
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

                    if (e.clickCount == 2 && node == s.SelectedTask)
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
                            if (navIdx >= 0 && s.IsNavSelected(navIdx))
                            {
                                s.selectedNavIndices.Remove(navIdx);
                                // Update primary to first remaining, or -1
                                s.primaryNavIndex = s.selectedNavIndices.Count > 0
                                    ? s.selectedNavIndices.First() : -1;
                            }
                            else if (navIdx >= 0)
                            {
                                s.selectedNavIndices.Add(navIdx);
                                s.primaryNavIndex = navIdx;
                            }
                            s.anchorNavIndex = navIdx;
                        }
                        else if (shift && s.anchorNavIndex >= 0 && navIdx >= 0)
                        {
                            SelectRange(s, navIdx);
                        }
                        else if (navIdx >= 0 && s.IsNavSelected(navIdx))
                        {
                            s.primaryNavIndex = navIdx;
                            s.anchorNavIndex = navIdx;
                        }
                        else if (navIdx >= 0)
                        {
                            SelectSingle(s, navIdx);
                        }

                        ClearTextFieldFocus();
                        s.dragActive  = false;
                        s.dragNeedCaptureStart = true;
                        s.draggedNode = node;
                        e.Use();
                    }
                }
                else if (e.button == 1)
                {
                    if (navIdx >= 0 && !s.IsNavSelected(navIdx))
                        SelectSingle(s, navIdx);
                }
            }

            y += RowHeight;

            // Children
            if (node is Runtime.CompositeTask composite && IsExpanded(s, composite) && composite.children != null)
                for (int i = 0; i < composite.children.Count; i++)
                {
                    var child = composite.children[i];
                    if (child == null) continue;
                    if (child.task != null)
                        DrawNodeRow(s, child.task, composite, i, depth + 1, ref y, width,
                                    taskTree, targetObj, property);
                    else
                        DrawEmptyChildRow(s, composite, i, depth + 1, ref y, width, taskTree, targetObj);
                }
        }

        /// <summary>
        /// Vẽ row cho child có task == null. Hiển thị đỏ "(empty)".
        /// Click để select → user chọn type từ Task Type dropdown trong inspector.
        /// </summary>
        static void DrawEmptyChildRow(DrawerState s, Runtime.CompositeTask parent, int indexInParent,
                                       int depth, ref float y, float width, TaskTree taskTree,
                                       UnityEngine.Object targetObj)
        {
            var e = Event.current;
            var rowRect = new Rect(0, y, width, RowHeight);
            float indent = RowPadLeft + depth * IndentStep + FoldoutW;
            int emptyNavIdx = FindNavIndexForEmpty(s, parent, indexInParent);
            bool isSelected = emptyNavIdx >= 0 && s.IsNavSelected(emptyNavIdx);

            // Background
            if (isSelected)
                EditorGUI.DrawRect(rowRect, SelectionHighlight);
            else if (rowRect.Contains(e.mousePosition) && e.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, HoverHighlight);

            // Badge + name
            if (e.type == EventType.Repaint)
            {
                GUI.Label(new Rect(indent, y, BadgeW, RowHeight), "[---]", s.dimLabelStyle);
                s.nodeNameStyle.normal.textColor = ErrorTextColor;
                GUI.Label(new Rect(indent + BadgeW + BadgePadding, y, width - indent - BadgeW - 8, RowHeight),
                    "(empty)", s.nodeNameStyle);
            }

            // Click → select empty child
            if (e.type == EventType.MouseDown && e.button == 0 && rowRect.Contains(e.mousePosition))
            {
                if (emptyNavIdx >= 0)
                    SelectSingle(s, emptyNavIdx);
                ClearTextFieldFocus();
                e.Use();
            }

            // Right-click → delete
            if (e.type == EventType.MouseDown && e.button == 1 && rowRect.Contains(e.mousePosition))
            {
                var menu = new GenericMenu();
                var capturedParent = parent;
                var capturedIdx = indexInParent;
                var capturedTarget = targetObj;
                menu.AddItem(new GUIContent("Delete"), false, () =>
                {
                    Undo.RegisterCompleteObjectUndo(capturedTarget, "Delete Empty Child");
                    capturedParent.children.RemoveAt(capturedIdx);
                    MarkDirty(capturedTarget);
                });
                menu.ShowAsContext();
                e.Use();
            }

            y += RowHeight;
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
            { CancelDrag(s); return; }

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

            if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove)
            {
                s.dropNeedsUpdate = true;
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

        struct DropSlot
        {
            public ATask node;
            public Runtime.CompositeTask parent;
            public int indexInParent;
            public float y;
        }

        static void BuildDropSlots(DrawerState s, ATask node, Runtime.CompositeTask parent, int idx,
                                    ref float y, List<DropSlot> slots)
        {
            if (node == null || !IsVisibleBySearch(s, node)) return;
            slots.Add(new DropSlot { node = node, parent = parent, indexInParent = idx, y = y });
            y += RowHeight;
            if (node is Runtime.CompositeTask comp && IsExpanded(s, comp) && comp.children != null)
                for (int i = 0; i < comp.children.Count; i++)
                    if (comp.children[i]?.task != null)
                        BuildDropSlots(s, comp.children[i].task, comp, i, ref y, slots);
        }

        /// <summary>
        /// Detect drop target. Must be called INSIDE GUI.BeginScrollView.
        /// Splits each row into 3 zones:
        ///   Top 25%:    insert BEFORE (sibling)
        ///   Middle 50%: insert INTO (child) - only for Runtime.CompositeTask
        ///   Bottom 25%: insert AFTER (sibling)
        /// Leaf nodes: top 50% (before) / bottom 50% (after).
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
                if (s.draggedNode == slot.node) break;

                bool isComp = slot.node is Runtime.CompositeTask;
                float relY = localY - slot.y;

                int zone;
                if (isComp)
                {
                    if (relY < RowHeight * 0.25f) zone = 0;
                    else if (relY < RowHeight * 0.75f) zone = 1;
                    else zone = 2;
                }
                else
                {
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
                    var comp = (Runtime.CompositeTask)slot.node;
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

            int dragNavIdx = FindNavIndexForTask(s, s.draggedNode);
            var selectedTasks = GetSelectedTasks(s);
            var nodesToMove = (dragNavIdx >= 0 && s.IsNavSelected(dragNavIdx))
                ? new List<ATask>(selectedTasks)
                : new List<ATask> { s.draggedNode };

            nodesToMove.RemoveAll(n => n == null || n == taskTree.root || IsAncestorOrSelf(n, s.dropParentTarget));
            nodesToMove.RemoveAll(n => nodesToMove.Any(other => other != n && IsAncestorOrSelf(other, n)));

            if (nodesToMove.Count == 0) return;

            Undo.RegisterCompleteObjectUndo(targetObj, "Move Nodes");

            int adjustedInsert = s.dropInsertIndex;

            var svMap = new Dictionary<ATask, float>();
            foreach (var node in nodesToMove)
            {
                FindParent(taskTree.root, node, out var oldParent, out int oldIdx);
                if (oldParent == null || oldParent.children == null || oldIdx < 0 || oldIdx >= oldParent.children.Count) continue;

                svMap[node] = oldParent.children[oldIdx].subTaskValue;

                if (oldParent == s.dropParentTarget && oldIdx < adjustedInsert)
                    adjustedInsert--;

                oldParent.children.RemoveAt(oldIdx);
            }

            if (s.dropParentTarget.children == null)
                s.dropParentTarget.children = new List<Runtime.CompositeTask.Child>();
            adjustedInsert = Mathf.Clamp(adjustedInsert, 0, s.dropParentTarget.children.Count);

            foreach (var node in nodesToMove)
            {
                float sv = svMap.GetValueOrDefault(node, 0f);
                s.dropParentTarget.children.Insert(adjustedInsert, new Runtime.CompositeTask.Child
                { subTaskValue = sv, task = node });
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
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].node == s.dropParentTarget)
                    {
                        float absY = scrollRect.y + slots[i].y - s.hierarchyScroll.y;
                        if (absY >= scrollRect.y && absY + RowHeight <= scrollRect.yMax)
                        {
                            var hlRect = new Rect(scrollRect.x + 2, absY, scrollRect.width - 4, RowHeight);
                            EditorGUI.DrawRect(hlRect, new Color(0.35f, 0.8f, 1f, 0.2f));
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
                float lineY = -1;

                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].parent == s.dropParentTarget && slots[i].indexInParent == s.dropInsertIndex)
                    { lineY = slots[i].y; break; }
                }

                if (lineY < 0 && s.dropParentTarget != null)
                {
                    for (int i = slots.Count - 1; i >= 0; i--)
                    {
                        if (slots[i].parent == s.dropParentTarget &&
                            slots[i].indexInParent == s.dropInsertIndex - 1)
                        { lineY = slots[i].y + RowHeight; break; }
                    }
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

        static void ShowContextMenu(DrawerState s, ATask hitNode, TaskTree taskTree,
                             UnityEngine.Object targetObj, SerializedProperty property)
        {
            var menu = new GenericMenu();
            var target = hitNode ?? s.SelectedTask;

            if (target is Runtime.CompositeTask comp)
            {
                // Build submenu with all task types
                var entries = s.RegistryEntries;
                menu.AddItem(new GUIContent("Add Child/Composite (Sequential)"), false,
                    () => AddChild(s, comp, new Runtime.CompositeTask
                    { name = "New Composite", executionMode = ExecutionMode.Sequential,
                      children = new List<Runtime.CompositeTask.Child>() }, targetObj));
                menu.AddItem(new GUIContent("Add Child/Composite (Parallel)"), false,
                    () => AddChild(s, comp, new Runtime.CompositeTask
                    { name = "New Composite", executionMode = ExecutionMode.Parallel,
                      children = new List<Runtime.CompositeTask.Child>() }, targetObj));
                foreach (var entry in entries)
                {
                    var capturedEntry = entry;
                    menu.AddItem(new GUIContent("Add Child/" + entry.DisplayName), false, () =>
                    {
                        try
                        {
                            var t = (ATask)Activator.CreateInstance(capturedEntry.Type);
                            t.name = capturedEntry.DisplayName;
                            AddChild(s, comp, t, targetObj);
                        }
                        catch (Exception ex) { Debug.LogError($"Failed: {ex.Message}"); }
                    });
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Expand All Children"), false, () => ExpandAll(s, comp));
                menu.AddItem(new GUIContent("Collapse All Children"), false, () => CollapseAll(s, comp));
                menu.AddSeparator("");
            }

            if (target != null)
            {
                bool isRoot = target == taskTree.root;
                int multiCount = s.selectedNavIndices.Count;
                bool isMulti = multiCount > 1;

                if (!isMulti)
                    menu.AddItem(new GUIContent("Rename        F2"), false, () => StartRename(s, target));
                else
                    menu.AddDisabledItem(new GUIContent("Rename        F2"));
                menu.AddSeparator("");

                if (isMulti)
                    menu.AddItem(new GUIContent($"Duplicate {multiCount} nodes  Ctrl+D"), false,
                        () => DuplicateMultipleNodes(s, taskTree, targetObj));
                else
                    menu.AddItem(new GUIContent("Duplicate     Ctrl+D"), false,
                        () => DuplicateNode(s, target, taskTree, targetObj));

                if (isMulti)
                    menu.AddItem(new GUIContent($"Copy {multiCount} nodes  Ctrl+C"), false,
                        () => { s.clipboard = GetSelectedTasks(s); });
                else
                    menu.AddItem(new GUIContent("Copy          Ctrl+C"), false,
                        () => { s.clipboard = new List<ATask> { target }; });

                if (s.clipboard != null && s.clipboard.Count > 0 && target is Runtime.CompositeTask cp)
                    menu.AddItem(new GUIContent($"Paste {s.clipboard.Count} as Child  Ctrl+V"), false,
                        () => PasteChildren(s, cp, taskTree, targetObj));
                else
                    menu.AddDisabledItem(new GUIContent("Paste as Child  Ctrl+V"));

                menu.AddSeparator("");

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

            // Delete empty child — handle before null-selected guard
            if (e.keyCode == KeyCode.Delete && s.PrimaryEntry?.IsEmpty == true)
            {
                var entry = s.PrimaryEntry.Value;
                var ep = entry.parent;
                int ei = entry.childIndex;
                if (ep != null && ei >= 0 && ei < ep.children.Count)
                {
                    Undo.RegisterCompleteObjectUndo(targetObj, "Delete Empty Child");
                    ep.children.RemoveAt(ei);
                    MarkDirty(targetObj);
                }
                SelectSingle(s, -1);
                e.Use(); return;
            }

            // Use navList (already rebuilt in OnGUI)
            int cur = s.primaryNavIndex;

            // No selection → select root
            if (cur < 0 && s.SelectedTask == null && !(s.PrimaryEntry?.IsEmpty == true))
            {
                int rootIdx = FindNavIndexForTask(s, taskTree.root);
                if (rootIdx >= 0) SelectSingle(s, rootIdx);
                return;
            }

            if (e.keyCode == KeyCode.Delete)
            {
                if (s.selectedNavIndices.Count > 1)
                    DeleteMultipleNodes(s, taskTree, targetObj);
                else
                    DeleteNode(s, s.SelectedTask, taskTree, targetObj);
                e.Use(); return;
            }

            if (ctrl && e.keyCode == KeyCode.D && s.SelectedTask != null)
            {
                if (s.selectedNavIndices.Count > 1)
                    DuplicateMultipleNodes(s, taskTree, targetObj);
                else
                    DuplicateNode(s, s.SelectedTask, taskTree, targetObj);
                e.Use(); return;
            }
            if (ctrl && e.keyCode == KeyCode.C && s.SelectedTask != null)
            {
                s.clipboard = GetSelectedTasks(s);
                if (s.clipboard.Count == 0 && s.SelectedTask != null)
                    s.clipboard.Add(s.SelectedTask);
                e.Use(); return;
            }
            if (ctrl && e.keyCode == KeyCode.V && s.SelectedTask is Runtime.CompositeTask cv)
            { PasteChildren(s, cv, taskTree, targetObj); e.Use(); return; }

            if (e.keyCode == KeyCode.F2 && s.SelectedTask != null && s.selectedNavIndices.Count <= 1)
            { StartRename(s, s.SelectedTask); e.Use(); return; }

            if (e.alt && e.keyCode == KeyCode.LeftArrow)
            { CollapseAll(s, taskTree.root); e.Use(); return; }
            if (e.alt && e.keyCode == KeyCode.RightArrow)
            { ExpandAll(s, taskTree.root); e.Use(); return; }

            if (cur >= 0)
            {
                if (e.keyCode == KeyCode.UpArrow)
                {
                    if (cur > 0)
                    {
                        int newIdx = cur - 1;
                        if (e.shift && s.anchorNavIndex >= 0)
                        {
                            SelectRange(s, newIdx);
                            ScrollToNode(s, taskTree, s.SelectedTask);
                        }
                        else
                            SelectNavEntry(s, newIdx, taskTree);
                    }
                    e.Use(); return;
                }
                if (e.keyCode == KeyCode.DownArrow)
                {
                    if (cur < s.navList.Count - 1)
                    {
                        int newIdx = cur + 1;
                        if (e.shift && s.anchorNavIndex >= 0)
                        {
                            SelectRange(s, newIdx);
                            ScrollToNode(s, taskTree, s.SelectedTask);
                        }
                        else
                            SelectNavEntry(s, newIdx, taskTree);
                    }
                    e.Use(); return;
                }
            }

            if (e.keyCode == KeyCode.LeftArrow && s.SelectedTask != null)
            {
                if (s.SelectedTask is Runtime.CompositeTask comp && IsExpanded(s, comp))
                    SetExpanded(s, comp, false);
                else
                {
                    FindParent(taskTree.root, s.SelectedTask, out var p, out _);
                    if (p != null)
                    {
                        int pIdx = FindNavIndexForTask(s, p);
                        if (pIdx >= 0) SelectSingle(s, pIdx);
                    }
                }
                ScrollToNode(s, taskTree, s.SelectedTask);
                e.Use(); return;
            }
            if (e.keyCode == KeyCode.RightArrow)
            {
                // Composite collapsed → expand
                if (s.SelectedTask is Runtime.CompositeTask comp && !IsExpanded(s, comp))
                {
                    SetExpanded(s, comp, true);
                }
                // Otherwise → jump to next CompositeTask in nav list
                else if (cur >= 0)
                {
                    for (int i = cur + 1; i < s.navList.Count; i++)
                    {
                        if (s.navList[i].task is Runtime.CompositeTask)
                        {
                            SelectNavEntry(s, i, taskTree);
                            break;
                        }
                    }
                }
                ScrollToNode(s, taskTree, s.SelectedTask);
                e.Use();
            }
        }

        /// <summary>
        /// Navigation entry — represents either a real task or an empty child slot.
        /// </summary>
        internal struct NavEntry
        {
            public ATask task;                      // null for empty child
            public Runtime.CompositeTask parent;    // parent composite (null for root)
            public int childIndex;                  // index in parent.children (-1 for root)

            public bool IsEmpty => task == null;
        }

        internal static void BuildNavList(DrawerState s, ATask node, Runtime.CompositeTask parent, int childIdx, List<NavEntry> list)
        {
            if (node != null)
            {
                if (!IsVisibleBySearch(s, node)) return;
                list.Add(new NavEntry { task = node, parent = parent, childIndex = childIdx });
                if (node is Runtime.CompositeTask comp && IsExpanded(s, comp) && comp.children != null)
                    for (int i = 0; i < comp.children.Count; i++)
                    {
                        var ch = comp.children[i];
                        if (ch == null) continue;
                        if (ch.task != null)
                            BuildNavList(s, ch.task, comp, i, list);
                        else
                            list.Add(new NavEntry { task = null, parent = comp, childIndex = i });
                    }
            }
            else
            {
                // empty child
                list.Add(new NavEntry { task = null, parent = parent, childIndex = childIdx });
            }
        }

        /// <summary>
        /// Apply NavEntry as selection state (by nav index).
        /// </summary>
        static void SelectNavEntry(DrawerState s, int navIdx, TaskTree taskTree)
        {
            SelectSingle(s, navIdx);
            ScrollToNode(s, taskTree, s.SelectedTask);
        }

        /// <summary>
        /// Find navList index for a given task. Returns -1 if not found.
        /// </summary>
        internal static int FindNavIndexForTask(DrawerState s, ATask task)
        {
            for (int i = 0; i < s.navList.Count; i++)
                if (s.navList[i].task == task) return i;
            return -1;
        }

        /// <summary>
        /// Find navList index for an empty child entry. Returns -1 if not found.
        /// </summary>
        internal static int FindNavIndexForEmpty(DrawerState s, Runtime.CompositeTask parent, int childIndex)
        {
            for (int i = 0; i < s.navList.Count; i++)
                if (s.navList[i].IsEmpty && s.navList[i].parent == parent && s.navList[i].childIndex == childIndex)
                    return i;
            return -1;
        }

        /// <summary>
        /// Select a single nav entry, setting primary, anchor, and clearing multi-selection.
        /// </summary>
        internal static void SelectSingle(DrawerState s, int navIdx)
        {
            s.primaryNavIndex = navIdx;
            s.anchorNavIndex = navIdx;
            s.selectedNavIndices.Clear();
            if (navIdx >= 0)
                s.selectedNavIndices.Add(navIdx);
        }

        /// <summary>
        /// Select range from anchor to target (inclusive), for shift+click/shift+arrow.
        /// </summary>
        static void SelectRange(DrawerState s, int targetIdx)
        {
            s.primaryNavIndex = targetIdx;
            s.selectedNavIndices.Clear();
            int from = Mathf.Min(s.anchorNavIndex, targetIdx);
            int to = Mathf.Max(s.anchorNavIndex, targetIdx);
            for (int i = from; i <= to; i++)
                s.selectedNavIndices.Add(i);
        }

        /// <summary>
        /// Get selected tasks as a list (for multi-selection operations).
        /// </summary>
        static List<ATask> GetSelectedTasks(DrawerState s)
        {
            var result = new List<ATask>();
            foreach (int idx in s.selectedNavIndices)
                if (idx >= 0 && idx < s.navList.Count && s.navList[idx].task != null)
                    result.Add(s.navList[idx].task);
            return result;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  RENAME
        // ══════════════════════════════════════════════════════════════════

        #region Rename

        static void StartRename(DrawerState s, ATask node)
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

        /// <summary>
        /// Add a child task to a CompositeTask parent.
        /// </summary>
        internal static void AddChild(DrawerState s, Runtime.CompositeTask parent, ATask task,
                                       UnityEngine.Object targetObj)
        {
            Undo.RegisterCompleteObjectUndo(targetObj, "Add Child");
            if (parent.children == null) parent.children = new List<Runtime.CompositeTask.Child>();
            parent.children.Add(new Runtime.CompositeTask.Child { subTaskValue = 0, task = task });
            SetExpanded(s, parent, true);
            if (task is Runtime.CompositeTask comp) SetExpanded(s, comp, true);
            if (task != null)
            {
                s.pendingSelectTask = task;
                s.needsScrollToSelection = true;
            }
            MarkDirty(targetObj);
        }

        /// <summary>
        /// Show a searchable popup to pick a task type, then add it as a child.
        /// </summary>
        internal static void ShowAddChildPopup(DrawerState s, Rect btnRect, Runtime.CompositeTask parent,
                                                TaskTree taskTree, UnityEngine.Object targetObj)
        {
            var concreteTypes = new List<Type>();
            var options = new List<string> { "Composite (Sequential)", "Composite (Parallel)" };
            foreach (var entry in s.RegistryEntries)
            {
                concreteTypes.Add(entry.Type);
                options.Add(entry.DisplayName);
            }

            SearchablePopup.Show(btnRect, options.ToArray(), -1, newIdx =>
            {
                ATask newTask;
                if (newIdx == 0 || newIdx == 1)
                {
                    var mode = newIdx == 0 ? ExecutionMode.Sequential : ExecutionMode.Parallel;
                    newTask = new Runtime.CompositeTask
                    {
                        name = "New Composite",
                        executionMode = mode,
                        children = new List<Runtime.CompositeTask.Child>()
                    };
                }
                else
                {
                    int typeIdx = newIdx - 2;
                    if (typeIdx < 0 || typeIdx >= concreteTypes.Count) return;
                    try
                    {
                        newTask = (ATask)Activator.CreateInstance(concreteTypes[typeIdx]);
                        newTask.name = options[newIdx];
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to create task: {ex.Message}");
                        return;
                    }
                }
                AddChild(s, parent, newTask, targetObj);
            });
        }

        static void DeleteNode(DrawerState s, ATask node, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            if (node == null || node == taskTree.root) return;
            FindParent(taskTree.root, node, out var parent, out int idx);
            if (parent == null) return;
            Undo.RegisterCompleteObjectUndo(targetObj, "Delete Node");
            parent.children.RemoveAt(idx);
            int navIdx = FindNavIndexForTask(s, node);
            if (navIdx >= 0) s.selectedNavIndices.Remove(navIdx);
            if (s.SelectedTask == node || (s.SelectedTask != null && IsDescendant(node, s.SelectedTask)))
                SelectSingle(s, -1);
            PurgeExpanded(s, taskTree);
            MarkDirty(targetObj);
        }

        static void DuplicateNode(DrawerState s, ATask node, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            if (node == null || node == taskTree.root) return;
            FindParent(taskTree.root, node, out var parent, out int idx);
            if (parent == null) return;
            var clone = CloneTask(node);
            Undo.RegisterCompleteObjectUndo(targetObj, "Duplicate Node");
            parent.children.Insert(idx + 1, new Runtime.CompositeTask.Child
            { enabled = parent.children[idx].enabled, subTaskValue = parent.children[idx].subTaskValue,
              task = clone });
            clone.name = MakeUniqueSiblingName(parent, node.name);
            s.pendingSelectTask = clone;
            s.needsScrollToSelection = true;
            MarkDirty(targetObj);
        }

        static void DeleteMultipleNodes(DrawerState s, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            var toDelete = GetSelectedTasks(s);
            toDelete.RemoveAll(n => n == null || n == taskTree.root);
            if (toDelete.Count == 0) return;

            Undo.RegisterCompleteObjectUndo(targetObj, "Delete Nodes");
            // Resolve parent+index, sort descending to avoid shift corruption on RemoveAt
            var entries = new List<(ATask node, Runtime.CompositeTask parent, int idx)>();
            foreach (var node in toDelete)
            {
                FindParent(taskTree.root, node, out var parent, out int idx);
                if (parent != null && idx >= 0 && idx < parent.children.Count)
                    entries.Add((node, parent, idx));
            }
            entries.Sort((a, b) => b.idx.CompareTo(a.idx));
            foreach (var (_, parent, idx) in entries)
                parent.children.RemoveAt(idx);
            SelectSingle(s, -1);
            PurgeExpanded(s, taskTree);
            MarkDirty(targetObj);
        }

        static void DuplicateMultipleNodes(DrawerState s, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            var toDuplicate = GetSelectedTasks(s);
            toDuplicate.RemoveAll(n => n == null || n == taskTree.root);
            if (toDuplicate.Count == 0) return;

            Undo.RegisterCompleteObjectUndo(targetObj, "Duplicate Nodes");

            // Resolve parent+index for each node, sort by descending index to avoid shift corruption
            var entries = new List<(ATask node, Runtime.CompositeTask parent, int idx)>();
            foreach (var node in toDuplicate)
            {
                FindParent(taskTree.root, node, out var parent, out int idx);
                if (parent != null) entries.Add((node, parent, idx));
            }
            entries.Sort((a, b) => b.idx.CompareTo(a.idx));

            var clonedTasks = new List<ATask>();
            foreach (var (node, parent, idx) in entries)
            {
                var clone = CloneTask(node);
                clone.name = MakeUniqueSiblingName(parent, node.name);
                parent.children.Insert(idx + 1, new Runtime.CompositeTask.Child
                { enabled = parent.children[idx].enabled, subTaskValue = parent.children[idx].subTaskValue,
                  task = clone });
                clonedTasks.Add(clone);
            }
            // Defer selection: set first clone as pending, rest will be handled after navList rebuild
            if (clonedTasks.Count > 0)
                s.pendingSelectTask = clonedTasks[0];
            MarkDirty(targetObj);
        }

        static void PasteChildren(DrawerState s, Runtime.CompositeTask parent, TaskTree taskTree, UnityEngine.Object targetObj)
        {
            if (s.clipboard == null || s.clipboard.Count == 0) return;
            Undo.RegisterCompleteObjectUndo(targetObj, "Paste Nodes");
            if (parent.children == null) parent.children = new List<Runtime.CompositeTask.Child>();

            ATask firstClone = null;
            foreach (var src in s.clipboard)
            {
                var clone = CloneTask(src);
                clone.name = MakeUniqueSiblingName(parent, src.name);
                parent.children.Add(new Runtime.CompositeTask.Child { enabled = true, subTaskValue = 0, task = clone });
                firstClone ??= clone;
            }
            SetExpanded(s, parent, true);
            s.pendingSelectTask = firstClone;
            s.needsScrollToSelection = true;
            MarkDirty(targetObj);
        }

        static void ExpandAll(DrawerState s, ATask node)
        {
            if (node is Runtime.CompositeTask comp)
            {
                SetExpanded(s, comp, true);
                if (comp.children != null)
                    foreach (var ch in comp.children)
                        if (ch?.task != null) ExpandAll(s, ch.task);
            }
        }

        static void CollapseAll(DrawerState s, ATask node)
        {
            if (node is Runtime.CompositeTask comp)
            {
                SetExpanded(s, comp, false);
                if (comp.children != null)
                    foreach (var ch in comp.children)
                        if (ch?.task != null) CollapseAll(s, ch.task);
            }
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  INSPECTOR CONTENT
        // ══════════════════════════════════════════════════════════════════

        #region Inspector

        // Special indices in the unified type popup
            /// <summary>
        /// Unified Task Type dropdown hoạt động trên child entry (parent + index).
        /// Hỗ trợ cả task != null và task == null.
        /// </summary>
        internal static float DrawTaskTypeForChild(DrawerState s, Runtime.CompositeTask parent, int childIdx,
                                           TaskTree taskTree, UnityEngine.Object targetObj,
                                           float x, float y, float w)
        {
            if (childIdx < 0 || childIdx >= parent.children.Count) return y;
            float lineH = EditorGUIUtility.singleLineHeight;
            var node = parent.children[childIdx].task;

            var concreteTypes = new List<Type>();
            var options = new List<string> { "None", "Composite (Sequential)", "Composite (Parallel)" };
            foreach (var entry in s.RegistryEntries)
            {
                concreteTypes.Add(entry.Type);
                options.Add(entry.DisplayName);
            }

            int popupIdx = TypeIdx_None;
            if (node is Runtime.CompositeTask ct)
                popupIdx = ct.executionMode == ExecutionMode.Sequential ? TypeIdx_CompositeSeq : TypeIdx_CompositePar;
            else if (node != null)
            {
                var curType = node.GetType();
                for (int i = 0; i < concreteTypes.Count; i++)
                    if (concreteTypes[i] == curType) { popupIdx = TypeIdx_FirstDefineTask + i; break; }
            }

            string currentLabel = popupIdx >= 0 && popupIdx < options.Count ? options[popupIdx] : "None";

            float labelW = EditorGUIUtility.labelWidth;
            GUI.Label(new Rect(x, y, labelW, lineH), "Task Type");
            var btnRect = new Rect(x + labelW, y, w - labelW, lineH);
            if (EditorGUI.DropdownButton(btnRect, new GUIContent(currentLabel), FocusType.Passive))
            {
                var capturedConcreteTypes = concreteTypes;
                var capturedParent = parent;
                var capturedChildIdx = childIdx;
                var capturedTarget = targetObj;
                var capturedState = s;
                var capturedTaskTree = taskTree;
                var capturedNode = node;

                SearchablePopup.Show(btnRect, options.ToArray(), popupIdx, newIdx =>
                {
                    Undo.RegisterCompleteObjectUndo(capturedTarget, "Change Task Type");
                    if (capturedChildIdx >= capturedParent.children.Count) return;

                    if (newIdx == TypeIdx_None)
                    {
                        capturedParent.children[capturedChildIdx].task = null;
                        // Select the empty child entry (deferred — navList not yet rebuilt)
                        capturedState.pendingSelectTask = null;
                        capturedState.pendingSelectEmptyParent = capturedParent;
                        capturedState.pendingSelectEmptyIndex = capturedChildIdx;
                    }
                    else if (newIdx == TypeIdx_CompositeSeq || newIdx == TypeIdx_CompositePar)
                    {
                        var mode = newIdx == TypeIdx_CompositeSeq ? ExecutionMode.Sequential : ExecutionMode.Parallel;
                        if (capturedNode is Runtime.CompositeTask existing)
                        {
                            existing.executionMode = mode;
                        }
                        else
                        {
                            var comp = new Runtime.CompositeTask
                            {
                                name = capturedNode?.name ?? "New Composite",
                                targetProgressToComplete = capturedNode?.targetProgressToComplete ?? 1f,
                                executionMode = mode,
                                children = new List<Runtime.CompositeTask.Child>()
                            };
                            capturedParent.children[capturedChildIdx].task = comp;
                            capturedState.pendingSelectTask = comp;
                        }
                    }
                    else
                    {
                        int typeIdx = newIdx - TypeIdx_FirstDefineTask;
                        if (typeIdx >= 0 && typeIdx < capturedConcreteTypes.Count)
                        {
                            try
                            {
                                var newTask = (ATask)Activator.CreateInstance(capturedConcreteTypes[typeIdx]);
                                newTask.name = capturedNode?.name ?? "New Task";
                                if (capturedNode != null)
                                    newTask.targetProgressToComplete = capturedNode.targetProgressToComplete;
                                capturedParent.children[capturedChildIdx].task = newTask;
                                capturedState.pendingSelectTask = newTask;
                            }
                            catch (Exception ex) { Debug.LogError($"Failed: {ex.Message}"); }
                        }
                    }
                    MarkDirty(capturedTarget);
                });
            }
            y += lineH + 2;
            return y;
        }

        internal const int TypeIdx_None = 0;
        internal const int TypeIdx_CompositeSeq = 1;
        internal const int TypeIdx_CompositePar = 2;
        internal const int TypeIdx_FirstDefineTask = 3;

        /// <summary>
        /// Draw runtime controls: Status, Progress bar, ForceComplete/ForceImmediate/Reset.
        /// Only called when Application.isPlaying.
        /// </summary>
        static float DrawRuntimeInspector(ATask node, float x, float y, float w)
        {
            float lineH = EditorGUIUtility.singleLineHeight;

            GUI.Label(new Rect(x, y, w, lineH), "Runtime", EditorStyles.boldLabel);
            y += lineH + 2;

            Color statusColor = node.Status switch
            {
                TaskStatus.Running   => StatusRunning,
                TaskStatus.Finishing => StatusRunning,
                TaskStatus.Completed => StatusCompleted,
                TaskStatus.Failed    => StatusFailed,
                _                    => StatusPending,
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

        static string FindNodePropertyPath(SerializedProperty taskTreeProp, ATask target)
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

        static bool FindNodePathRec(ATask current, ATask target, List<string> parts)
        {
            if (current is not Runtime.CompositeTask comp || comp.children == null) return false;
            for (int i = 0; i < comp.children.Count; i++)
            {
                var ch = comp.children[i];
                if (ch?.task == null) continue;
                parts.Add($".children.Array.data[{i}].task");
                if (ch.task == target) return true;
                if (FindNodePathRec(ch.task, target, parts)) return true;
                parts.RemoveAt(parts.Count - 1);
            }
            return false;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  DEEP CLONE
        // ══════════════════════════════════════════════════════════════════

        #region Clone

        /// <summary>
        /// Clone task: MemberwiseClone + clone tất cả serialized collection fields
        /// để tránh shared reference. UnityEngine.Object refs (Transform, GameObject...)
        /// giữ nguyên vì chúng trỏ scene objects.
        /// </summary>
        static ATask CloneTask(ATask src)
        {
            if (src == null) return null;

            var memberwiseClone = typeof(object).GetMethod("MemberwiseClone",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var clone = (ATask)memberwiseClone.Invoke(src, null);

            // Clone serialized collection fields (List<T>, T[]) để không share reference
            CloneCollectionFields(clone);

            // CompositeTask: clone children đệ quy
            if (clone is Runtime.CompositeTask compClone && src is Runtime.CompositeTask compSrc)
            {
                if (compSrc.children != null)
                {
                    compClone.children = new List<Runtime.CompositeTask.Child>();
                    foreach (var ch in compSrc.children)
                    {
                        if (ch == null) continue;
                        compClone.children.Add(new Runtime.CompositeTask.Child
                        {
                            enabled = ch.enabled,
                            subTaskValue = ch.subTaskValue,
                            task = CloneTask(ch.task)
                        });
                    }
                }
            }

            return clone;
        }

        /// <summary>
        /// Deep-clone tất cả serialized reference-type fields (trừ UnityEngine.Object).
        /// MemberwiseClone + recursive clone internal fields.
        /// </summary>
        static void CloneCollectionFields(object obj)
        {
            CloneReferenceFields(obj, 0);
        }

        static void CloneReferenceFields(object obj, int depth)
        {
            if (obj == null || depth > 4) return;
            var memberwiseClone = typeof(object).GetMethod("MemberwiseClone",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var type = obj.GetType();
            while (type != null && type != typeof(object))
            {
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                                  | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    // Only clone serialized fields
                    bool isSerialized = f.IsPublic || f.GetCustomAttribute<SerializeField>() != null;
                    if (!isSerialized) continue;
                    if (f.GetCustomAttribute<NonSerializedAttribute>() != null) continue;

                    // Skip value types, strings, UnityEngine.Object refs
                    if (f.FieldType.IsValueType) continue;
                    if (f.FieldType == typeof(string)) continue;
                    if (typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)) continue;

                    var val = f.GetValue(obj);
                    if (val == null) continue;

                    // Array → shallow clone
                    if (val is Array arr)
                    {
                        f.SetValue(obj, arr.Clone());
                    }
                    // List, Dictionary, etc. → MemberwiseClone + recurse on serialized fields only
                    else
                    {
                        try
                        {
                            var cloned = memberwiseClone.Invoke(val, null);
                            f.SetValue(obj, cloned);
                            CloneReferenceFields(cloned, depth + 1);
                        }
                        catch { /* skip */ }
                    }
                }
                type = type.BaseType;
            }
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════

        #region Helpers

        /// <summary>
        /// Get the actual managed TaskTree reference from the target object via reflection.
        /// Using property.boxedValue returns a COPY — mutations on it are lost.
        /// This method traverses the property path to return the live field reference.
        /// </summary>
        static TaskTree GetTaskTreeFromProperty(SerializedProperty property)
        {
            try
            {
                var target = property.serializedObject.targetObject;
                if (target == null) return null;

                object obj = target;
                var path = property.propertyPath.Replace(".Array.data[", "[");
                var parts = path.Split('.');

                foreach (var part in parts)
                {
                    if (obj == null) return null;

                    if (part.Contains("["))
                    {
                        var fieldName = part[..part.IndexOf('[')];
                        var indexStr = part[(part.IndexOf('[') + 1)..part.IndexOf(']')];
                        int index = int.Parse(indexStr);

                        var field = GetFieldRecursive(obj.GetType(), fieldName);
                        if (field == null) return null;
                        obj = field.GetValue(obj);

                        if (obj is System.Collections.IList list)
                            obj = index < list.Count ? list[index] : null;
                        else
                            return null;
                    }
                    else
                    {
                        var field = GetFieldRecursive(obj.GetType(), part);
                        if (field == null) return null;
                        obj = field.GetValue(obj);
                    }
                }

                return obj as TaskTree;
            }
            catch
            {
                return null;
            }
        }

        static FieldInfo GetFieldRecursive(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field;
                type = type.BaseType;
            }
            return null;
        }

        static bool IsAncestorOrSelf(ATask candidate, ATask target)
        {
            if (candidate == target) return true;
            if (candidate is Runtime.CompositeTask c && c.children != null)
                foreach (var ch in c.children)
                    if (ch?.task != null && IsAncestorOrSelf(ch.task, target)) return true;
            return false;
        }

        static bool IsDescendant(ATask root, ATask target)
        {
            if (root == target) return true;
            if (root is Runtime.CompositeTask comp && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch?.task != null && IsDescendant(ch.task, target)) return true;
            return false;
        }

        internal static void FindParent(ATask root, ATask target,
                                out Runtime.CompositeTask parent, out int index)
        {
            parent = null; index = -1;
            FindParentRec(root, target, ref parent, ref index);
        }

        static bool FindParentRec(ATask node, ATask target,
                                   ref Runtime.CompositeTask parent, ref int index)
        {
            if (node is Runtime.CompositeTask comp && comp.children != null)
                for (int i = 0; i < comp.children.Count; i++)
                {
                    var ch = comp.children[i];
                    if (ch?.task == null) continue;
                    if (ch.task == target) { parent = comp; index = i; return true; }
                    if (FindParentRec(ch.task, target, ref parent, ref index)) return true;
                }
            return false;
        }

        static string MakeUniqueSiblingName(Runtime.CompositeTask parent, string original)
        {
            if (parent?.children == null) return original;
            if (string.IsNullOrEmpty(original)) original = "New Task";
            var names = new List<string>();
            foreach (var ch in parent.children)
                if (ch?.task != null && !string.IsNullOrEmpty(ch.task.name))
                    names.Add(ch.task.name);
            return ObjectNames.GetUniqueName(names.ToArray(), original);
        }

        static bool IsExpanded(DrawerState s, Runtime.CompositeTask node)
        {
            if (!s.expanded.TryGetValue(node, out bool v)) { s.expanded[node] = true; return true; }
            return v;
        }

        static void SetExpanded(DrawerState s, Runtime.CompositeTask node, bool value) => s.expanded[node] = value;

        static Vector2 ScreenToScrollLocal(DrawerState s, Vector2 mousePos)
        {
            return new Vector2(
                mousePos.x - s.hierarchyScrollRect.x,
                mousePos.y - s.hierarchyScrollRect.y + s.hierarchyScroll.y);
        }

        static ATask HitTestNode(DrawerState s, ATask node, Vector2 localPos)
        {
            float y = 0;
            return HitTestRec(s, node, localPos, ref y);
        }

        static ATask HitTestRec(DrawerState s, ATask node, Vector2 pos, ref float y)
        {
            if (node == null || !IsVisibleBySearch(s, node)) return null;
            var r = new Rect(0, y, 10000, RowHeight);
            y += RowHeight;
            if (r.Contains(pos)) return node;
            if (node is Runtime.CompositeTask comp && IsExpanded(s, comp) && comp.children != null)
                foreach (var ch in comp.children)
                {
                    if (ch?.task == null) continue;
                    var result = HitTestRec(s, ch.task, pos, ref y);
                    if (result != null) return result;
                }
            return null;
        }

        static bool IsVisibleBySearch(DrawerState s, ATask node)
        {
            if (string.IsNullOrEmpty(s.searchFilter)) return true;
            return SubtreeMatch(node, s.searchFilter);
        }

        static bool SubtreeMatch(ATask node, string filter)
        {
            if (node == null) return false;
            if (!string.IsNullOrEmpty(node.name) &&
                node.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (node is Runtime.CompositeTask comp && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch?.task != null && SubtreeMatch(ch.task, filter)) return true;
            return false;
        }

        static bool IsNodeEnabled(DrawerState s, ATask node)
        {
            return s.enabledCache.TryGetValue(node, out bool v) ? v : true;
        }

        internal static void RebuildEnabledCache(DrawerState s, TaskTree taskTree)
        {
            s.enabledCache.Clear();
            if (taskTree == null) return;
            CacheEnabledRec(s, taskTree.root, null, -1, true);
        }

        static void CacheEnabledRec(DrawerState s, ATask node, Runtime.CompositeTask parent, int idx, bool parentEnabled)
        {
            bool self = parentEnabled;
            if (parent?.children != null && idx >= 0 && idx < parent.children.Count)
                self = parentEnabled && parent.children[idx].enabled;
            s.enabledCache[node] = self;
            if (node is Runtime.CompositeTask comp && comp.children != null)
                for (int i = 0; i < comp.children.Count; i++)
                    if (comp.children[i]?.task != null)
                        CacheEnabledRec(s, comp.children[i].task, comp, i, self);
        }

        static void PurgeExpanded(DrawerState s, TaskTree taskTree)
        {
            if (taskTree == null) { s.expanded.Clear(); return; }
            var alive = new HashSet<ATask>();
            CollectAll(taskTree.root, alive);
            var remove = new List<ATask>();
            foreach (var k in s.expanded.Keys) if (!alive.Contains(k)) remove.Add(k);
            foreach (var k in remove) s.expanded.Remove(k);
        }

        static void CollectAll(ATask node, HashSet<ATask> set)
        {
            if (node == null) return;
            set.Add(node);
            if (node is Runtime.CompositeTask comp && comp.children != null)
                foreach (var ch in comp.children)
                    if (ch?.task != null) CollectAll(ch.task, set);
        }


        internal static void HandleResize(DrawerState s, Rect handleRect)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && handleRect.Contains(e.mousePosition))
            {
                s.resizing = true;
                s.resizeDragStartY = e.mousePosition.y;
                s.resizeDragStartHeight = s.hierarchyHeight;
                e.Use();
            }
            if (s.resizing)
            {
                if (e.type == EventType.MouseDrag)
                {
                    float delta = e.mousePosition.y - s.resizeDragStartY;
                    s.hierarchyHeight = Mathf.Clamp(s.resizeDragStartHeight + delta, 80f, 800f);
                    HandleUtility.Repaint();
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    s.resizing = false;
                    e.Use();
                }
            }
        }

        internal static void ScrollToNode(DrawerState s, TaskTree taskTree, ATask target)
        {
            s.needsScrollToSelection = true;
        }

        static void ApplyScrollToSelection(DrawerState s, TaskTree taskTree)
        {
            if (!s.needsScrollToSelection) return;
            s.needsScrollToSelection = false;
            if (taskTree?.root == null) return;

            // Use primaryNavIndex directly (navList is already rebuilt)
            int idx = s.primaryNavIndex;
            if (idx < 0 || idx >= s.navList.Count) return;

            // Y position = idx * RowHeight (all rows are same height)
            float nodeY = idx * RowHeight;
            float viewH = s.hierarchyHeight;

            if (nodeY < s.hierarchyScroll.y)
                s.hierarchyScroll.y = nodeY;
            else if (nodeY + RowHeight > s.hierarchyScroll.y + viewH)
                s.hierarchyScroll.y = nodeY + RowHeight - viewH;
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
    /// OdinValueDrawer for TaskTree (V2).
    /// Hierarchy drawn with IMGUI rect-based, Inspector uses Odin InspectorProperty.Draw().
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
                _state.foldoutKeyPrefix = "TaskTreePD_V2_" + key + "_";
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

            // Rebuild navList each frame
            s.navList.Clear();
            if (taskTree?.root != null)
                TaskTreePropertyDrawer.BuildNavList(s, taskTree.root, null, -1, s.navList);

            // Resolve deferred selection
            if (s.pendingSelectTask != null)
            {
                int idx = TaskTreePropertyDrawer.FindNavIndexForTask(s, s.pendingSelectTask);
                if (idx >= 0) TaskTreePropertyDrawer.SelectSingle(s, idx);
                s.pendingSelectTask = null;
            }
            else if (s.pendingSelectEmptyParent != null && s.pendingSelectEmptyIndex >= 0)
            {
                int idx = TaskTreePropertyDrawer.FindNavIndexForEmpty(s, s.pendingSelectEmptyParent, s.pendingSelectEmptyIndex);
                if (idx >= 0) TaskTreePropertyDrawer.SelectSingle(s, idx);
                s.pendingSelectEmptyParent = null;
                s.pendingSelectEmptyIndex = -1;
            }

            // Main foldout
            this.Property.State.Expanded = Sirenix.Utilities.Editor.SirenixEditorGUI.Foldout(
                this.Property.State.Expanded, label ?? new GUIContent("Task Tree"));

            if (!this.Property.State.Expanded) return;

            EditorGUI.indentLevel++;

            // -- Import/Export Foldout --
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

            // -- Hierarchy Foldout --
            TaskTreePropertyDrawer.SetFoldout(s, TaskTreePropertyDrawer.FoldoutHierarchy,
                EditorGUILayout.Foldout(
                    TaskTreePropertyDrawer.GetFoldout(s, TaskTreePropertyDrawer.FoldoutHierarchy),
                    "Hierarchy", true));
            if (TaskTreePropertyDrawer.GetFoldout(s, TaskTreePropertyDrawer.FoldoutHierarchy))
            {
                float hierarchyH = s.hierarchyHeight + TaskTreePropertyDrawer.SearchHeight;
                var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, hierarchyH));

                // Focus/unfocus
                if (Event.current.type == EventType.MouseDown)
                {
                    bool inside = rect.Contains(Event.current.mousePosition);
                    if (inside && !s.hierarchyFocused) { s.hierarchyFocused = true; HandleUtility.Repaint(); }
                    else if (!inside && s.hierarchyFocused) { s.hierarchyFocused = false; HandleUtility.Repaint(); }
                }

                if (s.hierarchyFocused)
                    TaskTreePropertyDrawer.HandleKeyboard(s, taskTree, targetObj);
                TaskTreePropertyDrawer.HandleDrag(s, taskTree, targetObj);

                // Focus border
                if (s.hierarchyFocused && Event.current.type == EventType.Repaint)
                {
                    var bc = new Color(0.24f, 0.49f, 0.91f, 0.8f);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), bc);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), bc);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), bc);
                    EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), bc);
                }

                var serializedProp = this.Property.Tree.UnitySerializedObject?.FindProperty(this.Property.UnityPropertyPath);
                if (serializedProp != null)
                    TaskTreePropertyDrawer.DrawHierarchy(s, rect, taskTree, targetObj, serializedProp);

                // Resize handle
                var resizeRect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, 6));
                EditorGUIUtility.AddCursorRect(resizeRect, MouseCursor.ResizeVertical);
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(resizeRect.x + resizeRect.width * 0.5f - 12, resizeRect.y + 2, 24, 2), new Color(0.4f, 0.4f, 0.4f));
                TaskTreePropertyDrawer.HandleResize(s, resizeRect);
            }

            // -- Execute button (play mode) --
            if (Application.isPlaying && taskTree != null)
            {
                if (GUILayout.Button("▶  Execute"))
                    taskTree.Execute();
            }

            // -- Inspector Foldout --
            bool odinHasEmpty = s.PrimaryEntry?.IsEmpty == true;
            if (s.SelectedTask != null || odinHasEmpty)
            {
                TaskTreePropertyDrawer.SetFoldout(s, TaskTreePropertyDrawer.FoldoutInspector,
                    EditorGUILayout.Foldout(
                        TaskTreePropertyDrawer.GetFoldout(s, TaskTreePropertyDrawer.FoldoutInspector),
                        "Inspector", true));

                if (TaskTreePropertyDrawer.GetFoldout(s, TaskTreePropertyDrawer.FoldoutInspector))
                {
                    EditorGUI.indentLevel++;

                    // Resolve child entry
                    Runtime.CompositeTask selParent;
                    int selChildIdx;
                    if (s.SelectedTask != null)
                        TaskTreePropertyDrawer.FindParent(taskTree.root, s.SelectedTask, out selParent, out selChildIdx);
                    else
                    {
                        var entry = s.PrimaryEntry.Value;
                        selParent = entry.parent;
                        selChildIdx = entry.childIndex;
                    }

                    bool isNonRoot = s.SelectedTask != null ? s.SelectedTask != taskTree.root : true;

                    // -- Enabled + Name --
                    if (isNonRoot && selParent != null && selChildIdx >= 0 && selChildIdx < selParent.children.Count)
                    {
                        var childData = selParent.children[selChildIdx];
                        EditorGUILayout.BeginHorizontal();

                        EditorGUI.BeginChangeCheck();
                        bool newEnabled = EditorGUILayout.Toggle(GUIContent.none, childData.enabled, GUILayout.Width(18));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RegisterCompleteObjectUndo(targetObj, "Toggle Enabled");
                            childData.enabled = newEnabled;
                            TaskTreePropertyDrawer.MarkDirty(targetObj);
                        }

                        // Name
                        string curName = childData.task?.name ?? "";
                        EditorGUI.BeginChangeCheck();
                        string newName = EditorGUILayout.TextField(curName);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RegisterCompleteObjectUndo(targetObj, "Rename");
                            if (childData.task != null) childData.task.name = newName;
                            TaskTreePropertyDrawer.MarkDirty(targetObj);
                        }

                        EditorGUILayout.EndHorizontal();

                        // SubTaskValue (separate row)
                        EditorGUI.BeginChangeCheck();
                        float newSv = EditorGUILayout.FloatField("Sub Task Value", childData.subTaskValue);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RegisterCompleteObjectUndo(targetObj, "Edit SubTaskValue");
                            childData.subTaskValue = Mathf.Max(0f, newSv);
                            TaskTreePropertyDrawer.MarkDirty(targetObj);
                        }
                    }
                    else if (!isNonRoot && s.SelectedTask != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        string newName = EditorGUILayout.TextField("Name", s.SelectedTask.name ?? "");
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RegisterCompleteObjectUndo(targetObj, "Rename");
                            s.SelectedTask.name = newName;
                            TaskTreePropertyDrawer.MarkDirty(targetObj);
                        }
                    }

                    // -- Task Type (non-root) --
                    if (isNonRoot && selParent != null && selChildIdx >= 0)
                        DrawTaskTypeOdin(s, this.Property, selParent, selChildIdx, taskTree, targetObj);

                    // -- Task-specific Odin properties --
                    if (s.SelectedTask != null)
                    {
                        var nodeProp = FindOdinProperty(this.Property, taskTree, s.SelectedTask);
                        if (nodeProp != null)
                        {
                            for (int i = 0; i < nodeProp.Children.Count; i++)
                            {
                                var child = nodeProp.Children[i];
                                if (child.Name == "name" || child.Name == "children") continue;
                                child.Draw();
                            }
                        }
                    }

                    EditorGUI.indentLevel--;

                    // Runtime inspector
                    if (Application.isPlaying && s.SelectedTask != null)
                    {
                        GUILayout.Space(4);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);

                        Color statusColor = s.SelectedTask.Status switch
                        {
                            TaskStatus.Running   => TaskTreePropertyDrawer.StatusRunning,
                            TaskStatus.Finishing => TaskTreePropertyDrawer.StatusRunning,
                            TaskStatus.Completed => TaskTreePropertyDrawer.StatusCompleted,
                            TaskStatus.Failed    => TaskTreePropertyDrawer.StatusFailed,
                            _                    => TaskTreePropertyDrawer.StatusPending,
                        };
                        var oldColor = GUI.contentColor;
                        GUI.contentColor = statusColor;
                        EditorGUILayout.LabelField("Status", s.SelectedTask.Status.ToString());
                        GUI.contentColor = oldColor;

                        var progRect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, 16));
                        EditorGUI.ProgressBar(progRect, s.SelectedTask.Progress,
                            $"{s.SelectedTask.Progress * 100f:F1}%");

                        var btnRect2 = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
                        float btnW = (btnRect2.width - 8) / 3f;
                        if (GUI.Button(new Rect(btnRect2.x, btnRect2.y, btnW, btnRect2.height), "ForceComplete"))
                            s.SelectedTask.ForceComplete();
                        if (GUI.Button(new Rect(btnRect2.x + btnW + 4, btnRect2.y, btnW, btnRect2.height), "ForceImmediate"))
                            s.SelectedTask.ForceComplete(true);
                        if (GUI.Button(new Rect(btnRect2.x + 2*(btnW+4), btnRect2.y, btnW, btnRect2.height), "Reset"))
                            s.SelectedTask.Reset();
                        EditorGUI.indentLevel--;
                    }

                    // Add child button
                    if (s.SelectedTask is Runtime.CompositeTask comp)
                    {
                        GUILayout.Space(4);
                        var addRect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
                        if (GUI.Button(addRect, "+ Add Child"))
                            TaskTreePropertyDrawer.ShowAddChildPopup(s, addRect, comp, taskTree, targetObj);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Draw Task Type dropdown in Odin layout — delegates to shared rect-based DrawTaskTypeForChild.
        /// </summary>
        void DrawTaskTypeOdin(TaskTreePropertyDrawer.DrawerState s,
                               Sirenix.OdinInspector.Editor.InspectorProperty taskTreeProp,
                               Runtime.CompositeTask parent, int childIdx,
                               TaskTree taskTree, UnityEngine.Object targetObj)
        {
            var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
            TaskTreePropertyDrawer.DrawTaskTypeForChild(s, parent, childIdx, taskTree, targetObj,
                rect.x, rect.y, rect.width);
        }

        /// <summary>
        /// GUILayout-based import/export (for Odin drawer).
        /// Calls shared static methods from PropertyDrawer.
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
        /// Find InspectorProperty in Odin property tree corresponding to selected ATask.
        /// </summary>
        static Sirenix.OdinInspector.Editor.InspectorProperty FindOdinProperty(
            Sirenix.OdinInspector.Editor.InspectorProperty taskTreeProp,
            TaskTree taskTree, ATask target)
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
            ATask current, ATask target)
        {
            if (current is not Runtime.CompositeTask comp || comp.children == null) return null;

            Sirenix.OdinInspector.Editor.InspectorProperty childrenProp = null;
            for (int i = 0; i < prop.Children.Count; i++)
                if (prop.Children[i].Name == "children") { childrenProp = prop.Children[i]; break; }
            if (childrenProp == null) return null;

            int count = Mathf.Min(comp.children.Count, childrenProp.Children.Count);
            for (int i = 0; i < count; i++)
            {
                var childEntry = childrenProp.Children[i];
                if (childEntry == null) continue;

                Sirenix.OdinInspector.Editor.InspectorProperty taskProp = null;
                for (int j = 0; j < childEntry.Children.Count; j++)
                    if (childEntry.Children[j].Name == "task") { taskProp = childEntry.Children[j]; break; }
                if (taskProp == null) continue;

                if (comp.children[i]?.task == target) return taskProp;

                var result = FindOdinPropertyRec(taskProp, comp.children[i].task, target);
                if (result != null) return result;
            }
            return null;
        }
    }
#endif
}
