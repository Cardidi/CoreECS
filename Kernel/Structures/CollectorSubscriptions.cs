using System;
using System.Collections.Generic;
using CoreECS.Defines;

#pragma warning disable CS0618

namespace CoreECS.Structures
{
    internal interface ICollectorSubscriptionTarget
    {
        ulong CreatedSequence { get; }

        void MarkPending(EntityId entityId, CollectorPendingReason reason);
    }

    internal sealed class CollectorSubscriptions
    {
        private readonly Dictionary<uint, List<ICollectorSubscriptionTarget>>
            m_structuralByType =
                new Dictionary<uint, List<ICollectorSubscriptionTarget>>();
        private readonly Dictionary<uint, List<ICollectorSubscriptionTarget>>
            m_revisionByType =
                new Dictionary<uint, List<ICollectorSubscriptionTarget>>();
        private readonly List<ICollectorSubscriptionTarget> m_wildcardStructural =
            new List<ICollectorSubscriptionTarget>();
        private readonly List<ICollectorSubscriptionTarget> m_wildcardRevision =
            new List<ICollectorSubscriptionTarget>();
        private readonly List<ICollectorSubscriptionTarget> m_lifecycleSensitive =
            new List<ICollectorSubscriptionTarget>();
        private readonly Dictionary<ICollectorSubscriptionTarget, Registration> m_registrations =
            new Dictionary<ICollectorSubscriptionTarget, Registration>();

        internal int RegisteredCountForTesting => m_registrations.Count;

        internal int SubscriptionLinkCountForTesting { get; private set; }

        internal int LastDeliveredCountForTesting { get; private set; }

        internal int LastUnrelatedCountForTesting { get; private set; }

        internal void Register(
            ICollectorSubscriptionTarget target,
            EntityMatcherPlan plan,
            EntityCollectorFlag flag)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (m_registrations.ContainsKey(target))
                throw new InvalidOperationException("Collector is already registered.");

            uint[] typeIds = CollectDistinctTypeIds(plan);
            bool relatedOnly = (flag & EntityCollectorFlag.RelatedComponentOnly) != 0;
            bool structuralWildcard = !relatedOnly || typeIds.Length == 0;
            if (structuralWildcard)
            {
                m_wildcardStructural.Add(target);
                SubscriptionLinkCountForTesting++;
            }
            else
            {
                for (int i = 0; i < typeIds.Length; i++)
                {
                    Add(m_structuralByType, typeIds[i], target);
                }
            }

            bool revisionWildcard = false;
            uint[] revisionTypeIds = Array.Empty<uint>();
            if ((flag & EntityCollectorFlag.RevisionAsChange) != 0)
            {
                if (!relatedOnly || typeIds.Length == 0)
                {
                    m_wildcardRevision.Add(target);
                    SubscriptionLinkCountForTesting++;
                    revisionWildcard = true;
                }
                else
                {
                    revisionTypeIds = typeIds;
                    for (int i = 0; i < revisionTypeIds.Length; i++)
                    {
                        Add(m_revisionByType, revisionTypeIds[i], target);
                    }
                }
            }

            bool lifecycleSensitive = plan.AllTypeIds.Length == 0 && plan.AnyTypeIds.Length == 0;
            if (lifecycleSensitive)
            {
                m_lifecycleSensitive.Add(target);
                SubscriptionLinkCountForTesting++;
            }

            m_registrations.Add(target,
                new Registration(structuralWildcard ? Array.Empty<uint>() : typeIds,
                    revisionTypeIds, CopyTypeIds(plan.AllTypeIds),
                    CopyTypeIds(plan.AnyTypeIds), CopyTypeIds(plan.NoneTypeIds),
                    structuralWildcard, revisionWildcard, lifecycleSensitive));
        }

        internal bool Unregister(ICollectorSubscriptionTarget target)
        {
            if (target == null || !m_registrations.TryGetValue(target, out Registration registration))
            {
                return false;
            }

            for (int i = 0; i < registration.StructuralTypeIds.Length; i++)
            {
                Remove(m_structuralByType, registration.StructuralTypeIds[i], target);
            }

            if (registration.StructuralWildcard)
            {
                m_wildcardStructural.Remove(target);
                SubscriptionLinkCountForTesting--;
            }

            for (int i = 0; i < registration.RevisionTypeIds.Length; i++)
            {
                Remove(m_revisionByType, registration.RevisionTypeIds[i], target);
            }

            if (registration.RevisionWildcard)
            {
                m_wildcardRevision.Remove(target);
                SubscriptionLinkCountForTesting--;
            }

            if (registration.LifecycleSensitive)
            {
                m_lifecycleSensitive.Remove(target);
                SubscriptionLinkCountForTesting--;
            }

            m_registrations.Remove(target);
            return true;
        }

        internal void DispatchStructural(uint typeId, EntityId entityId)
        {
            DispatchStructural(typeId, entityId, CollectorPendingReason.Structural);
        }

        internal void DispatchStructural(
            uint typeId,
            EntityId entityId,
            CollectorPendingReason reason)
        {
            DispatchStructural(typeId, entityId, reason, null);
        }

        internal void DispatchStructural(
            uint typeId,
            EntityId entityId,
            CollectorPendingReason reason,
            IReadOnlyCollection<IComponentRefCore> currentComponents)
        {
            int delivered = m_structuralByType.TryGetValue(typeId,
                out List<ICollectorSubscriptionTarget> targets)
                ? DispatchStructural(targets, typeId, entityId, reason, currentComponents)
                : 0;
            delivered += DispatchStructural(m_wildcardStructural, typeId, entityId, reason,
                currentComponents);
            SetDiagnostics(delivered);
        }

        internal void DispatchRevision(ComponentChangeJournal.Entry entry)
        {
            int delivered = Dispatch(m_revisionByType, entry.TypeId, entry.EntityId,
                CollectorPendingReason.Revision, entry.Sequence, true);
            delivered += Dispatch(m_wildcardRevision, entry.EntityId,
                CollectorPendingReason.Revision, entry.Sequence, true);
            SetDiagnostics(delivered);
        }

        internal void DispatchLifecycle(EntityId entityId)
        {
            int delivered = Dispatch(m_lifecycleSensitive, entityId,
                CollectorPendingReason.Lifecycle, 0, false);
            SetDiagnostics(delivered);
        }

        internal void Clear()
        {
            m_structuralByType.Clear();
            m_wildcardStructural.Clear();
            m_revisionByType.Clear();
            m_wildcardRevision.Clear();
            m_lifecycleSensitive.Clear();
            m_registrations.Clear();
            SubscriptionLinkCountForTesting = 0;
            SetDiagnostics(0);
        }

        private int Dispatch(
            Dictionary<uint, List<ICollectorSubscriptionTarget>> index,
            uint typeId,
            EntityId entityId,
            CollectorPendingReason reason,
            ulong sequence,
            bool applySequenceGate)
        {
            return index.TryGetValue(typeId, out List<ICollectorSubscriptionTarget> targets)
                ? Dispatch(targets, entityId, reason, sequence, applySequenceGate)
                : 0;
        }

        private static int Dispatch(
            List<ICollectorSubscriptionTarget> targets,
            EntityId entityId,
            CollectorPendingReason reason,
            ulong sequence,
            bool applySequenceGate)
        {
            int delivered = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                ICollectorSubscriptionTarget target = targets[i];
                if (applySequenceGate && sequence < target.CreatedSequence)
                {
                    continue;
                }

                target.MarkPending(entityId, reason);
                delivered++;
            }

            return delivered;
        }

        private int DispatchStructural(
            List<ICollectorSubscriptionTarget> targets,
            uint typeId,
            EntityId entityId,
            CollectorPendingReason reason,
            IReadOnlyCollection<IComponentRefCore> currentComponents)
        {
            int delivered = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                ICollectorSubscriptionTarget target = targets[i];
                Registration registration = m_registrations[target];
                CollectorPendingReason targetReason = reason;
                if (((reason & CollectorPendingReason.StructuralRemoved) != 0 &&
                     (Contains(registration.AllTypeIds, typeId) ||
                      (Contains(registration.AnyTypeIds, typeId) &&
                       !HasAnyCurrentType(registration.AnyTypeIds, currentComponents)))) ||
                    ((reason & CollectorPendingReason.StructuralAdded) != 0 &&
                     Contains(registration.NoneTypeIds, typeId)))
                {
                    targetReason |= CollectorPendingReason.StructuralClashed;
                }

                target.MarkPending(entityId, targetReason);
                delivered++;
            }

            return delivered;
        }

        private void SetDiagnostics(int delivered)
        {
            LastDeliveredCountForTesting = delivered;
            LastUnrelatedCountForTesting = m_registrations.Count - delivered;
        }

        private void Add(
            Dictionary<uint, List<ICollectorSubscriptionTarget>> index,
            uint typeId,
            ICollectorSubscriptionTarget target)
        {
            if (!index.TryGetValue(typeId, out List<ICollectorSubscriptionTarget> targets))
            {
                targets = new List<ICollectorSubscriptionTarget>();
                index.Add(typeId, targets);
            }

            targets.Add(target);
            SubscriptionLinkCountForTesting++;
        }

        private void Remove(
            Dictionary<uint, List<ICollectorSubscriptionTarget>> index,
            uint typeId,
            ICollectorSubscriptionTarget target)
        {
            if (!index.TryGetValue(typeId, out List<ICollectorSubscriptionTarget> targets) ||
                !targets.Remove(target))
            {
                return;
            }

            SubscriptionLinkCountForTesting--;
            if (targets.Count == 0)
            {
                index.Remove(typeId);
            }
        }

        private static uint[] CollectDistinctTypeIds(EntityMatcherPlan plan)
        {
            var distinct = new HashSet<uint>();
            AddRange(distinct, plan.AllTypeIds);
            AddRange(distinct, plan.AnyTypeIds);
            AddRange(distinct, plan.NoneTypeIds);
            if (distinct.Count == 0)
            {
                return Array.Empty<uint>();
            }

            var result = new uint[distinct.Count];
            distinct.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        private static uint[] CopyTypeIds(ReadOnlySpan<uint> source)
        {
            if (source.Length == 0) return Array.Empty<uint>();

            var result = new uint[source.Length];
            source.CopyTo(result);
            return result;
        }

        private static bool Contains(uint[] typeIds, uint typeId)
        {
            return Array.BinarySearch(typeIds, typeId) >= 0;
        }

        private static bool HasAnyCurrentType(
            uint[] anyTypeIds,
            IReadOnlyCollection<IComponentRefCore> currentComponents)
        {
            if (currentComponents == null) return false;

            foreach (IComponentRefCore component in currentComponents)
            {
                uint currentTypeId;
                if (component is IHandlerComponentRefCore handlerCore &&
                    handlerCore.Handler != null)
                {
                    currentTypeId = handlerCore.Handler.TypeId;
                }
                else
                {
                    Type componentType = component?.RefLocator?.GetT();
                    if (componentType == null) continue;
                    currentTypeId = ComponentTypeRegistry.Register(componentType).TypeId;
                }

                if (Contains(anyTypeIds, currentTypeId)) return true;
            }

            return false;
        }

        private static void AddRange(HashSet<uint> destination, ReadOnlySpan<uint> source)
        {
            for (int i = 0; i < source.Length; i++)
            {
                destination.Add(source[i]);
            }
        }

        private sealed class Registration
        {
            internal uint[] StructuralTypeIds { get; }
            internal uint[] RevisionTypeIds { get; }
            internal uint[] AllTypeIds { get; }
            internal uint[] AnyTypeIds { get; }
            internal uint[] NoneTypeIds { get; }
            internal bool StructuralWildcard { get; }
            internal bool RevisionWildcard { get; }
            internal bool LifecycleSensitive { get; }

            internal Registration(
                uint[] structuralTypeIds,
                uint[] revisionTypeIds,
                uint[] allTypeIds,
                uint[] anyTypeIds,
                uint[] noneTypeIds,
                bool structuralWildcard,
                bool revisionWildcard,
                bool lifecycleSensitive)
            {
                StructuralTypeIds = structuralTypeIds;
                RevisionTypeIds = revisionTypeIds;
                AllTypeIds = allTypeIds;
                AnyTypeIds = anyTypeIds;
                NoneTypeIds = noneTypeIds;
                StructuralWildcard = structuralWildcard;
                RevisionWildcard = revisionWildcard;
                LifecycleSensitive = lifecycleSensitive;
            }
        }
    }
}

#pragma warning restore CS0618
