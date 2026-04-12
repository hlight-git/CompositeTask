using System;
using System.Collections.Generic;
using Hlight.Structures.CompositeTask.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hlight.Structures.CompositeTask.Editor
{
    /// <summary>
    /// Draws procedural type icons and runtime status backgrounds for TaskNode
    /// GameObjects in the Hierarchy window.
    /// Icons are drawn with EditorGUI.DrawRect — no external assets needed.
    /// </summary>
    [InitializeOnLoad]
    internal static class TaskTreeHierarchyDecorator
    {
        private static readonly Dictionary<int, NodeEntry> _table = new();

        private enum NodeType { Sequential, Parallel, Leaf, Invalid }

        private struct NodeEntry
        {
            public TaskNode node;
            public NodeType type;
            public bool hasChildren;
        }

        // ── Colors ────────────────────────────────────────────────────

        private static readonly Color ColorDefault  = new(0.75f, 0.65f, 0.9f);   // light purple — distinct from plain GOs
        private static readonly Color ColorInvalid = new(0.86f, 0.27f, 0.27f);  // red — clearly wrong

        private static readonly Color StatusRunning   = new(0.2f, 0.8f, 1f);
        private static readonly Color StatusFinishing = new(1f, 0.85f, 0.2f);
        private static readonly Color StatusCompleted = new(0.2f, 0.85f, 0.3f);
        private static readonly Color StatusFailed    = new(1f, 0.25f, 0.2f);

        // ── Init ──────────────────────────────────────────────────────

        static TaskTreeHierarchyDecorator()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;

            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;

            EditorApplication.update -= RepaintDuringPlayMode;
            EditorApplication.update += RepaintDuringPlayMode;

            OnHierarchyChanged();
        }

        private static void RepaintDuringPlayMode()
        {
            if (Application.isPlaying)
                EditorApplication.RepaintHierarchyWindow();
        }

        // ── Rebuild cache ─────────────────────────────────────────────

        private static void OnPrefabStageChanged(PrefabStage _) => OnHierarchyChanged();

        private static void OnHierarchyChanged()
        {
            _table.Clear();

            var nodes = Object.FindObjectsByType<TaskNode>(FindObjectsSortMode.None);
            foreach (var node in nodes)
                RegisterNode(node);

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                foreach (var node in prefabStage.prefabContentsRoot.GetComponentsInChildren<TaskNode>(true))
                    RegisterNode(node);
            }
        }

        private static void RegisterNode(TaskNode node)
        {
            NodeType type;
            if (IsInvalidPlacement(node))
                type = NodeType.Invalid;
            else if (node is SequentialNode)
                type = NodeType.Sequential;
            else if (node is ParallelNode)
                type = NodeType.Parallel;
            else if (node is CompositeNode)
                type = NodeType.Sequential; // fallback for unknown composite
            else
                type = NodeType.Leaf;

            _table[node.gameObject.GetInstanceID()] = new NodeEntry
            {
                node        = node,
                type        = type,
                hasChildren = node.transform.childCount > 0
            };
        }

        private static bool IsInvalidPlacement(TaskNode node)
        {
            var parent = node.transform.parent;
            if (parent == null) return false;
            if (parent.GetComponent<TaskTree>() != null) return false;
            var parentNode = parent.GetComponent<TaskNode>();
            if (parentNode == null) return true; // parent is plain GO
            return !parentNode.IsValidChild(node);
        }

        // ── Draw ──────────────────────────────────────────────────────

        private static void OnHierarchyGUI(int instanceId, Rect selectionRect)
        {
            if (!_table.TryGetValue(instanceId, out var entry)) return;
            if (entry.node == null) return; // destroyed between frames

            var iconColor = ResolveIconColor(entry);

            // Icon area
            var r = new Rect(selectionRect);
            r.x -= 26;
            if (!entry.hasChildren) r.x += 13;
            r.y += 2;
            r.width  = 12;
            r.height = 12;

            var tex = entry.type switch
            {
                NodeType.Sequential => GetArrowTex(),
                NodeType.Parallel   => GetBarsTex(),
                NodeType.Leaf       => GetCircleTex(),
                NodeType.Invalid    => GetCrossTex(),
                _                   => null
            };
            if (tex != null) DrawIcon(r, iconColor, tex);
        }

        private static Color ResolveIconColor(NodeEntry entry)
        {
            // During play mode: override with status color (except Pending)
            if (Application.isPlaying)
            {
                switch (entry.node.Status)
                {
                    case TaskStatus.Running:   return StatusRunning;
                    case TaskStatus.Finishing:  return StatusFinishing;
                    case TaskStatus.Completed:  return StatusCompleted;
                    case TaskStatus.Failed:     return StatusFailed;
                }
            }

            // Default: type color
            return entry.type == NodeType.Invalid ? ColorInvalid : ColorDefault;
        }

        // ── Procedural icon textures (cached, anti-aliased) ──────────

        private const int TexSize = 32; // render at 32px, display at 12px → smooth
        private static Texture2D _texArrow, _texBars, _texCircle, _texCross;

        private static Texture2D GetArrowTex()   => _texArrow  ??= GenerateIcon(DrawArrowPixels);
        private static Texture2D GetBarsTex()    => _texBars   ??= GenerateIcon(DrawBarsPixels);
        private static Texture2D GetCircleTex()  => _texCircle ??= GenerateIcon(DrawCirclePixels);
        private static Texture2D GetCrossTex()   => _texCross  ??= GenerateIcon(DrawCrossPixels);

        private static void DrawIcon(Rect r, Color color, Texture2D tex)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            GUI.color = prev;
        }

        private static Texture2D GenerateIcon(Action<Color[], int> drawFunc)
        {
            var pixels = new Color[TexSize * TexSize];
            drawFunc(pixels, TexSize);
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static void SetPixel(Color[] pixels, int s, int x, int y, float alpha)
        {
            if (x < 0 || x >= s || y < 0 || y >= s) return;
            // Flip Y — texture origin is bottom-left, drawing origin is top-left
            pixels[(s - 1 - y) * s + x] = new Color(1, 1, 1, Mathf.Clamp01(alpha));
        }

        // ↓ Arrow with stem
        private static void DrawArrowPixels(Color[] px, int s)
        {
            float cx = s * 0.5f;
            float stemW = s * 0.18f;
            int stemTop = Mathf.RoundToInt(s * 0.1f);
            int stemBot = Mathf.RoundToInt(s * 0.5f);

            // Stem
            for (int y = stemTop; y < stemBot; y++)
                for (int x = 0; x < s; x++)
                {
                    float dist = Mathf.Abs(x - cx + 0.5f);
                    float alpha = Mathf.Clamp01(stemW * 0.5f - dist + 0.5f);
                    if (alpha > 0) SetPixel(px, s, x, y, alpha);
                }

            // Arrowhead triangle
            int headTop = stemBot;
            int headBot = Mathf.RoundToInt(s * 0.9f);
            float halfBase = s * 0.45f;
            for (int y = headTop; y <= headBot; y++)
            {
                float t = (float)(y - headTop) / Mathf.Max(1, headBot - headTop);
                float halfW = halfBase * (1f - t);
                for (int x = 0; x < s; x++)
                {
                    float dist = Mathf.Abs(x - cx + 0.5f);
                    float alpha = Mathf.Clamp01(halfW - dist + 0.5f);
                    if (alpha > 0) SetPixel(px, s, x, y, alpha);
                }
            }
        }

        // ≡ Three bars
        private static void DrawBarsPixels(Color[] px, int s)
        {
            float barH = s * 0.16f;
            float gap = (s - barH * 3) / 4f;
            float inset = s * 0.1f;

            for (int b = 0; b < 3; b++)
            {
                float barY = gap + b * (barH + gap);
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float dy = Mathf.Abs(y - (barY + barH * 0.5f) + 0.5f);
                        float dx = Mathf.Max(inset - x, x - (s - inset));
                        float alphaY = Mathf.Clamp01(barH * 0.5f - dy + 0.5f);
                        float alphaX = Mathf.Clamp01(-dx + 0.5f);
                        float alpha = alphaY * alphaX;
                        if (alpha > 0) SetPixel(px, s, x, y, Mathf.Max(px[y * s + x].a, alpha));
                    }
            }
        }

        // ● Circle
        private static void DrawCirclePixels(Color[] px, int s)
        {
            float cx = s * 0.5f;
            float cy = s * 0.5f;
            float r = s * 0.38f;

            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dist = Mathf.Sqrt((x - cx + 0.5f) * (x - cx + 0.5f) + (y - cy + 0.5f) * (y - cy + 0.5f));
                    float alpha = Mathf.Clamp01(r - dist + 0.5f);
                    if (alpha > 0) SetPixel(px, s, x, y, alpha);
                }
        }

        // ✕ Cross
        private static void DrawCrossPixels(Color[] px, int s)
        {
            float cx = s * 0.5f;
            float cy = s * 0.5f;
            float halfLen = s * 0.35f;
            float thick = s * 0.1f;
            float m = s * 0.15f;

            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x - cx + 0.5f;
                float dy = y - cy + 0.5f;

                // Distance to diagonal line y=x
                float distA = Mathf.Abs(dx - dy) * 0.7071f;
                // Distance to diagonal line y=-x
                float distB = Mathf.Abs(dx + dy) * 0.7071f;

                // Clamp to cross area (not extending beyond square)
                float fromCenter = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                if (fromCenter > halfLen + 0.5f) continue;

                float alphaA = Mathf.Clamp01(thick - distA + 0.5f);
                float alphaB = Mathf.Clamp01(thick - distB + 0.5f);
                float alpha = Mathf.Max(alphaA, alphaB);

                // Margin from edges
                float edgeDist = Mathf.Min(x, y, s - 1 - x, s - 1 - y);
                alpha *= Mathf.Clamp01(edgeDist - m + 1);

                if (alpha > 0) SetPixel(px, s, x, y, Mathf.Max(px[y * s + x].a, alpha));
            }
        }
    }
}
