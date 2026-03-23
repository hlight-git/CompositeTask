using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [Serializable]
    public class TaskTree
    {
        public CompositeTaskNode root = new()
        {
            name = "Root",
            executionMode = ExecutionMode.Sequential,
            children = new List<CompositeTaskNode.Child>(),
        };

        public void Accept(IDependencyInjectionVisitor dependencyInjectionVisitor)
        {
            dependencyInjectionVisitor.Visit(root);
        }

        public CancellationTokenSource Execute()
        {
            var cancellationTokenSource = new CancellationTokenSource();
            root.ExecuteAsync(cancellationTokenSource.Token).Forget();
            return cancellationTokenSource;
        }
    }
}
