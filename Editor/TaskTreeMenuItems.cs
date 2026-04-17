using Hlight.Structures.CompositeTask.Runtime;
using UnityEditor;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Editor
{
    /// <summary>
    /// GameObject menu items for quickly scaffolding TaskTree and composite nodes.
    /// </summary>
    internal static class TaskTreeMenuItems
    {
        private const string MenuRoot = "GameObject/Composite Task/";

        // ── Task Tree ──────────────────────────────────────────────────

        [MenuItem(MenuRoot + "Task Tree", false, 10)]
        private static void CreateTaskTree(MenuCommand cmd)
        {
            var parent = cmd.context as GameObject;

            var treeGo = new GameObject("Task Tree");
            Undo.RegisterCreatedObjectUndo(treeGo, "Create Task Tree");
            if (parent != null)
                GameObjectUtility.SetParentAndAlign(treeGo, parent);
            treeGo.AddComponent<TaskTree>();

            var rootGo = new GameObject("Sequential");
            Undo.RegisterCreatedObjectUndo(rootGo, "Create Task Tree");
            GameObjectUtility.SetParentAndAlign(rootGo, treeGo);
            rootGo.AddComponent<SequentialNode>();

            Selection.activeGameObject = treeGo;
            EditorGUIUtility.PingObject(treeGo);
        }

        // ── Composite Nodes ────────────────────────────────────────────

        [MenuItem(MenuRoot + "Sequential Node", false, 30)]
        private static void CreateSequentialNode(MenuCommand cmd)
            => CreateCompositeNode<SequentialNode>("Sequential", cmd.context as GameObject);

        [MenuItem(MenuRoot + "Parallel Node", false, 31)]
        private static void CreateParallelNode(MenuCommand cmd)
            => CreateCompositeNode<ParallelNode>("Parallel", cmd.context as GameObject);

        // ── Helpers ────────────────────────────────────────────────────

        private static void CreateCompositeNode<T>(string name, GameObject parent) where T : Component
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name} Node");
            if (parent != null)
                GameObjectUtility.SetParentAndAlign(go, parent);
            go.AddComponent<T>();
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
        }
    }
}
