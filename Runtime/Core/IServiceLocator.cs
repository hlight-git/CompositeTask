namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Pull-model resolver consumed by <see cref="TaskNode.ResolveFrom"/>. Hands
    /// out per-type providers; consumers call <see cref="IProvider{T}.TryProvide"/> at the
    /// actual use-site so IDE "Find Usages" on <c>TryProvide</c> lists every real
    /// consumer of the type.
    /// </summary>
    /// <remarks>
    /// No <c>Resolve</c>/<c>TryResolve</c> wrapper ships intentionally — a generic wrapper
    /// hides <c>TryProvide</c> from IDE Find Usages behind one call site.
    /// <para>
    /// Shape identical to <c>Apero.Unity.DesignPattern.DependencyInversion.ServiceLocator.IServiceLocator</c>:
    /// <see cref="GetProvider{T}"/> takes an <paramref name="dependencyConsumer"/> so the
    /// context can veto per consumer (return <c>null</c>). An Apero scope satisfies this
    /// interface directly (or via a single-line adapter) — this submodule stays
    /// independent of any specific DI package.
    /// </para>
    /// </remarks>
    public interface IServiceLocator
    {
        /// <param name="dependencyConsumer">
        /// The task node (or any caller) asking for the dependency. Contexts MAY inspect
        /// this to apply per-consumer routing / veto. Task nodes pass <c>this</c>.
        /// </param>
        IProvider<T> GetProvider<T>(object dependencyConsumer) where T : class;

        /// <summary>
        /// Per-type provider facet. <paramref name="instanceKey"/> disambiguates multiple
        /// instances of the same type (e.g. <c>worldCamera</c> vs <c>uiCamera</c>); pass
        /// <c>null</c> when the provider owns a single instance.
        /// </summary>
        public interface IProvider<T> where T : class
        {
            bool TryProvide(out T value, string instanceKey = null);
        }
    }
}
