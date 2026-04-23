namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Pull-model resolver consumed by <see cref="TaskNode.ResolveDependencies"/>. Hands
    /// out per-type providers; consumers call <see cref="IProvider{T}.TryProvide"/> at the
    /// actual use-site so IDE "Find Usages" on <c>TryProvide</c> lists every real
    /// consumer of the type.
    /// </summary>
    /// <remarks>
    /// No <c>Resolve</c>/<c>TryResolve</c> wrapper ships intentionally — a generic wrapper
    /// hides <c>TryProvide</c> from IDE Find Usages behind one call site.
    /// <para>
    /// Shape mirrors the <c>Apero.Unity.DesignPattern.DependencyInversion.ServiceLocator.IServiceLocator</c>
    /// contract so an Apero scope adapts to this interface with a trivial wrapper: forward
    /// <see cref="GetProvider{T}"/> to <c>scope.Locator.GetProvider&lt;T&gt;(consumer)</c> and
    /// adapt the returned provider. This submodule stays independent — no cross-ref needed.
    /// </para>
    /// </remarks>
    public interface IDependencyContext
    {
        IProvider<T> GetProvider<T>() where T : class;

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
