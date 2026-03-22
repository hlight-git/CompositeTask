using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Editor
{
    /// <summary>
    /// Cấu hình tập ITaskDefinition được phép sử dụng trong TaskTreeEditorWindow.
    /// Bạn tự tạo một asset từ menu:
    ///   Create → Task Tree → Task Definition Database
    /// và điền danh sách các task definition bạn muốn cho phép.
    /// </summary>
    [CreateAssetMenu(menuName = "Task Tree/Task Definition Database", fileName = "TaskDefinitionDatabase")]
    public class TaskDefinitionDatabase : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            /// <summary>
            /// Tên hiển thị trong editor (nếu để trống sẽ dùng tên type).
            /// </summary>
            public string displayName;

            public bool useFullNameWhenSerializationBinding;

            /// <summary>
            /// MonoScript trỏ tới class implement ITaskDefinition.
            /// </summary>
            public MonoScript script;

            /// <summary>
            /// Mô tả ngắn về task definition (hiển thị trong inspector/tool).
            /// </summary>
            [TextArea]
            public string description;
        }

        public List<Entry> entries = new List<Entry>();
    }
}