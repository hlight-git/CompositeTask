using System.Collections.Generic;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>Sugar over <see cref="IDependencyContext.TryResolve{T}"/>.</summary>
    public static class DependencyContextExtensions
    {
        /// <summary>
        /// Resolve a required dependency. Throws <see cref="KeyNotFoundException"/> if the
        /// context cannot provide it — use <see cref="IDependencyContext.TryResolve{T}"/>
        /// for optional deps instead.
        /// </summary>
        public static T Resolve<T>(this IDependencyContext context, string id = null) where T : class
        {
            if (context.TryResolve<T>(out var value, id)) return value;
            throw new KeyNotFoundException(
                $"No provider registered for '{typeof(T).FullName}'" +
                (string.IsNullOrEmpty(id) ? "." : $" with id '{id}'."));
        }
    }
}
