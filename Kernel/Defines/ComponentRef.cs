using System;
using System.Runtime.CompilerServices;
using CoreECS.Structures;

namespace CoreECS.Defines
{
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
        // ReSharper disable once InconsistentNaming
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
                    case ComponentKind.Sparse:
                        return ref structure.GetSparseRef<T>(row);
                    default:
                        throw new InvalidOperationException("Tag components carry no data.");
                }
            }
        }

        /// <summary>Writable ref to the component data; bumps the revision on access.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        // ReSharper disable once InconsistentNaming
        public ref T RW
        {
            get
            {
                Core.ChangeRevision();

                // Resolve after the change notification: a handler may migrate or destroy
                // the entity, so the live structure and row must be read afterwards.
                var structure = RequireStructure();
                var row = Core.Location.Row;
                switch (Core.Kind)
                {
                    case ComponentKind.Dense:
                        return ref structure.GetDenseRef<T>(row);
                    case ComponentKind.Sparse:
                        return ref structure.GetSparseRef<T>(row);
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
