using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS
{
    /// <summary>
    /// Interface for entity matchers that can exclude entities with specific components.
    /// This is the first stage in the fluent interface for building entity queries.
    /// </summary>
    public interface INoneOfEntityMatcher : IEntityMatcher
    {
        /// <summary>
        /// Excludes entities that have the specified component type.
        /// </summary>
        /// <typeparam name="T">Component type to exclude, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>This matcher instance for method chaining</returns>
        public INoneOfEntityMatcher OfNone<T>() where T : struct, IComponent<T>;
    }

    /// <summary>
    /// Interface for entity matchers that can include entities with any of the specified components.
    /// This is the second stage in the fluent interface for building entity queries.
    /// </summary>
    public interface IAnyOfEntityMatcher : INoneOfEntityMatcher
    {
        /// <summary>
        /// Includes entities that have at least one of the specified component types.
        /// </summary>
        /// <typeparam name="T">Component type to include, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>This matcher instance for method chaining</returns>
        public IAnyOfEntityMatcher OfAny<T>() where T : struct, IComponent<T>;
    }
    
    /// <summary>
    /// Interface for entity matchers that can require entities to have all specified components.
    /// This is the final stage in the fluent interface for building entity queries.
    /// </summary>
    public interface IAllOfEntityMatcher : IAnyOfEntityMatcher
    {
        /// <summary>
        /// Requires entities to have all of the specified component types.
        /// </summary>
        /// <typeparam name="T">Component type to require, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>This matcher instance for method chaining</returns>
        public IAllOfEntityMatcher OfAll<T>() where T : struct, IComponent<T>;
    }
    
    /// <summary>
    /// Implementation of the entity matcher that allows building complex queries for entities
    /// based on their component composition. Uses a fluent interface to specify inclusion,
    /// exclusion, and requirement criteria.
    /// </summary>
    public class EntityMatcher : IAllOfEntityMatcher
    {

        #region Config
        
        /// <summary>
        /// Excludes entities that have the specified component type.
        /// </summary>
        /// <typeparam name="T">Component type to exclude, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>This matcher instance for method chaining</returns>
        public INoneOfEntityMatcher OfNone<T>() where T : struct, IComponent<T>
        {
            var type = typeof(T);
            m_none.Add(type);
            m_noneResolved.Add(type);
            return this;
        }

        /// <summary>
        /// Includes entities that have at least one of the specified component types.
        /// </summary>
        /// <typeparam name="T">Component type to include, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>This matcher instance for method chaining</returns>
        public IAnyOfEntityMatcher OfAny<T>() where T : struct, IComponent<T>
        {
            var type = typeof(T);
            m_any.Add(type);
            m_anyResolved.Add(type);
            return this;
        }

        /// <summary>
        /// Requires entities to have all of the specified component types.
        /// </summary>
        /// <typeparam name="T">Component type to require, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>This matcher instance for method chaining</returns>
        public IAllOfEntityMatcher OfAll<T>() where T : struct, IComponent<T>
        {
            var type = typeof(T);
            m_all.Add(type);
            m_allResolved.Add(type);
            return this;
        }

        /// <summary>
        /// Private constructor for creating an entity matcher with a specific mask.
        /// </summary>
        /// <param name="mask">The entity mask to use for filtering</param>
        private EntityMatcher(ulong mask)
        {
            m_mask = mask;
        }

        /// <summary>
        /// Creates a new entity matcher that matches all entities.
        /// </summary>
        public static EntityMatcher With => new(ulong.MaxValue);

        /// <summary>
        /// Creates a new entity matcher with the specified entity mask.
        /// </summary>
        /// <param name="mask">The entity mask to use for filtering</param>
        /// <returns>A new entity matcher with the specified mask</returns>
        public static EntityMatcher WithMask(ulong mask) => new(mask);
        
        #endregion
        
        /// <summary>
        /// The entity mask used for initial filtering.
        /// </summary>
        private readonly ulong m_mask;
        
        /// <summary>
        /// Set of component types that entities must have all of.
        /// </summary>
        private readonly HashSet<Type> m_all = new();
        
        /// <summary>
        /// Set of component types that entities must have at least one of.
        /// </summary>
        private readonly HashSet<Type> m_any = new();
        
        /// <summary>
        /// Set of component types that entities must not have.
        /// </summary>
        private readonly HashSet<Type> m_none = new();

        /// <summary>All-of conditions resolved to per-kind type ids at configuration time.</summary>
        private readonly ResolvedSet m_allResolved = new();

        /// <summary>Any-of conditions resolved to per-kind type ids at configuration time.</summary>
        private readonly ResolvedSet m_anyResolved = new();

        /// <summary>None-of conditions resolved to per-kind type ids at configuration time.</summary>
        private readonly ResolvedSet m_noneResolved = new();

        /// <summary>
        /// Number of times <see cref="EvaluateStructure"/> has run. Internal test hook used
        /// to prove the collector evaluates a structure once and reuses the result across
        /// its rows; not part of the public API.
        /// </summary>
        internal int StructureEvaluationCount { get; private set; }

        /// <summary>
        /// Evaluates this matcher against a structure row without materializing component
        /// references. Dense conditions resolve at structure level; tag and discrete
        /// conditions resolve at row level; the entity mask must intersect the structure mask.
        /// </summary>
        /// <param name="structure">Structure owning the row.</param>
        /// <param name="row">Live row inside the structure.</param>
        /// <returns>True when the mask, all, none and any criteria are satisfied.</returns>
        public bool ComponentFilter(Structure structure, int row)
        {
            var match = EvaluateStructure(structure);
            return match.Passes && RowFilter(structure, row, match.AnySatisfied);
        }

        /// <summary>
        /// Structure-level part of the matcher: mask intersection, dense none/all conditions
        /// and the dense any conditions. A structure's dense composition and mask never
        /// change, so the collector implementation caches this result per structure.
        /// </summary>
        /// <param name="structure">Structure to evaluate.</param>
        /// <returns>
        /// A result whose <see cref="StructureMatch.Passes"/> is false when no row can match
        /// and whose <see cref="StructureMatch.AnySatisfied"/> tells whether the any-condition
        /// already holds for every row through the dense composition.
        /// </returns>
        internal StructureMatch EvaluateStructure(Structure structure)
        {
            StructureEvaluationCount += 1;

            if ((EntityMask & structure.Mask) == 0UL) return default;
            if (HasAnyDense(structure, m_noneResolved)) return default;
            if (!HasAllDense(structure, m_allResolved)) return default;

            var anySatisfied = m_anyResolved.IsEmpty || HasAnyDense(structure, m_anyResolved);

            // The any-condition is the only disjunction: when the dense composition cannot
            // satisfy it and there are no row-level any conditions, no row can match.
            if (!anySatisfied && m_anyResolved.Tags.Count == 0 && m_anyResolved.Discretes.Count == 0)
                return default;

            return new StructureMatch(true, anySatisfied);
        }

        /// <summary>
        /// Row-level part of the matcher: tag and discrete none/all conditions plus the
        /// row-level any conditions when the dense any did not already satisfy the matcher.
        /// Only call after <see cref="EvaluateStructure"/> passed.
        /// </summary>
        /// <param name="structure">Structure owning the row.</param>
        /// <param name="row">Live row inside the structure.</param>
        /// <param name="anySatisfied">True when the any-condition already holds for every row.</param>
        /// <returns>True when the row-level criteria are satisfied.</returns>
        internal bool RowFilter(Structure structure, int row, bool anySatisfied)
        {
            if (HasAnyRow(structure, row, m_noneResolved)) return false;
            if (!HasAllRow(structure, row, m_allResolved)) return false;

            return anySatisfied || HasAnyRow(structure, row, m_anyResolved);
        }

        /// <summary>True when every dense condition in the set is present at the structure.</summary>
        private static bool HasAllDense(Structure structure, ResolvedSet set)
        {
            for (var i = 0; i < set.Dense.Count; i++)
            {
                if (!structure.HasDense(set.Dense[i])) return false;
            }

            return true;
        }

        /// <summary>True when at least one dense condition in the set is present at the structure.</summary>
        private static bool HasAnyDense(Structure structure, ResolvedSet set)
        {
            for (var i = 0; i < set.Dense.Count; i++)
            {
                if (structure.HasDense(set.Dense[i])) return true;
            }

            return false;
        }

        /// <summary>True when every tag/discrete condition in the set is present at the row.</summary>
        private static bool HasAllRow(Structure structure, int row, ResolvedSet set)
        {
            for (var i = 0; i < set.Tags.Count; i++)
            {
                if (!structure.HasTag(set.Tags[i], row)) return false;
            }

            for (var i = 0; i < set.Discretes.Count; i++)
            {
                if (!structure.HasDiscrete(set.Discretes[i], row)) return false;
            }

            return true;
        }

        /// <summary>True when at least one tag/discrete condition in the set is present at the row.</summary>
        private static bool HasAnyRow(Structure structure, int row, ResolvedSet set)
        {
            for (var i = 0; i < set.Tags.Count; i++)
            {
                if (structure.HasTag(set.Tags[i], row)) return true;
            }

            for (var i = 0; i < set.Discretes.Count; i++)
            {
                if (structure.HasDiscrete(set.Discretes[i], row)) return true;
            }

            return false;
        }

        /// <summary>
        /// Cached result of <see cref="EvaluateStructure"/>: <see cref="Passes"/> is false when
        /// no row of the structure can match; <see cref="AnySatisfied"/> is true when the
        /// any-condition already holds for every row through the dense composition.
        /// </summary>
        internal readonly struct StructureMatch
        {
            /// <summary>True when the structure-level conditions are satisfied.</summary>
            public readonly bool Passes;

            /// <summary>True when the any-condition needs no row-level evaluation.</summary>
            public readonly bool AnySatisfied;

            /// <summary>Creates a structure-level match result.</summary>
            /// <param name="passes">Whether the structure-level conditions are satisfied.</param>
            /// <param name="anySatisfied">Whether the any-condition holds for every row.</param>
            public StructureMatch(bool passes, bool anySatisfied)
            {
                Passes = passes;
                AnySatisfied = anySatisfied;
            }
        }

        /// <summary>
        /// Matcher conditions resolved once at configuration time into per-kind type id
        /// lists, so structure evaluation never inspects <see cref="Type"/> or the registry.
        /// </summary>
        private sealed class ResolvedSet
        {
            public readonly List<uint> Dense = new();
            public readonly List<uint> Tags = new();
            public readonly List<uint> Discretes = new();

            public bool IsEmpty => Dense.Count == 0 && Tags.Count == 0 && Discretes.Count == 0;

            /// <summary>Resolves the component type and appends its id to the kind bucket.</summary>
            public void Add(Type type)
            {
                var info = ComponentTypeRegistry.GetOrRegister(type);
                switch (info.Kind)
                {
                    case ComponentKind.Dense:
                        Dense.Add(info.TypeId);
                        break;
                    case ComponentKind.Discrete:
                        Discretes.Add(info.TypeId);
                        break;
                    case ComponentKind.Tag:
                        Tags.Add(info.TypeId);
                        break;
                }
            }
        }

        /// <summary>
        /// Determines whether the specified component type is relevant
        /// to this matcher's criteria (all, any, or none sets).
        /// </summary>
        /// <param name="componentType">The component type to check</param>
        /// <returns>True if the component appears in any matcher set</returns>
        public bool IsRelevantComponent(Type componentType)
        {
            if (m_all.Count == 0 && m_any.Count == 0 && m_none.Count == 0)
                return true;
            return m_all.Contains(componentType) || m_any.Contains(componentType) || m_none.Contains(componentType);
        }

        /// <summary>
        /// Gets the entity mask for this matcher.
        /// </summary>
        public ulong EntityMask => m_mask;
    }
}