using System;
using Microsoft.Extensions.DependencyInjection;

namespace CoreECS.Defines
{
    /// <summary>
    /// Creates service collections and builds <see cref="IInjectionProxy"/> instances for a world.
    /// </summary>
    public interface IInjectionProxyFactory
    {
        /// <summary>
        /// Creates a new service collection for this world.
        /// </summary>
        IServiceCollection CreateServiceCollection();

        /// <summary>
        /// Builds an immutable <see cref="IInjectionProxy"/> from the configured collection.
        /// </summary>
        /// <param name="collection">The configured service collection</param>
        IInjectionProxy CreateProxy(IServiceCollection collection);
    }
}
