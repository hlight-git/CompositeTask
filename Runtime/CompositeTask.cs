using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [Serializable]
    public class CompositeTask : ATask
    {
        [Serializable]
        public class Child
        {
            public bool enabled = true;
            [Min(0)]
            public float subTaskValue;
            [SerializeReference] public ATask task;
        }

        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        [SerializeField] public ExecutionMode executionMode;
        [SerializeField] public List<Child> children;

        public override void Accept(IDependencyInjectionVisitor dependencyInjectionVisitor)
        {
            base.Accept(dependencyInjectionVisitor);
            if (children == null) return;
            foreach (var child in children)
                child?.task?.Accept(dependencyInjectionVisitor);
        }

        public override void Awake()
        {
            base.Awake();
            if (children == null) return;
            foreach (var child in children)
                if (child is { enabled: true })
                    child.task?.Awake();
        }

        protected override async UniTask OnRunning(CancellationToken cancellationToken)
        {
            if (executionMode == ExecutionMode.Sequential)
            {
                foreach (var child in children)
                    if (child.enabled)
                        await ExecuteChildTask(child.task, cancellationToken);
            }
            else
            {
                foreach (var child in children)
                    if (child.enabled)
                        ExecuteChildTask(child.task, cancellationToken).Forget(Debug.LogError);
                await UniTask.WaitUntil(IsAllChildTasksCompleted, cancellationToken: cancellationToken);
            }
        }

        protected override UniTask OnFinishing(CancellationToken cancellationToken)
        {
            if (IsAllChildTasksCompleted()) return UniTask.CompletedTask;
            return UniTask.WaitUntil(IsAllChildTasksCompleted, cancellationToken: cancellationToken);
        }

        private bool IsAllChildTasksCompleted()
        {
            if (children == null) return true;
            foreach (var child in children)
            {
                if (!child.enabled) continue;
                var status = child.task.Status;
                if (status != TaskStatus.Completed && status != TaskStatus.Failed) return false;
            }
            return true;
        }

        private UniTask ExecuteChildTask(ATask childTask, CancellationToken cancellationToken)
        {
            childTask.ProgressChanged += OnChildProgressChanged;
            childTask.Completed += OnChildCompleted;
            return childTask.ExecuteAsync(cancellationToken);
        }

        private void OnChildProgressChanged(ATask childTask, float delta)
        {
            var sum = GetSubTaskValueSum();
            if (sum <= 0f) return;
            var child = children.Find(c => c.task == childTask);
            Progress += delta * child.subTaskValue / sum;
        }

        private float GetSubTaskValueSum()
        {
            var result = 0f;
            foreach (var child in children) if (child.enabled) result += child.subTaskValue;
            return result;
        }

        private void OnChildCompleted(ATask childTask)
        {
            childTask.ProgressChanged -= OnChildProgressChanged;
            childTask.Completed -= OnChildCompleted;
        }

        public void InsertChild(int index, Child child)
        {
            children.Insert(index, child);
            if (Status != TaskStatus.Running) return;

            var subTaskValueSum = GetSubTaskValueSum();
            var oldSum = subTaskValueSum - child.subTaskValue;
            if (subTaskValueSum > 0f && oldSum > 0f)
            {
                Progress = Progress * oldSum / subTaskValueSum;
            }

            if (executionMode == ExecutionMode.Parallel && taskFinishCts != null)
                ExecuteChildTask(child.task, taskFinishCts.Token).Forget();
        }
    }
}
