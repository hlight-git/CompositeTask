using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [Serializable]
    public class TaskTree : IDisposable
    {
        public CompositeTask root = new()
        {
            name = "Root",
            executionMode = ExecutionMode.Sequential,
            children = new List<CompositeTask.Child>(),
        };

        public void Accept(IDependencyInjectionVisitor dependencyInjectionVisitor)
        {
            root.Accept(dependencyInjectionVisitor);
        }

        public CancellationTokenSource Execute()
        {
            var cancellationTokenSource = new CancellationTokenSource();
            Execute(cancellationTokenSource.Token);
            return cancellationTokenSource;
        }
        
        public void Execute(CancellationToken cancellationToken)
        {
            root.Awake();
            root.ExecuteAsync(cancellationToken).Forget();
        }

        public void Dispose()
        {
            root?.Dispose();
        }
    }
}
