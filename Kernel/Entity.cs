using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;

namespace CoreECS
{
    /// <summary>
    /// A World-local handle for an entity.
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        private readonly World m_world;
        private readonly EntityId m_entityId;

        public World World => m_world ??
            throw new InvalidOperationException("Entity is not associated with a World.");

        public EntityId EntityId => m_entityId;

        internal World WorldOrNull => m_world;

        public bool IsValid => m_world != null && m_world.Ready &&
                               m_world.GetManager<EntityManager>().IsAlive(m_entityId);

        public ulong Mask => AccessEntityManager().GetMask(m_entityId);

        internal Entity(World world, EntityId entityId)
        {
            m_world = world ?? throw new ArgumentNullException(nameof(world));
            m_entityId = entityId;
        }

        public ComponentRef<T> CreateComponent<T>()
            where T : struct, IComponent<T>
        {
            T initialValue = default(T);
            return CreateComponent(in initialValue);
        }

        public ComponentRef<T> CreateComponent<T>(in T initialValue)
            where T : struct, IComponent<T>
        {
            return AccessComponentManager().Create(m_entityId, in initialValue);
        }

        public ComponentRef<T> GetComponent<T>()
            where T : struct, IComponent<T>
        {
            return AccessComponentManager().Get<T>(m_entityId);
        }

        public bool HasComponent<T>()
            where T : struct, IComponent<T>
        {
            return AccessComponentManager().Has<T>(m_entityId);
        }

        public void DestroyComponent<T>()
            where T : struct, IComponent<T>
        {
            AccessComponentManager().Destroy<T>(m_entityId);
        }

        public void DestroyComponent<T>(ComponentRef<T> component)
            where T : struct, IComponent<T>
        {
            AccessComponentManager().Destroy(component, m_entityId);
        }

        public void DestroyComponent(ComponentRef component)
        {
            AccessComponentManager().Destroy(component, m_entityId);
        }

        public int GetComponents(ICollection<ComponentRef> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            ComponentManager componentManager = AccessComponentManager();
            ComponentHandler handler = componentManager.EntityTable.GetComponentHead(m_entityId);
            int count = 0;
            while (handler != null)
            {
                output.Add(new ComponentRef(handler));
                handler = handler.Next;
                count++;
            }

            return count;
        }

        public bool Equals(Entity other)
        {
            return ReferenceEquals(m_world, other.m_world) && m_entityId == other.m_entityId;
        }

        public override bool Equals(object obj)
        {
            return obj is Entity other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int worldHash = m_world == null ? 0 : RuntimeHelpers.GetHashCode(m_world);
                return (worldHash * 397) ^ m_entityId.GetHashCode();
            }
        }

        public static bool operator ==(Entity left, Entity right) => left.Equals(right);

        public static bool operator !=(Entity left, Entity right) => !left.Equals(right);

        private EntityManager AccessEntityManager()
        {
            if (m_world == null || !m_world.Ready)
            {
                throw new InvalidOperationException("Entity is not associated with an active World.");
            }

            EntityManager manager = m_world.GetManager<EntityManager>();
            if (!manager.IsAlive(m_entityId))
            {
                throw new InvalidOperationException("Entity has already been destroyed.");
            }

            return manager;
        }

        private ComponentManager AccessComponentManager()
        {
            AccessEntityManager();
            return m_world.GetManager<ComponentManager>();
        }
    }
}
