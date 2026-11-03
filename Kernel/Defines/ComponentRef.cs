using System;
using System.Runtime.CompilerServices;
using CoreECS.Structures;

#pragma warning disable CS0618

#if COREECS_ULONG_ENTITY
using RevisionValue = System.UInt64;
#else
using RevisionValue = System.UInt32;
#endif

namespace CoreECS.Defines
{
    [Obsolete("Temporary v2 migration bridge")]
    public interface IComponentRefLocator
    {
        bool NotNull(uint version, int offset);
        ref T Get<T>(int offset) where T : struct, IComponent<T>;
        bool IsT(Type type);
        Type GetT();
        ulong GetEntityId(int offset);
        IComponentRefCore GetRefCore(int offset);
        uint GetRevision(int offset);
        uint ChangeRevision(int offset);
    }

    [Obsolete("Temporary v2 migration bridge")]
    public interface IComponentRefCore
    {
        IComponentRefLocator RefLocator { get; }
        int Offset { get; }
        uint Version { get; }
    }

    internal interface IHandlerComponentRefCore
    {
        ComponentHandler Handler { get; }
        ulong BindGeneration { get; }
    }

    public readonly struct ComponentRef : IEquatable<ComponentRef>
    {
        private readonly ComponentHandler m_handler;
        private readonly ulong m_bindGeneration;

        internal ComponentHandler Handler => m_handler;
        internal ulong BindGeneration => m_bindGeneration;

        [Obsolete("Temporary v2 migration bridge")]
        internal IComponentRefCore Core => m_handler == null
            ? null
            : new HandlerComponentRefCore(m_handler, m_bindGeneration);

        public bool NotNull => ComponentRefAccess.IsAlive(m_handler, m_bindGeneration);

        public Type RuntimeType => NotNull ? m_handler.Storage.ComponentType : null;

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

        [Obsolete("Temporary v2 migration bridge")]
        internal ComponentRef(IComponentRefCore core)
        {
            if (core == null)
            {
                m_handler = null;
                m_bindGeneration = 0;
                return;
            }

            if (!(core is IHandlerComponentRefCore handlerCore))
            {
                throw new ArgumentException("The compatibility core is not handler-backed.", nameof(core));
            }

            m_handler = handlerCore.Handler;
            m_bindGeneration = handlerCore.BindGeneration;
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

        public override bool Equals(object obj)
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

        [Obsolete("Temporary v2 migration bridge")]
        internal IComponentRefCore Core => m_handler == null
            ? null
            : new HandlerComponentRefCore(m_handler, m_bindGeneration);

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

        [Obsolete("Temporary v2 migration bridge")]
        internal ComponentRef(IComponentRefCore core)
        {
            if (core == null)
            {
                m_handler = null;
                m_bindGeneration = 0;
                return;
            }

            if (!(core is IHandlerComponentRefCore handlerCore))
            {
                throw new ArgumentException("The compatibility core is not handler-backed.", nameof(core));
            }

            m_handler = handlerCore.Handler;
            m_bindGeneration = handlerCore.BindGeneration;
            if (NotNull && m_handler.Storage.ComponentType != typeof(T))
            {
                throw new InvalidCastException("Given type is unmatched with actual component type.");
            }
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

        public override bool Equals(object obj)
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

    [Obsolete("Temporary v2 migration bridge")]
    internal sealed class HandlerComponentRefCore : IComponentRefCore, IComponentRefLocator, IHandlerComponentRefCore
    {
        public ComponentHandler Handler { get; }
        public ulong BindGeneration { get; }
        public IComponentRefLocator RefLocator => ComponentRefAccess.IsAlive(Handler, BindGeneration) ? this : null;
        public int Offset => ComponentRefAccess.IsAlive(Handler, BindGeneration) ? Handler.PackedOffset : -1;
        public uint Version => unchecked((uint)BindGeneration);

        internal HandlerComponentRefCore(ComponentHandler handler, ulong bindGeneration)
        {
            Handler = handler;
            BindGeneration = bindGeneration;
        }

        public bool NotNull(uint version, int offset) => ComponentRefAccess.IsAlive(Handler, BindGeneration);

        public ref TComponent Get<TComponent>(int offset)
            where TComponent : struct, IComponent<TComponent>
        {
            ComponentRefAccess.ThrowIfStale(Handler, BindGeneration);
            return ref ComponentRefAccess.Get<TComponent>(Handler);
        }

        public bool IsT(Type type) => type != null && Handler?.Storage?.ComponentType == type;
        public Type GetT() => Handler?.Storage?.ComponentType;

        public ulong GetEntityId(int offset)
        {
            return ComponentRefAccess.IsAlive(Handler, BindGeneration)
                ? Convert.ToUInt64(Handler.EntityId.Index)
                : 0;
        }

        public IComponentRefCore GetRefCore(int offset) => this;
        public uint GetRevision(int offset) => ComponentRefAccess.IsAlive(Handler, BindGeneration)
            ? unchecked((uint)Handler.Revision)
            : 0;

        public uint ChangeRevision(int offset)
        {
            ComponentRefAccess.ThrowIfStale(Handler, BindGeneration);
            Handler.RecordWrite();
            return unchecked((uint)Handler.Revision);
        }
    }

    [Obsolete("Temporary v2 migration bridge")]
    internal class ComponentRefCore : IComponentRefCore
    {
        private IComponentRefLocator m_locator;
        private int m_offset;
        private uint m_version;

        public IComponentRefLocator RefLocator => m_locator;
        public int Offset => m_offset;
        public uint Version => m_version;

        public ComponentRefCore(IComponentRefLocator locator, int offset, uint version)
        {
            Allocate(locator, offset, version);
        }

        public void Relocate(int offset) => m_offset = offset;

        public void Allocate(IComponentRefLocator locator, int offset, uint version)
        {
            m_locator = locator;
            m_offset = offset;
            m_version = version;
        }

        public void Invalidate()
        {
            m_locator = null;
            m_offset = -1;
            m_version = 0;
        }
    }
}
#pragma warning restore CS0618
