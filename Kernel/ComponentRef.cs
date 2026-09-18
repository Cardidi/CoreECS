using System;
using System.Runtime.CompilerServices;
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS
{
    /// <summary>
    /// Identity equality for v2 ref handles: a core reference plus the bind generation
    /// captured by the handle. Storage owns one core per component instance, so two
    /// handles to the same instance share both; a recycled core yields a new generation
    /// and never aliases a stale handle.
    /// </summary>
    internal static class ComponentRefCoreComparer
    {
        public static bool Equals(
            ComponentRefCore left, uint leftGeneration, ComponentRefCore right, uint rightGeneration)
        {
            return ReferenceEquals(left, right) && leftGeneration == rightGeneration;
        }

        public static int GetHashCode(ComponentRefCore core, uint coreGeneration)
        {
            if (core == null) return 0;

            unchecked
            {
                return (RuntimeHelpers.GetHashCode(core) * 397) ^ (int)coreGeneration;
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

        /// <summary>Bind generation captured when this handle was created.</summary>
        internal readonly uint CoreGeneration;

        /// <summary>Creates a ref around a kernel core (ECS integration only).</summary>
        internal ComponentRef(ComponentRefCore core)
        {
            Core = core;
            CoreGeneration = core?.BindGeneration ?? 0;
        }

        /// <summary>
        /// Creates a ref around a kernel core carrying the given bind generation.
        /// Used by conversions so a stale handle cannot be resurrected with
        /// <c>noSafeCheck: true</c>.
        /// </summary>
        internal ComponentRef(ComponentRefCore core, uint coreGeneration)
        {
            Core = core;
            CoreGeneration = coreGeneration;
        }

        private bool IsAlive => Core != null && Core.BindGeneration == CoreGeneration;

        /// <summary>True when the referenced component instance still exists.</summary>
        public bool NotNull => IsAlive && Core.NotNull;

        /// <summary>Runtime type of the referenced component, or null when invalid.</summary>
        public Type RuntimeType => NotNull ? ComponentTypeRegistry.GetById(Core.TypeId).Type : null;

        /// <summary>Entity owning the component, or 0 when invalid.</summary>
        public ulong EntityId => NotNull ? Core.EntityId : 0UL;

        /// <summary>Current revision, or 0 when invalid/tag.</summary>
        public ulong Revision => NotNull ? Core.Revision : 0UL;

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
                if (!NotNull) throw new NullReferenceException("Component Reference is cut.");
                if (Core.TypeId != ComponentTypeRegistry.GetOrRegister<T>().TypeId)
                    throw new InvalidCastException("Given type is unmatched with actual component type.");
            }

            return new ComponentRef<T>(Core, CoreGeneration);
        }

        /// <inheritdoc />
        public bool Equals(ComponentRef other) =>
            ComponentRefCoreComparer.Equals(Core, CoreGeneration, other.Core, other.CoreGeneration);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null) return Core is null;
            return obj is ComponentRef other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() => ComponentRefCoreComparer.GetHashCode(Core, CoreGeneration);

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

        /// <summary>Bind generation captured when this handle was created.</summary>
        internal readonly uint CoreGeneration;

        /// <summary>Creates a ref around a kernel core (ECS integration only).</summary>
        internal ComponentRef(ComponentRefCore core)
        {
            Core = core;
            CoreGeneration = core?.BindGeneration ?? 0;
        }

        /// <summary>
        /// Creates a ref around a kernel core carrying the given bind generation.
        /// Used by conversions so a stale handle cannot be resurrected with
        /// <c>noSafeCheck: true</c>.
        /// </summary>
        internal ComponentRef(ComponentRefCore core, uint coreGeneration)
        {
            Core = core;
            CoreGeneration = coreGeneration;
        }

        private bool IsAlive => Core != null && Core.BindGeneration == CoreGeneration;

        /// <summary>True when the referenced component instance still exists.</summary>
        public bool NotNull => IsAlive && Core.NotNull;

        /// <summary>Entity owning the component, or 0 when invalid.</summary>
        public ulong EntityId => NotNull ? Core.EntityId : 0UL;

        /// <summary>Current revision, or 0 when invalid/tag.</summary>
        public ulong Revision => NotNull ? Core.Revision : 0UL;

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
                        Core.TryGetDenseSlot(structure, out var slot);
                        return ref structure.GetDenseRefAt<T>(slot, row);
                    case ComponentKind.Sparse:
                    {
                        var store = Core.GetSparseStore(structure);
                        if (store == null)
                        {
                            throw new InvalidOperationException(
                                $"Sparse component {typeof(T).Name} is not present at row {row}.");
                        }

                        return ref ((SparseStore<T>)store).Get(row);
                    }
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
                var core = Core;
                if (core == null || core.BindGeneration != CoreGeneration)
                    throw new NullReferenceException("Component Reference is cut.");

                var structure = core.Location?.Structure;
                if (structure == null || core.Location.Generation != core.Generation)
                    throw new NullReferenceException("Component Reference is cut.");

                var row = core.Location.Row;
                switch (core.Kind)
                {
                    case ComponentKind.Dense:
                    {
                        if (!core.TryBumpDenseRevision(structure, row, out var slot))
                            throw new NullReferenceException("Component Reference is cut.");

                        if (structure.HasChangeInterest)
                        {
                            // Capture the mutating flag before notifying: a handler may
                            // remove itself synchronously, which must not skip the
                            // post-notification re-resolution.
                            var location = core.Location;
                            var mutating = structure.HasMutatingChangeHandlers;

                            // A journal entry already pending for this (entity, type) makes
                            // the notification redundant unless public handlers must run.
                            var alreadyPending =
                                location.PendingRevisionIndex >= 0 &&
                                location.PendingRevisionTypeId == core.TypeId;

                            if (mutating || !alreadyPending)
                            {
                                structure.NotifyChanged(row, core.TypeId);

                                // A public handler may migrate or destroy the entity, so the
                                // live structure, row and slot must be re-resolved afterwards.
                                if (mutating)
                                {
                                    structure = RequireStructure();
                                    row = core.Location.Row;
                                    core.TryGetDenseSlot(structure, out slot);
                                }
                            }
                        }

                        return ref structure.GetDenseRefAt<T>(slot, row);
                    }
                    case ComponentKind.Sparse:
                    {
                        if (!core.TryBumpSparseRevision(structure, row))
                            throw new NullReferenceException("Component Reference is cut.");

                        if (structure.HasChangeInterest)
                        {
                            var location = core.Location;
                            var mutating = structure.HasMutatingChangeHandlers;
                            var alreadyPending =
                                location.PendingRevisionIndex >= 0 &&
                                location.PendingRevisionTypeId == core.TypeId;

                            if (mutating || !alreadyPending)
                            {
                                structure.NotifyChanged(row, core.TypeId);

                                if (mutating)
                                {
                                    structure = RequireStructure();
                                    row = core.Location.Row;
                                }
                            }
                        }

                        var store = core.GetSparseStore(structure);
                        if (store == null)
                        {
                            throw new InvalidOperationException(
                                $"Sparse component {typeof(T).Name} is not present at row {row}.");
                        }

                        return ref ((SparseStore<T>)store).Get(row);
                    }
                    default:
                        if (!core.NotNull) throw new NullReferenceException("Component Reference is cut.");
                        throw new InvalidOperationException("Tag components carry no data.");
                }
            }
        }

        /// <summary>Converts to the typeless ref.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        public ComponentRef Untyped()
        {
            if (!NotNull) throw new NullReferenceException("Component Reference is cut.");
            return new ComponentRef(Core, CoreGeneration);
        }

        private Structure RequireStructure()
        {
            if (!NotNull) throw new NullReferenceException("Component Reference is cut.");
            return Core.Location.Structure;
        }

        /// <inheritdoc />
        public bool Equals(ComponentRef<T> other) =>
            ComponentRefCoreComparer.Equals(Core, CoreGeneration, other.Core, other.CoreGeneration);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null) return Core is null;
            return obj is ComponentRef<T> other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() => ComponentRefCoreComparer.GetHashCode(Core, CoreGeneration);

        public static bool operator ==(ComponentRef<T> left, ComponentRef<T> right) => left.Equals(right);

        public static bool operator !=(ComponentRef<T> left, ComponentRef<T> right) => !left.Equals(right);

        public static implicit operator ComponentRef(ComponentRef<T> obj) => obj.Untyped();

        public static explicit operator ComponentRef<T>(ComponentRef obj) => obj.Typed<T>();
    }
}
