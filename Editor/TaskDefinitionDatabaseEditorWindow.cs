using System;
using System.Collections.Generic;
using System.Linq;
using Hlight.Structures.CompositeTask.Runtime;
using UnityEditor;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Editor
{
    /// <summary>
    /// Tool đơn giản để:
    /// - Chọn / tạo TaskDefinitionDatabase.
    /// - Thêm entry mới từ các ITaskDefinition chưa có trong database.
    /// - Export JSON (typeName + description).
    /// 
    /// Logic theo yêu cầu:
    /// - Luôn vẽ form.
    /// - Dropdown có option "None" ở đầu.
    /// - Mỗi lần vẽ dropdown sẽ quét type ITaskDefinition một lần (chỉ dùng reflection, không quét asset).
    /// - Khi chọn "None" thì không AddEntry được.
    /// - Khi AddEntry mới, script được tìm bằng FindAssets/MonoScript đúng lúc bấm nút.
    /// </summary>
    public class TaskDefinitionDatabaseEditorWindow : EditorWindow
    {
        const string DefaultAssetPath = "Assets/Submodules/CompositeTask/Editor/TaskDefinitionDatabase.asset";

        TaskDefinitionDatabase _database;

        // Cache ứng viên hiện tại cho dropdown
        Type[]  _availableTypes   = Array.Empty<Type>();   // 0-based, không bao gồm "None"
        string[] _availableOptions = Array.Empty<string>(); // 0: "None", 1.. = type.FullName

        // Form state
        int    _selectedTypeIndex = 0; // index trong _availableOptions (0 = None)
        string _newDisplayName    = "";
        string _newDescription    = "";

        [MenuItem("Window/Task Tree/Task Definition Database")]
        public static void Open()
        {
            var win = GetWindow<TaskDefinitionDatabaseEditorWindow>("Task Definitions");
            win.minSize = new Vector2(520, 260);
            win.Show();
        }

        void OnEnable()
        {
            if (_database == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:TaskDefinitionDatabase");
                if (guids != null && guids.Length > 0)
                {
                    var loadPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _database = AssetDatabase.LoadAssetAtPath<TaskDefinitionDatabase>(loadPath);
                }
            }
        }

        void OnGUI()
        {
            EditorGUILayout.Space();
            DrawDatabaseHeader();

            if (_database == null)
            {
                EditorGUILayout.HelpBox(
                    "Không tìm thấy TaskDefinitionDatabase.\n" +
                    "Nhấn \"Create Database...\" để chọn đường dẫn và tạo một asset mới.",
                    MessageType.Info);
                if (GUILayout.Button("Create Database...", GUILayout.Height(24)))
                {
                    CreateDatabaseWithDialog();
                }
                return;
            }

            var entries = _database.entries ??= new List<TaskDefinitionDatabase.Entry>();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Add New Entry", EditorStyles.boldLabel);

            DrawAddEntryForm(entries);

            EditorGUILayout.Space();
            if (GUILayout.Button("Export Task Definitions JSON", GUILayout.Height(22)))
            {
                ExportToJson(entries);
            }
        }

        void DrawDatabaseHeader()
        {
            EditorGUILayout.BeginHorizontal();

            _database = (TaskDefinitionDatabase)EditorGUILayout.ObjectField(
                "Database Asset", _database, typeof(TaskDefinitionDatabase), false);

            if (GUILayout.Button("Find", GUILayout.Width(60)))
            {
                string[] guids = AssetDatabase.FindAssets("t:TaskDefinitionDatabase");
                if (guids != null && guids.Length > 0)
                {
                    var loadPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _database = AssetDatabase.LoadAssetAtPath<TaskDefinitionDatabase>(loadPath);
                }
            }

            if (_database != null && GUILayout.Button("Ping", GUILayout.Width(60)))
            {
                EditorGUIUtility.PingObject(_database);
            }

            EditorGUILayout.EndHorizontal();
        }

        void CreateDatabaseWithDialog()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Task Definition Database",
                System.IO.Path.GetFileName(DefaultAssetPath),
                "asset",
                "Chọn nơi lưu asset TaskDefinitionDatabase.");

            if (string.IsNullOrEmpty(path)) return;

            var db = ScriptableObject.CreateInstance<TaskDefinitionDatabase>();
            AssetDatabase.CreateAsset(db, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _database = db;
        }

        void DrawAddEntryForm(List<TaskDefinitionDatabase.Entry> entries)
        {
            // 1) Vẽ label
            EditorGUILayout.LabelField("ITaskDefinition Type", EditorStyles.miniBoldLabel);

            // 2) Lấy rect cho popup và tự vẽ bằng EditorGUI.Popup để bắt mouse
            var popupRect = EditorGUILayout.GetControlRect();
            popupRect = EditorGUI.IndentedRect(popupRect);

            var e = Event.current;
            if (e.type == EventType.MouseDown && popupRect.Contains(e.mousePosition))
            {
                // Người dùng vừa click mở dropdown → scan type một lần
                _availableTypes = FindAvailableDefinitionTypes(entries);

                _availableOptions = new string[_availableTypes.Length + 1];
                _availableOptions[0] = "None";
                for (int i = 0; i < _availableTypes.Length; i++)
                {
                    _availableOptions[i + 1] = _availableTypes[i].FullName;
                }

                _selectedTypeIndex = Mathf.Clamp(_selectedTypeIndex, 0, _availableOptions.Length - 1);
            }

            // Nếu chưa có options (chưa click bao giờ), tạo mảng mặc định với đúng một lựa chọn "None"
            if (_availableOptions == null || _availableOptions.Length == 0)
            {
                _availableOptions = new[] { "None" };
                _selectedTypeIndex = 0;
            }

            int newIndex = EditorGUI.Popup(popupRect, "Type", _selectedTypeIndex, _availableOptions);
            if (newIndex != _selectedTypeIndex)
            {
                _selectedTypeIndex = newIndex;
                if (_selectedTypeIndex > 0 &&
                    _availableTypes != null &&
                    _selectedTypeIndex - 1 < _availableTypes.Length)
                {
                    var t = _availableTypes[_selectedTypeIndex - 1];
                    _newDisplayName = t != null ? t.Name : _newDisplayName;
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
                        EditorUtility.DisplayDialog(
                            "Cannot find script",
                            $"Không tìm thấy MonoScript tương ứng cho type: {type.FullName}",
                            "OK");
                        return;
                    }

                    Undo.RecordObject(_database, "Add TaskDefinition Entry");
                    entries.Add(new TaskDefinitionDatabase.Entry
                    {
                        script      = script,
                        displayName = _newDisplayName,
                        description = _newDescription,
                    });
                    EditorUtility.SetDirty(_database);
                    AssetDatabase.SaveAssets();

                    // Reset form & cache; type list sẽ được scan lại khi click dropdown lần nữa
                    _selectedTypeIndex = 0;
                    _newDisplayName    = "";
                    _newDescription    = "";
                    _availableTypes    = Array.Empty<Type>();
                    _availableOptions  = Array.Empty<string>();
                }
            }
        }

        /// <summary>
        /// Tìm tất cả type implement ITaskDefinition, loại bỏ những cái đã có trong database.
        /// Chỉ dùng reflection (AppDomain), không đụng tới AssetDatabase nên tương đối nhẹ.
        /// </summary>
        static Type[] FindAvailableDefinitionTypes(List<TaskDefinitionDatabase.Entry> entries)
        {
            // 1) Thu thập type đã có trong DB
            var existing = new HashSet<Type>();
            if (entries != null)
            {
                foreach (var e in entries)
                {
                    if (e?.script == null) continue;
                    var t = e.script.GetClass();
                    if (t != null)
                        existing.Add(t);
                }
            }

            // 2) Quét AppDomain tìm ITaskDefinition hợp lệ
            var result = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Bỏ qua assembly không thể GetTypes
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(ITaskDefinition).IsAssignableFrom(t)) continue;
                    if (existing.Contains(t)) continue;
                    result.Add(t);
                }
            }

            return result
                .OrderBy(t => t.Name)
                .ToArray();
        }

        /// <summary>
        /// Tìm MonoScript tương ứng cho một type ITaskDefinition.
        /// Quét asset lúc bấm Add Entry (một lần), chấp nhận cost này vì không xảy ra liên tục.
        /// </summary>
        static MonoScript FindMonoScriptForType(Type type)
        {
            if (type == null) return null;

            var guids = AssetDatabase.FindAssets("t:MonoScript");
            foreach (var guid in guids)
            {
                var path   = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;
                var t = script.GetClass();
                if (t == type) return script;
            }

            return null;
        }

        void ExportToJson(List<TaskDefinitionDatabase.Entry> entries)
        {
            if (_database == null || entries == null)
            {
                EditorUtility.DisplayDialog("Export Task Definitions JSON", "Không có database để export.", "OK");
                return;
            }

            var path = EditorUtility.SaveFilePanel(
                "Export Task Definitions JSON",
                "",
                "TaskDefinitions.json",
                "json");

            if (string.IsNullOrEmpty(path)) return;

            var list = new List<SerializableEntry>();
            foreach (var e in entries)
            {
                if (e?.script == null) continue;
                var type = e.script.GetClass();
                if (type == null) continue;

                list.Add(new SerializableEntry
                {
                    typeName    = $"{type.FullName}, {type.Assembly.GetName().Name}",
                    description = e.description ?? string.Empty,
                });
            }

            var wrapper = new SerializableEntryList { entries = list.ToArray() };
            var json = JsonUtility.ToJson(wrapper, true);

            System.IO.File.WriteAllText(path, json);
            EditorUtility.RevealInFinder(path);
        }

        [Serializable]
        class SerializableEntry
        {
            public string typeName;
            public string description;
        }

        [Serializable]
        class SerializableEntryList
        {
            public SerializableEntry[] entries;
        }
    }
}