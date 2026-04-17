using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    public static class CompositeNodeExtensions
    {
        /// <summary>Insert an IInlineTask as a child node at runtime.</summary>
        public static TaskNode InsertInline(this CompositeNode parent, IInlineTask task, string name = null)
        {
            var go = new GameObject(name ?? $"[Inline] {task.GetType().Name}");
            go.transform.SetParent(parent.transform);
            var node = go.AddComponent<InlineTaskNode>();
            node.SetTask(task);

            return node;
        }

        /// <summary>Insert a lambda as a child node at runtime.</summary>
        public static TaskNode InsertInline(this CompositeNode parent, Func<CancellationToken, UniTask> action, string name = "Inline")
        {
            return parent.InsertInline(new LambdaInlineTask(action), name);
        }

        private class LambdaInlineTask : IInlineTask
        {
            private readonly Func<CancellationToken, UniTask> _action;
            public LambdaInlineTask(Func<CancellationToken, UniTask> action) => _action = action;
            public UniTask OnRunning(CancellationToken ct) => _action(ct);
        }
    }
}
