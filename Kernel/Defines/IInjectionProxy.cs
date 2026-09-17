using System;
using Microsoft.Extensions.DependencyInjection;

namespace CoreECS.Defines
{
    /// <summary>
    /// Provides an immutable service provider and object activation for the world DI graph.
    /// Does not activate <see cref="ISystem"/> types; use framework system activation instead.
    /// </summary>
    public interface IInjectionProxy
    {
        /// <summary>
        /// Root service provider built from the world service collection; immutable after creation.
        /// </summary>
        IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// Creates a fully constructed instance with constructor dependencies resolved from <see cref="ServiceProvider"/>.
        /// </summary>
        object CreateObject(Type objectType);

        /// <summary>
        /// Creates a fully constructed instance with constructor dependencies resolved from <see cref="ServiceProvider"/>.
        /// </summary>
        T CreateObject<T>();
    }
}
