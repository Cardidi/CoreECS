using System;
using System.Runtime.CompilerServices;
using CoreECS.Structures;

namespace CoreECS.Defines
{
    /// <summary>
    /// Interface for a component reference memory locator.
    /// Provides methods to locate and access component data in memory without knowing the exact type.
    /// This is part of the low-level component access system in the ECS framework.
    /// </summary>
    public interface IComponentRefLocator
    {
        /// <summary>
        /// Checks if a component reference at the specified offset is valid and not null.
        /// </summary>
        /// <param name="version">Version of the component reference to verify</param>
        /// <param name="offset">Memory offset of the component reference</param>
        /// <returns>True if the component reference is valid and not null, false otherwise</returns>
        public bool NotNull(uint version, int offset);

        /// <summary>
        /// Gets a reference to the component data of type T at the specified offset.
        /// </summary>
        /// <typeparam name="T">Component type, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <param name="offset">Memory offset of the component</param>
        /// <returns>Reference to the component data</returns>
        public ref T Get<T>(int offset) where T : struct, IComponent<T>;

        /// <summary>
        /// Checks if the component at the specified offset is of the given type.
        /// </summary>
        /// <param name="type">Type to check against</param>
        /// <returns>True if the component is of the specified type, false otherwise</returns>
        public bool IsT(Type type);

        /// <summary>
        /// Gets the actual runtime type of the component at the specified offset.
        /// </summary>
        /// <returns>The runtime type of the component</returns>
        public Type GetT();

        /// <summary>
        /// Gets the entity ID associated with the component at the specified offset.
        /// </summary>
        /// <param name="offset">Memory offset of the component</param>
        /// <returns>Entity ID that owns this component</returns>
        public ulong GetEntityId(int offset);

        /// <summary>
        /// Gets the core reference object for the component at the specified offset.
        /// </summary>
        /// <param name="offset">Memory offset of the component</param>
        /// <returns>Core reference object containing locator and offset information</returns>
        public IComponentRefCore GetRefCore(int offset);
        
        /// <summary>
        /// Gets the modification revision of the component at the specified offset.
        /// </summary>
        /// <param name="offset">Memory offset of the component</param>
        /// <returns>Modification revision of the component</returns>
        public uint GetRevision(int offset);

        /// <summary>
        /// Changes the modification revision of the component at the specified offset.
        /// </summary>
        /// <param name="offset">Memory offset of the component</param>
        /// <returns>New modification revision of the component</returns>
        public uint ChangeRevision(int offset);
    }

    /// <summary>
    /// Readonly component core reference object that holds the component reference information.
    /// Contains the locator, offset, and version needed to access a component in memory.
    /// </summary>
    public interface IComponentRefCore
    {
        /// <summary>
        /// Locator for this component reference, used to access the actual component data.
        /// </summary>
        IComponentRefLocator RefLocator { get; }
        
        /// <summary>
        /// Memory offset of this component reference within its container.
        /// </summary>
        int Offset { get; }
        
        /// <summary>
        /// Version of this component reference, used for validation and to detect stale references.
        /// </summary>
        uint Version { get; }
    }

    /// <summary>
    /// Value equality for v2 ref cores. v1 compared shared pooled cores by reference;
    /// v2 creates a core per access, so identity is the component instance coordinates.
    /// </summary>
    internal static class ComponentRefCoreComparer
    {
        public static bool Equals(ComponentRefCore left, ComponentRefCore right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;

            return ReferenceEquals(left.Location, right.Location)
                   && left.Generation == right.Generation
                   && left.TypeId == right.TypeId
                   && left.Kind == right.Kind
                   && left.Version == right.Version;
        }

        public static int GetHashCode(ComponentRefCore core)
        {
            if (core == null) return 0;

            unchecked
            {
                var hash = core.Location == null ? 0 : RuntimeHelpers.GetHashCode(core.Location);
                hash = (hash * 397) ^ (int)core.Generation;
                hash = (hash * 397) ^ (int)core.TypeId;
                hash = (hash * 397) ^ (int)core.Kind;
                hash = (hash * 397) ^ (int)core.Version;
                return hash;
            }
        }
    }

    /// <summary>
    /// Typeless component reference over the v2 kernel core. <see cref="Core"/> is internal
    /// because the kernel core type is internal; public members keep v1 semantics.
    /// </summary>
    public readonly struct ComponentRef : IEquatable<ComponentRef>
    {
        /// <summary>Kernel reference core; null for default/invalid refs.</summary>
        internal readonly ComponentRefCore Core;

        /// <summary>Creates a ref around a kernel core (ECS integration only).</summary>
        internal ComponentRef(ComponentRefCore core)
        {
            Core = core;
        }

        /// <summary>True when the referenced component instance still exists.</summary>
        public bool NotNull => Core != null && Core.NotNull;

        /// <summary>Runtime type of the referenced component, or null when invalid.</summary>
        public Type RuntimeType => NotNull ? ComponentTypeRegistry.GetById(Core.TypeId).Type : null;

        /// <summary>Entity owning the component, or 0 when invalid.</summary>
        public ulong EntityId => Core?.EntityId ?? 0UL;

        /// <summary>Current revision, or 0 when invalid/tag.</summary>
        public ulong Revision => Core?.Revision ?? 0UL;

        /// <summary>Checks whether the ref points at a component of type <typeparamref name="T"/>.</summary>
        public bool Inspect<T>() where T : struct, IComponent<T>
            => NotNull && Core.TypeId == ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        /// <summary>Checks whether the ref points at a component of the given type.</summary>
        public bool Inspect(Type type)
            => NotNull
               && type != null
               && ComponentTypeRegistry.TryGet(type, out var info)
               && info.TypeId == Core.TypeId;

        /// <summary>Converts to a typed ref, validating presence and type unless skipped.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        /// <exception cref="InvalidCastException">Thrown when the component type differs.</exception>
        public ComponentRef<T> Typed<T>(bool noSafeCheck = false) where T : struct, IComponent<T>
        {
            if (!noSafeCheck)
            {
                if (Core == null || !Core.NotNull) throw new NullReferenceException("Component Reference is cut.");
                if (Core.TypeId != ComponentTypeRegistry.GetOrRegister<T>().TypeId)
                    throw new InvalidCastException("Given type is unmatched with actual component type.");
            }

            return new ComponentRef<T>(Core);
        }

        /// <inheritdoc />
        public bool Equals(ComponentRef other) => ComponentRefCoreComparer.Equals(Core, other.Core);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null) return Core is null;
            return obj is ComponentRef other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() => ComponentRefCoreComparer.GetHashCode(Core);

        public static bool operator ==(ComponentRef left, ComponentRef right) => left.Equals(right);

        public static bool operator !=(ComponentRef left, ComponentRef right) => !left.Equals(right);
    }

    /// <summary>
    /// Typed component reference over the v2 kernel core. RO/RW read the owning structure
    /// directly; RW bumps the revision (emitting the change event through the structure
    /// observer) before handing out the writable ref.
    /// </summary>
    public readonly struct ComponentRef<T> : IEquatable<ComponentRef<T>> where T : struct, IComponent<T>
    {
        /// <summary>Kernel reference core; null for default/invalid refs.</summary>
        internal readonly ComponentRefCore Core;

        /// <summary>Creates a ref around a kernel core (ECS integration only).</summary>
        internal ComponentRef(ComponentRefCore core)
        {
            Core = core;
        }

        /// <summary>True when the referenced component instance still exists.</summary>
        public bool NotNull => Core != null && Core.NotNull;

        /// <summary>Entity owning the component, or 0 when invalid.</summary>
        public ulong EntityId => Core?.EntityId ?? 0UL;

        /// <summary>Current revision, or 0 when invalid/tag.</summary>
        public ulong Revision => Core?.Revision ?? 0UL;

        /// <summary>Readonly ref to the component data.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        public ref readonly T RO
        {
            get
            {
                var structure = RequireStructure();
                var row = Core.Location.Row;
                switch (Core.Kind)
                {
                    case ComponentKind.Dense:
                        return ref structure.GetDenseRef<T>(row);
                    case ComponentKind.Discrete:
                        return ref structure.GetDiscreteRef<T>(row);
                    default:
                        throw new InvalidOperationException("Tag components carry no data.");
                }
            }
        }

        /// <summary>Writable ref to the component data; bumps the revision on access.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        public ref T RW
        {
            get
            {
                var structure = RequireStructure();
                Core.ChangeRevision();
                var row = Core.Location.Row;
                switch (Core.Kind)
                {
                    case ComponentKind.Dense:
                        return ref structure.GetDenseRef<T>(row);
                    case ComponentKind.Discrete:
                        return ref structure.GetDiscreteRef<T>(row);
                    default:
                        throw new InvalidOperationException("Tag components carry no data.");
                }
            }
        }

        /// <summary>Converts to the typeless ref.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        public ComponentRef Untyped()
        {
            if (Core == null || !Core.NotNull) throw new NullReferenceException("Component Reference is cut.");
            return new ComponentRef(Core);
        }

        private Structure RequireStructure()
        {
            if (Core == null || !Core.NotNull) throw new NullReferenceException("Component Reference is cut.");
            return Core.Location.Structure;
        }

        /// <inheritdoc />
        public bool Equals(ComponentRef<T> other) => ComponentRefCoreComparer.Equals(Core, other.Core);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null) return Core is null;
            return obj is ComponentRef<T> other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() => ComponentRefCoreComparer.GetHashCode(Core);

        public static bool operator ==(ComponentRef<T> left, ComponentRef<T> right) => left.Equals(right);

        public static bool operator !=(ComponentRef<T> left, ComponentRef<T> right) => !left.Equals(right);

        public static implicit operator ComponentRef(ComponentRef<T> obj) => obj.Untyped();

        public static explicit operator ComponentRef<T>(ComponentRef obj) => obj.Typed<T>();
    }
}
