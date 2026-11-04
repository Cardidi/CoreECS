using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS
{
    internal enum EntityMatcherCandidateStrategy
    {
        LiveEntityTable,
        SmallestAllStorage,
        AnyStorageUnion,
    }

    internal sealed class EntityMatcherPlan
    {
        private readonly uint[] m_allTypeIds;
        private readonly uint[] m_anyTypeIds;
        private readonly uint[] m_noneTypeIds;

        internal ulong EntityMask { get; }
        internal EntityMatcherCandidateStrategy CandidateStrategy { get; }
        internal ReadOnlySpan<uint> AllTypeIds => m_allTypeIds;
        internal ReadOnlySpan<uint> AnyTypeIds => m_anyTypeIds;
        internal ReadOnlySpan<uint> NoneTypeIds => m_noneTypeIds;

        internal EntityMatcherPlan(ulong entityMask, uint[] allTypeIds, uint[] anyTypeIds,
            uint[] noneTypeIds)
        {
            EntityMask = entityMask;
            m_allTypeIds = allTypeIds;
            m_anyTypeIds = anyTypeIds;
            m_noneTypeIds = noneTypeIds;
            CandidateStrategy = allTypeIds.Length > 0
                ? EntityMatcherCandidateStrategy.SmallestAllStorage
                : anyTypeIds.Length > 0
                    ? EntityMatcherCandidateStrategy.AnyStorageUnion
                    : EntityMatcherCandidateStrategy.LiveEntityTable;
        }

        internal bool IsRelevantTypeId(uint typeId)
        {
            if (m_allTypeIds.Length == 0 && m_anyTypeIds.Length == 0 && m_noneTypeIds.Length == 0)
                return true;

            return Contains(m_allTypeIds, typeId) || Contains(m_anyTypeIds, typeId) ||
                   Contains(m_noneTypeIds, typeId);
        }

        internal bool Matches(ComponentHandler? componentHead)
        {
            bool anyConditionMet = m_anyTypeIds.Length == 0;
            int allConditionCount = 0;

            ComponentHandler? component = componentHead;
            while (component != null)
            {
                uint typeId = component.TypeId;
                if (Contains(m_noneTypeIds, typeId)) return false;
                if (!anyConditionMet && Contains(m_anyTypeIds, typeId)) anyConditionMet = true;
                if (Contains(m_allTypeIds, typeId)) allConditionCount += 1;
                component = component.Next;
            }

            return anyConditionMet && allConditionCount == m_allTypeIds.Length;
        }

        private static bool Contains(uint[] typeIds, uint typeId)
        {
            return Array.BinarySearch(typeIds, typeId) >= 0;
        }
    }

    /// <summary>
    /// Builds component and mask conditions for entity queries and collectors.
    /// A matcher becomes immutable when it is first bound to either consumer.
    /// </summary>
    public sealed class EntityMatcher
    {
        private readonly ulong m_mask;
        private readonly HashSet<Type> m_all = new HashSet<Type>();
        private readonly HashSet<Type> m_any = new HashSet<Type>();
        private readonly HashSet<Type> m_none = new HashSet<Type>();
        private EntityMatcherPlan m_plan = null!;

        private EntityMatcher(ulong mask)
        {
            m_mask = mask;
        }

        /// <summary>
        /// Creates a matcher with no component conditions and a mask accepting every non-zero bit.
        /// </summary>
        public static EntityMatcher With => new EntityMatcher(ulong.MaxValue);

        /// <summary>
        /// Creates a matcher whose entity mask must share at least one bit with <paramref name="mask"/>.
        /// </summary>
        public static EntityMatcher WithMask(ulong mask) => new EntityMatcher(mask);

        /// <summary>
        /// Requires entities to have the specified component type.
        /// </summary>
        public EntityMatcher OfAll<T>() where T : struct, IComponent<T>
        {
            ThrowIfFrozen();
            m_all.Add(typeof(T));
            return this;
        }

        /// <summary>
        /// Requires entities to have at least one of the specified Any component types.
        /// </summary>
        public EntityMatcher OfAny<T>() where T : struct, IComponent<T>
        {
            ThrowIfFrozen();
            m_any.Add(typeof(T));
            return this;
        }

        /// <summary>
        /// Excludes entities that have the specified component type.
        /// </summary>
        public EntityMatcher OfNone<T>() where T : struct, IComponent<T>
        {
            ThrowIfFrozen();
            m_none.Add(typeof(T));
            return this;
        }

        /// <summary>
        /// Gets the immutable entity mask used by this matcher.
        /// </summary>
        public ulong EntityMask => m_mask;

        /// <summary>
        /// Determines whether a component type participates in this matcher's conditions.
        /// Every component type is relevant to an empty matcher.
        /// </summary>
        public bool IsRelevantComponent(Type componentType)
        {
            if (componentType == null) return false;

            if (m_all.Count == 0 && m_any.Count == 0 && m_none.Count == 0) return true;
            return m_all.Contains(componentType) || m_any.Contains(componentType) ||
                   m_none.Contains(componentType);
        }

        internal EntityMatcherPlan Bind()
        {
            if (m_plan != null) return m_plan;

            var plan = new EntityMatcherPlan(m_mask, ResolveTypeIds(m_all), ResolveTypeIds(m_any),
                ResolveTypeIds(m_none));
            m_plan = plan;
            return plan;
        }

        private static uint[] ResolveTypeIds(HashSet<Type> componentTypes)
        {
            if (componentTypes.Count == 0) return Array.Empty<uint>();

            var typeIds = new uint[componentTypes.Count];
            int index = 0;
            foreach (Type componentType in componentTypes)
            {
                typeIds[index++] = ComponentTypeRegistry.Register(componentType).TypeId;
            }

            Array.Sort(typeIds);
            return typeIds;
        }

        private void ThrowIfFrozen()
        {
            if (m_plan != null)
                throw new InvalidOperationException("An EntityMatcher cannot be modified after it is bound.");
        }
    }
}
