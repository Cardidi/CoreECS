using System;
using System.Runtime.CompilerServices;
using CoreECS.Structures;

#if COREECS_ULONG_ENTITY
using RevisionValue = System.UInt64;
#else
using RevisionValue = System.UInt32;
#endif

namespace CoreECS.Defines
{
    public readonly struct ComponentRef : IEquatable<ComponentRef>
    {
        private readonly ComponentHandler m_handler;
        private readonly ulong m_bindGeneration;

        internal ComponentHandler Handler => m_handler;
        internal ulong BindGeneration => m_bindGeneration;

        public bool NotNull => ComponentRefAccess.IsAlive(m_handler, m_bindGeneration);

        public EntityId EntityId => NotNull ? m_handler.EntityId : default(EntityId);

        public RevisionValue Revision => NotNull ? m_handler.Revision : (RevisionValue)0;

        internal ComponentRef(ComponentHandler handler)
        {
            m_handler = handler;
            m_bindGeneration = handler?.BindGeneration ?? 0;
        }

        internal ComponentRef(ComponentHandler handler, ulong bindGeneration)
        {
            m_handler = handler;
            m_bindGeneration = bindGeneration;
        }

        public bool Inspect<T>() where T : struct, IComponent<T>
        {
            return Inspect(typeof(T));
        }

        public bool Inspect(Type type)
        {
            return type != null && NotNull && m_handler.Storage.ComponentType == type;
        }

        public ComponentRef<T> Typed<T>() where T : struct, IComponent<T>
        {
            ComponentRefAccess.ThrowIfStale(m_handler, m_bindGeneration);
            if (m_handler.Storage.ComponentType != typeof(T))
            {
                throw new InvalidCastException("Given type is unmatched with actual component type.");
            }

            return new ComponentRef<T>(m_handler, m_bindGeneration);
        }

        public bool Equals(ComponentRef other)
        {
            return ReferenceEquals(m_handler, other.m_handler) && m_bindGeneration == other.m_bindGeneration;
        }

        public override bool Equals(object? obj)
        {
            return obj is ComponentRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int handlerHash = m_handler == null ? 0 : RuntimeHelpers.GetHashCode(m_handler);
                return (handlerHash * 397) ^ m_bindGeneration.GetHashCode();
            }
        }

        public static bool operator ==(ComponentRef left, ComponentRef right) => left.Equals(right);
        public static bool operator !=(ComponentRef left, ComponentRef right) => !left.Equals(right);
    }

    public readonly struct ComponentRef<T> : IEquatable<ComponentRef<T>>
        where T : struct, IComponent<T>
    {
        private readonly ComponentHandler m_handler;
        private readonly ulong m_bindGeneration;

        internal ComponentHandler Handler => m_handler;
        internal ulong BindGeneration => m_bindGeneration;

        public bool NotNull => ComponentRefAccess.IsAlive(m_handler, m_bindGeneration);

        public EntityId EntityId => NotNull ? m_handler.EntityId : default(EntityId);

        public RevisionValue Revision => NotNull ? m_handler.Revision : (RevisionValue)0;

        public ref readonly T RO
        {
            get
            {
                ComponentRefAccess.ThrowIfStale(m_handler, m_bindGeneration);
                return ref ComponentRefAccess.Get<T>(m_handler);
            }
        }

        public ref T RW
        {
            get
            {
                ComponentRefAccess.ThrowIfStale(m_handler, m_bindGeneration);
                m_handler.RecordWrite();
                return ref ComponentRefAccess.Get<T>(m_handler);
            }
        }

        internal ComponentRef(ComponentHandler handler)
        {
            m_handler = handler;
            m_bindGeneration = handler?.BindGeneration ?? 0;
        }

        internal ComponentRef(ComponentHandler handler, ulong bindGeneration)
        {
            m_handler = handler;
            m_bindGeneration = bindGeneration;
        }

        public ComponentRef Untyped()
        {
            ComponentRefAccess.ThrowIfStale(m_handler, m_bindGeneration);
            return new ComponentRef(m_handler, m_bindGeneration);
        }

        public bool Equals(ComponentRef<T> other)
        {
            return ReferenceEquals(m_handler, other.m_handler) && m_bindGeneration == other.m_bindGeneration;
        }

        public override bool Equals(object? obj)
        {
            return obj is ComponentRef<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int handlerHash = m_handler == null ? 0 : RuntimeHelpers.GetHashCode(m_handler);
                return (handlerHash * 397) ^ m_bindGeneration.GetHashCode();
            }
        }

        public static bool operator ==(ComponentRef<T> left, ComponentRef<T> right) => left.Equals(right);
        public static bool operator !=(ComponentRef<T> left, ComponentRef<T> right) => !left.Equals(right);
        public static implicit operator ComponentRef(ComponentRef<T> value) => value.Untyped();
        public static explicit operator ComponentRef<T>(ComponentRef value) => value.Typed<T>();
    }

    internal static class ComponentRefAccess
    {
        internal static bool IsAlive(ComponentHandler handler, ulong bindGeneration)
        {
            return handler != null && handler.Storage != null && handler.BindGeneration == bindGeneration;
        }

        internal static void ThrowIfStale(ComponentHandler handler, ulong bindGeneration)
        {
            if (!IsAlive(handler, bindGeneration))
            {
                throw new NullReferenceException("Component Reference is cut.");
            }
        }

        internal static ref T Get<T>(ComponentHandler handler)
            where T : struct, IComponent<T>
        {
            if (handler.Storage is ComponentStorage<T> dataStorage)
            {
                return ref dataStorage.GetPayloadAt(handler.PackedOffset);
            }

            if (handler.Storage is TagStorage<T> tagStorage)
            {
                return ref tagStorage.GetSharedDefault();
            }

            throw new InvalidCastException("Given type is unmatched with actual component type.");
        }
    }

}
