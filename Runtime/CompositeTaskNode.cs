using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [Serializable]
    public class CompositeTaskNode : ATaskNode
    {
        [Serializable]
        public class Child
        {
            public bool enabled = true;
            [Min(0)]
            public float subTaskValue;
            [SerializeReference] public ATaskNode taskNode;
        }
        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        [SerializeField] public ExecutionMode executionMode;
        [SerializeField] public List<Child> children;

        public override void Accept(IDependencyInjectionVisitor dependencyInjectionVisitor)
        {
            if (children == null) return;
            foreach (var child in children)
                child?.taskNode?.Accept(dependencyInjectionVisitor);
        }

        protected override async UniTask RunTheTask(CancellationToken cancellationToken)
        {
            if (executionMode == ExecutionMode.Sequential)
            {
                foreach (var child in children)
                    if (child.enabled)
                        await ExecuteChildNode(child.taskNode, cancellationToken);
            }
            else
            {
                foreach (var child in children)
                    if (child.enabled)
                        ExecuteChildNode(child.taskNode, cancellationToken).Forget(Debug.LogError);
                await UniTask.WaitUntil(IsAllChildNodesCompleted, cancellationToken: cancellationToken);
            }
        }

        protected override UniTask FinishTheTask(CancellationToken cancellationToken)
        {
            if (IsAllChildNodesCompleted()) return UniTask.CompletedTask;
            return UniTask.WaitUntil(IsAllChildNodesCompleted, cancellationToken: cancellationToken);
        }

        private bool IsAllChildNodesCompleted()
        {
            foreach (var child in children)
            {
                if (!child.enabled) continue;
                var status = child.taskNode.Status;
                if (status != TaskNodeStatus.Completed && status != TaskNodeStatus.Failed) return false;
            }

            return true;
        }
        
        private UniTask ExecuteChildNode(ATaskNode childTaskNode, CancellationToken cancellationToken)
        {
            childTaskNode.ProgressChanged += OnChildProgressChanged;
            childTaskNode.Completed += OnChildCompleted;
            return childTaskNode.ExecuteAsync(cancellationToken);
        }

        private void OnChildProgressChanged(ATaskNode childTaskNode, float delta)
        {
            var sum = GetSubTaskValueSum();
            if (sum <= 0f) return;
            var child = children.Find(c => c.taskNode == childTaskNode);
            Progress += delta * child.subTaskValue / sum;
        }
        
        private float GetSubTaskValueSum()
        {
            var result = 0f;
            foreach (var child in children) if (child.enabled) result += child.subTaskValue;
            return result;
        }

        private void OnChildCompleted(ATaskNode childTaskNode)
        {
            childTaskNode.ProgressChanged -= OnChildProgressChanged;
            childTaskNode.Completed -= OnChildCompleted;
        }

        public void InsertChild(int index, Child child)
        {
            children.Insert(index, child);
            if (Status != TaskNodeStatus.Running) return;

            var subTaskValueSum = GetSubTaskValueSum();
            var oldSum = subTaskValueSum - child.subTaskValue;
            if (subTaskValueSum > 0f && oldSum > 0f)
            {
                Progress = Progress * oldSum / subTaskValueSum;
            }

            if (executionMode == ExecutionMode.Parallel && taskFinishCts != null)
                ExecuteChildNode(child.taskNode, taskFinishCts.Token).Forget();
        }
    }
}