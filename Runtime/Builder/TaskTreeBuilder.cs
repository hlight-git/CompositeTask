using System;
using System.Collections.Generic;
using System.Reflection;
using Hlight.Structures.CompositeTask.Runtime.Blueprint;
using Newtonsoft.Json;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Factory that converts a BlueprintTree (pure data) into a MonoBehaviour hierarchy.
    /// Accepts an optional PrefabInstantiator delegate so callers can control how preset
    /// prefabs are instantiated (e.g. PrefabUtility.InstantiatePrefab in Editor).
    /// </summary>
    public static class TaskTreeBuilder
    {
        /// <summary>
        /// Delegate for instantiating a prefab under a parent.
        /// Default (null): Object.Instantiate (runtime, breaks prefab link).
        /// Editor: (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent).
        /// </summary>
        public delegate GameObject PrefabInstantiator(GameObject prefab, Transform parent);

        // ── Public API ────────────────────────────────────────────────

        /// <summary>
        /// Build a full task tree. Returns root GameObject with TaskTree component.
        /// </summary>
        public static GameObject Build(
            BlueprintTree blueprint,
            Transform parent = null,
            IReadOnlyList<TaskNodePreset> presets = null,
            PrefabInstantiator instantiatePrefab = null)
        {
            if (blueprint?.root == null)
            {
                Debug.LogWarning("[TaskTreeBuilder] Blueprint or root is null.");
                return null;
            }

            var rootGo = new GameObject(blueprint.root.Name ?? "TaskTree");
            if (parent != null) rootGo.transform.SetParent(parent);

            rootGo.AddComponent<TaskTree>();
            BuildNode(blueprint.root, rootGo.transform, presets, instantiatePrefab);
            return rootGo;
        }

        /// <summary>Build a single node and its children under the given parent.</summary>
        public static GameObject BuildNode(
            IBlueprint blueprint,
            Transform parent,
            IReadOnlyList<TaskNodePreset> presets = null,
            PrefabInstantiator instantiatePrefab = null)
        {
            if (blueprint == null) return null;

            if (blueprint is CompositeBlueprint composite)
                return BuildComposite(composite, parent, presets, instantiatePrefab);

            if (blueprint is LeafBlueprint leaf)
                return BuildLeaf(leaf, parent, presets, instantiatePrefab);

            Debug.LogWarning($"[TaskTreeBuilder] Unknown blueprint type: {blueprint.GetType().Name}");
            return null;
        }

        // ── Config helpers (public — used by editor) ──────────────────

        /// <summary>Apply raw config JSON to a node that implements IConfigurable.</summary>
        public static void ApplyConfigJson(TaskNode node, string configJson)
        {
            if (node is not IConfigurable || string.IsNullOrEmpty(configJson)) return;

            var configProp = node.GetType().GetProperty("Config",
                BindingFlags.Public | BindingFlags.Instance);
            if (configProp == null) return;

            try
            {
                var deserialized = JsonConvert.DeserializeObject(configJson, configProp.PropertyType);
                if (deserialized != null && node is IConfigurable configurable)
                    configurable.ApplyConfig(deserialized);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[TaskTreeBuilder] ApplyConfigJson failed on '{node.gameObject.name}': {ex.Message}");
            }
        }

        /// <summary>Look up a preset by typeId. Returns null if not found.</summary>
        public static TaskNodePreset FindPreset(IReadOnlyList<TaskNodePreset> presets, string typeId)
        {
            if (presets == null) return null;
            for (int i = 0; i < presets.Count; i++)
            {
                var p = presets[i];
                if (p.prefab != null && p.typeId == typeId) return p;
            }
            return null;
        }

        // ── Internal build ────────────────────────────────────────────

        private static GameObject BuildComposite(
            CompositeBlueprint blueprint, Transform parent,
            IReadOnlyList<TaskNodePreset> presets, PrefabInstantiator instantiatePrefab)
        {
            var go = new GameObject(blueprint.name ?? "Composite");
            go.transform.SetParent(parent, false);

            TaskNode node = blueprint.mode switch
            {
                BlueprintExecutionMode.Sequential => go.AddComponent<SequentialNode>(),
                BlueprintExecutionMode.Parallel   => go.AddComponent<ParallelNode>(),
                _                                 => go.AddComponent<SequentialNode>()
            };
            ApplyCommonProperties(node, blueprint);

            if (blueprint.children != null)
                foreach (var child in blueprint.children)
                    BuildNode(child, go.transform, presets, instantiatePrefab);

            return go;
        }

        private static GameObject BuildLeaf(
            LeafBlueprint blueprint, Transform parent,
            IReadOnlyList<TaskNodePreset> presets, PrefabInstantiator instantiatePrefab)
        {
            var preset = FindPreset(presets, blueprint.typeId);
            if (preset != null)
                return BuildLeafFromPreset(blueprint, parent, preset, instantiatePrefab);

            var nodeType = TaskNodeRegistry.GetNodeType(blueprint.typeId);
            if (nodeType == null)
            {
                Debug.LogWarning(
                    $"[TaskTreeBuilder] Unknown typeId '{blueprint.typeId}' for '{blueprint.name}'. Skipping.");
                return null;
            }

            var go = new GameObject(blueprint.name ?? blueprint.typeId);
            go.transform.SetParent(parent, false);
            var node = (TaskNode)go.AddComponent(nodeType);
            ApplyCommonProperties(node, blueprint);
            TryApplyConfig(node, blueprint);
            return go;
        }

        private static GameObject BuildLeafFromPreset(
            LeafBlueprint blueprint, Transform parent,
            TaskNodePreset preset, PrefabInstantiator instantiatePrefab)
        {
            // Use delegate if provided, otherwise runtime Instantiate
            var go = instantiatePrefab != null
                ? instantiatePrefab(preset.prefab, parent)
                : UnityEngine.Object.Instantiate(preset.prefab, parent);

            go.name = blueprint.name ?? blueprint.typeId;
            go.SetActive(blueprint.enabled);

            var node = go.GetComponent<TaskNode>();
            if (node == null)
            {
                var nodeType = TaskNodeRegistry.GetNodeType(blueprint.typeId);
                if (nodeType == null)
                {
                    Debug.LogWarning(
                        $"[TaskTreeBuilder] Preset for '{blueprint.typeId}' has no TaskNode and typeId is unknown.");
                    return go;
                }
                node = (TaskNode)go.AddComponent(nodeType);
            }

            ApplyCommonProperties(node, blueprint);
            TryApplyConfig(node, blueprint);
            return go;
        }

        // ── Private helpers ───────────────────────────────────────────

        private static void ApplyCommonProperties(TaskNode node, IBlueprint blueprint)
        {
            node.Name = blueprint.Name;
            node.gameObject.SetActive(blueprint.Enabled);
            node.Weight = blueprint.Weight;
        }

        private static void TryApplyConfig(TaskNode node, LeafBlueprint blueprint)
        {
            if (node is not IConfigurable configurable) return;

            // Path 1: typed config from LeafBlueprint<TConfig> (programmatic)
            var blueprintType = blueprint.GetType();
            if (blueprintType.IsGenericType)
            {
                var configField = blueprintType.GetField("config");
                var configValue = configField?.GetValue(blueprint);
                if (configValue != null)
                {
                    try { configurable.ApplyConfig(configValue); }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"[TaskTreeBuilder] Failed to apply typed config for '{blueprint.name}': {ex.Message}");
                    }
                    return;
                }
            }

            // Path 2: raw JSON config (from import)
            ApplyConfigJson(node, blueprint.configJson);
        }
    }
}
