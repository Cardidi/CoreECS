using System.Collections.Generic;
using System.Linq;
using CoreECS.Defines;
using CoreECS.Utils;

namespace CoreECS
{
    /// <summary>
    /// Represents the current state of an entity, including its components and metadata.
    /// This class manages the component references for an entity and tracks its state.
    /// Note: Do not cache instances of this class in production as they are pooled and reused.
    /// </summary>
    public sealed class EntityGraph 
    {
        /// <summary>
        /// Object pool for EntityGraph instances to reduce memory allocations.
        /// </summary>
        public static readonly Pool<EntityGraph> Pool = new(
            createFunc: () => new EntityGraph(),
            returnAction: x => x.Reset());

        /// <summary>
        /// Gets or sets the unique identifier for the entity this graph represents.
        /// </summary>
        public ulong EntityId { get; set; }

        /// <summary>
        /// Gets or sets the component mask for this entity.
        /// The mask is a bitmask that represents which component types this entity has.
        /// </summary>
        public ulong Mask { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether this entity is marked for destruction.
        /// </summary>
        public bool WishDestroy { get; set; }
        
        /// <summary>
        /// Gets the list of component references for this entity.
        /// This list contains the core references to all components attached to the entity.
        /// </summary>
        public List<IComponentRefCore> RwComponents { get; } = new();

        /// <summary>
        /// Gets a reference to a component of type TComp attached to this entity.
        /// </summary>
        /// <typeparam name="TComp">Component type to retrieve, must be a struct implementing IComponent&lt;TComp&gt;</typeparam>
        /// <returns>A typed reference to the component, or default if not found</returns>
        public ComponentRef<TComp> GetComponent<TComp>() where TComp : struct, IComponent<TComp>
        {
            for (var i = 0; i < RwComponents.Count; i++)
            {
                var r = RwComponents[i];
                var loc = r.RefLocator;
                if (loc.IsT(typeof(TComp))) return new ComponentRef<TComp>(r);
            }

            return default;
        }

        /// <summary>
        /// Gets all components attached to this entity.
        /// </summary>
        /// <returns>An array of typeless component references</returns>
        public ComponentRef[] GetComponents()
        {
            return RwComponents.Select(x => new ComponentRef(x)).ToArray();
        }

        /// <summary>
        /// Gets all components attached to this entity and adds them to the specified collection.
        /// </summary>
        /// <param name="results">Collection to add component references to</param>
        /// <returns>The number of components added to the collection</returns>
        public int GetComponents(ICollection<ComponentRef> results)
        {
            var l = RwComponents.Count;
            for (var i = 0; i < RwComponents.Count; i++) results.Add(new ComponentRef(RwComponents[i]));

            return l;
        }

        /// <summary>
        /// Gets all components of type TComp attached to this entity.
        /// </summary>
        /// <typeparam name="TComp">Component type to retrieve, must be a struct implementing IComponent&lt;TComp&gt;</typeparam>
        /// <returns>An array of typed component references</returns>
        public ComponentRef<TComp>[] GetComponents<TComp>() where TComp : struct, IComponent<TComp>
        {
            using (ListPool<ComponentRef<TComp>>.Get(out var builder))
            {
                for (var i = 0; i < RwComponents.Count; i++)
                {
                    var r = RwComponents[i];
                    var loc = r.RefLocator;
                    if (loc.IsT(typeof(TComp))) builder.Add(new ComponentRef<TComp>(r));
                }

                return builder.ToArray();
            }
        }

        /// <summary>
        /// Gets all components of type TComp attached to this entity and adds them to the specified collection.
        /// </summary>
        /// <typeparam name="TComp">Component type to retrieve, must be a struct implementing IComponent&lt;TComp&gt;</typeparam>
        /// <param name="results">Collection to add component references to</param>
        /// <returns>The number of components added to the collection</returns>
        public int GetComponents<TComp>(ICollection<ComponentRef<TComp>> results)
            where TComp : struct, IComponent<TComp>
        {
            var collected = 0;
            for (var i = 0; i < RwComponents.Count; i++)
            {
                var r = RwComponents[i];
                var loc = r.RefLocator;
                if (loc.IsT(typeof(TComp)))
                {
                    collected += 1;
                    results.Add(new ComponentRef<TComp>(r));
                }
            }

            return collected;
        }

        /// <summary>
        /// Checks if this entity has a component of type TComp.
        /// </summary>
        /// <typeparam name="TComp">Component type to check for, must be a struct implementing IComponent&lt;TComp&gt;</typeparam>
        /// <returns>True if the entity has the component, false otherwise</returns>
        public bool HasComponent<TComp>() where TComp : struct, IComponent<TComp>
        {
            for (var i = 0; i < RwComponents.Count; i++)
            {
                var r = RwComponents[i];
                var loc = r.RefLocator;
                if (loc.IsT(typeof(TComp))) return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets the count of components of type TComp attached to this entity.
        /// </summary>
        /// <typeparam name="TComp">Component type to count, must be a struct implementing IComponent&lt;TComp&gt;</typeparam>
        /// <returns>The number of components of type TComp attached to this entity</returns>
        public int GetComponentCount<TComp>() where TComp : struct, IComponent<TComp>
        {
            var collected = 0;
            for (var i = 0; i < RwComponents.Count; i++)
            {
                var r = RwComponents[i];
                var loc = r.RefLocator;
                if (loc.IsT(typeof(TComp))) collected += 1;
            }
            
            return collected;
        }

        /// <summary>
        /// Resets this EntityGraph instance to its default state.
        /// This method is called when the instance is returned to the pool.
        /// </summary>
        private void Reset()
        {
            EntityId = 0;
            Mask = 0;
            WishDestroy = false;
            RwComponents.Clear();
        }
        
        /// <summary>
        /// Private constructor to prevent direct instantiation.
        /// Use the Pool property to get instances.
        /// </summary>
        private EntityGraph() {}
    }
}