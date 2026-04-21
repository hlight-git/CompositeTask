namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Minimal resolver contract consumed by <see cref="TaskNode.ResolveDependencies"/>.
    /// Shape mirrors typical pull-model DI packages — implement on your project's context
    /// (or write a thin adapter) to hand dependencies to tasks during tree warm-up.
    /// </summary>
    public interface IDependencyContext
    {
        bool TryResolve<T>(out T value, string id = null) where T : class;
    }
}
