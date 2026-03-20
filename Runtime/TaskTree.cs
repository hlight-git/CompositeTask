using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    public class TaskTree : MonoBehaviour
    {
        [field: SerializeField] public CompositeTaskNode Root { get; set; }

        public void Accept(IDependencyInjectionVisitor dependencyInjectionVisitor)
        {
            dependencyInjectionVisitor.Visit(Root);
        }

        public CancellationTokenSource Execute()
        {
            var cancellationTokenSource = new CancellationTokenSource();
            Root.ExecuteAsync(cancellationTokenSource.Token).Forget();
            return cancellationTokenSource;
        }
    }
}