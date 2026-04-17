namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Visitor interface for opt-in dependency injection.
    /// TaskNode subclasses override Accept() and call v.Visit(this) to receive dependencies.
    /// </summary>
    public interface IDependencyInjectionVisitor
    {
        void Visit<T>(T target);
    }
}
