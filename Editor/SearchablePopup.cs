// =============================================================================
//  SearchablePopup.cs
//  Dropdown popup nhỏ có thanh search — dùng cho chọn TaskDefinition type.
// =============================================================================

using System;
using UnityEditor;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Editor
{
    /// <summary>
    /// EditorWindow popup nhỏ hiện dưới button, có search field + danh sách items.
    /// Gọi callback khi user chọn item.
    /// </summary>
    public class SearchablePopup : EditorWindow
    {
        string[]       _items;
        int            _currentIndex;
        Action<int>    _onSelect;
        string         _search = "";
        Vector2        _scroll;
        int            _hoveredIndex = -1;

        const float ItemHeight = 20f;
        const float SearchHeight = 22f;
        const float MinHeight = 100f;
        const float MaxHeight = 300f;

        /// <summary>
        /// Hiện popup ngay dưới buttonRect.
        /// items[0] thường là "None". currentIndex là index đang chọn.
        /// onSelect(newIndex) được gọi khi user click chọn.
        /// </summary>
        public static void Show(Rect buttonRect, string[] items, int currentIndex, Action<int> onSelect)
        {
            var win = CreateInstance<SearchablePopup>();
            win._items = items;
            win._currentIndex = currentIndex;
            win._onSelect = onSelect;
            win._search = "";

            float contentH = items.Length * ItemHeight + SearchHeight + 8;
            float height = Mathf.Clamp(contentH, MinHeight, MaxHeight);

            // Width: fit widest item, min = button width
            float maxTextWidth = buttonRect.width;
            var style = EditorStyles.label;
            foreach (var item in items)
            {
                float w = style.CalcSize(new GUIContent(item)).x + 16; // padding
                if (w > maxTextWidth) maxTextWidth = w;
            }
            float popupWidth = Mathf.Max(maxTextWidth, buttonRect.width);

            // Convert button rect to screen coords
            var screenPos = GUIUtility.GUIToScreenPoint(new Vector2(buttonRect.x, buttonRect.yMax));
            win.ShowAsDropDown(new Rect(screenPos, Vector2.zero),
                new Vector2(popupWidth, height));
        }

        void OnGUI()
        {
            if (_items == null) { Close(); return; }

            // Search field
            GUI.SetNextControlName("SearchField");
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            EditorGUI.FocusTextInControl("SearchField");

            // Filter items
            string filter = _search?.Trim() ?? "";
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int i = 0; i < _items.Length; i++)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    _items[i].IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                bool isSelected = i == _currentIndex;
                var rect = EditorGUILayout.GetControlRect(false, ItemHeight);

                // Hover highlight
                if (rect.Contains(Event.current.mousePosition))
                {
                    EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f, 0.5f));
                    _hoveredIndex = i;
                    Repaint();
                }

                // Selected highlight
                if (isSelected)
                    EditorGUI.DrawRect(rect, new Color(0.24f, 0.49f, 0.91f, 0.5f));

                // Label
                var style = isSelected ? EditorStyles.boldLabel : EditorStyles.label;
                GUI.Label(rect, _items[i], style);

                // Click
                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    _onSelect?.Invoke(i);
                    Event.current.Use();
                    Close();
                    return;
                }
            }

            EditorGUILayout.EndScrollView();

            // Escape to close
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
            }
        }

        void OnLostFocus() => Close();
    }
}
