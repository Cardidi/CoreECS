using System;
using System.Runtime.CompilerServices;
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS
{
    /// <summary>
    /// Identity equality for v2 ref handles: a handler reference plus the bind generation
    /// captured by the handle. Storage owns one handler per component instance, so two
    /// handles to the same instance share both; a recycled handler yields a new generation
    /// and never aliases a stale handle.
    /// </summary>
    internal static class ComponentHandlerComparer
    {
        public static bool Equals(
            ComponentHandler left, uint leftGeneration, ComponentHandler right, uint rightGeneration)
        {
            return ReferenceEquals(left, right) && leftGeneration == rightGeneration;
        }

        public static int GetHashCode(ComponentHandler handler, uint handlerGeneration)
        {
            if (handler == null) return 0;

            unchecked
            {
                return (RuntimeHelpers.GetHashCode(handler) * 397) ^ (int)handlerGeneration;
            }
        }
    }

    /// <summary>
    /// Typeless component reference over the v2 kernel handler. <see cref="Handler"/> is internal
    /// because the kernel handler type is internal; public members keep v1 semantics.
    /// </summary>
    public readonly struct ComponentRef : IEquatable<ComponentRef>
    {
        /// <summary>Kernel handler; null for default/invalid refs.</summary>
        internal readonly ComponentHandler Handler;

        /// <summary>Bind generation captured when this handle was created.</summary>
        internal readonly uint HandlerGeneration;

        /// <summary>Creates a ref around a kernel handler (ECS integration only).</summary>
        internal ComponentRef(ComponentHandler handler)
        {
            Handler = handler;
            HandlerGeneration = handler?.BindGeneration ?? 0;
        }

        /// <summary>
        /// Creates a ref around a kernel handler carrying the given bind generation.
        /// Used by conversions so a stale handle cannot be resurrected with
        /// <c>noSafeCheck: true</c>.
        /// </summary>
        internal ComponentRef(ComponentHandler handler, uint handlerGeneration)
        {
            Handler = handler;
            HandlerGeneration = handlerGeneration;
        }

        private bool IsAlive => Handler != null && Handler.BindGeneration == HandlerGeneration;

        /// <summary>True when the referenced component instance still exists.</summary>
        public bool NotNull => IsAlive && Handler.NotNull;

        /// <summary>Runtime type of the referenced component, or null when invalid.</summary>
        public Type RuntimeType => NotNull ? ComponentTypeRegistry.GetById(Handler.TypeId).Type : null;

        /// <summary>Entity owning the component, or 0 when invalid.</summary>
        public ulong EntityId => NotNull ? Handler.EntityId : 0UL;

        /// <summary>Current revision, or 0 when invalid/tag.</summary>
        public ulong Revision => NotNull ? Handler.Revision : 0UL;

        /// <summary>Checks whether the ref points at a component of type <typeparamref name="T"/>.</summary>
        public bool Inspect<T>() where T : struct, IComponent<T>
            => NotNull && Handler.TypeId == ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        /// <summary>Checks whether the ref points at a component of the given type.</summary>
        public bool Inspect(Type type)
            => NotNull &&
               type != null &&
               ComponentTypeRegistry.TryGet(type, out var info) &&
               info.TypeId == Handler.TypeId;

        /// <summary>Converts to a typed ref, validating presence and type unless skipped.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        /// <exception cref="InvalidCastException">Thrown when the component type differs.</exception>
        public ComponentRef<T> Typed<T>(bool @unsafe = false) where T : struct, IComponent<T>
        {
            if (!@unsafe)
            {
                if (!NotNull) throw new NullReferenceException("Component Reference is cut.");
                if (Handler.TypeId != ComponentTypeRegistry.GetOrRegister<T>().TypeId)
                    throw new InvalidCastException("Given type is unmatched with actual component type.");
            }

            return new ComponentRef<T>(Handler, HandlerGeneration);
        }

        /// <inheritdoc />
        public bool Equals(ComponentRef other) =>
            ComponentHandlerComparer.Equals(Handler, HandlerGeneration, other.Handler, other.HandlerGeneration);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null) return Handler is null;
            return obj is ComponentRef other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() => ComponentHandlerComparer.GetHashCode(Handler, HandlerGeneration);

        public static bool operator ==(ComponentRef left, ComponentRef right) => left.Equals(right);

        public static bool operator !=(ComponentRef left, ComponentRef right) => !left.Equals(right);
    }

    /// <summary>
    /// Typed component reference over the v2 kernel handler. RO/RW read the owning structure
    /// directly; RW bumps the revision (emitting the change event through the structure
    /// observer) before handing out the writable ref.
    /// </summary>
    public readonly struct ComponentRef<T> : IEquatable<ComponentRef<T>> where T : struct, IComponent<T>
    {
        /// <summary>Kernel handler; null for default/invalid refs.</summary>
        internal readonly ComponentHandler Handler;

        /// <summary>Bind generation captured when this handle was created.</summary>
        internal readonly uint HandlerGeneration;

        /// <summary>Creates a ref around a kernel handler (ECS integration only).</summary>
        internal ComponentRef(ComponentHandler handler)
        {
            Handler = handler;
            HandlerGeneration = handler?.BindGeneration ?? 0;
        }

        /// <summary>
        /// Creates a ref around a kernel handler carrying the given bind generation.
        /// Used by conversions so a stale handle cannot be resurrected with
        /// <c>noSafeCheck: true</c>.
        /// </summary>
        internal ComponentRef(ComponentHandler handler, uint handlerGeneration)
        {
            Handler = handler;
            HandlerGeneration = handlerGeneration;
        }

        private bool IsAlive => Handler != null && Handler.BindGeneration == HandlerGeneration;

        /// <summary>True when the referenced component instance still exists.</summary>
        public bool NotNull => IsAlive && Handler.NotNull;

        /// <summary>Entity owning the component, or 0 when invalid.</summary>
        public ulong EntityId => NotNull ? Handler.EntityId : 0UL;

        /// <summary>Current revision, or 0 when invalid/tag.</summary>
        public ulong Revision => NotNull ? Handler.Revision : 0UL;

        /// <summary>Readonly ref to the component data.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        // ReSharper disable once InconsistentNaming
        public ref readonly T RO
        {
            get
            {
                var structure = RequireStructure();
                var row = Handler.Location.Row;
                switch (Handler.Kind)
                {
                    case ComponentKind.Dense:
                        Handler.TryGetDenseSlot(structure, out var slot);
                        return ref structure.GetDenseRefAt<T>(slot, row);
                    case ComponentKind.Sparse:
                    {
                        var store = Handler.GetSparseStore(structure);
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
                var handler = Handler;
                if (handler == null || handler.BindGeneration != HandlerGeneration)
                    throw new NullReferenceException("Component Reference is cut.");

                var structure = handler.Location?.Structure;
                if (structure == null || handler.Location.Generation != handler.Generation)
                    throw new NullReferenceException("Component Reference is cut.");

                var row = handler.Location.Row;
                switch (handler.Kind)
                {
                    case ComponentKind.Dense:
                    {
                        if (!handler.TryBumpDenseRevision(structure, row, out var slot))
                            throw new NullReferenceException("Component Reference is cut.");

                        if (structure.HasChangeInterest)
                        {
                            // Capture the mutating flag before notifying: a handler may
                            // remove itself synchronously, which must not skip the
                            // post-notification re-resolution.
                            var location = handler.Location;
                            var mutating = structure.HasMutatingChangeHandlers;

                            // A journal entry already pending for this (entity, type) makes
                            // the notification redundant unless public handlers must run.
                            var alreadyPending =
                                location.PendingRevisionIndex >= 0 &&
                                location.PendingRevisionTypeId == handler.TypeId;

                            if (mutating || !alreadyPending)
                            {
                                structure.NotifyChanged(row, handler.TypeId);

                                // A public handler may migrate or destroy the entity, so the
                                // live structure, row and slot must be re-resolved afterwards.
                                if (mutating)
                                {
                                    structure = RequireStructure();
                                    row = handler.Location.Row;
                                    handler.TryGetDenseSlot(structure, out slot);
                                }
                            }
                        }

                        return ref structure.GetDenseRefAt<T>(slot, row);
                    }
                    case ComponentKind.Sparse:
                    {
                        if (!handler.TryBumpSparseRevision(structure, row))
                            throw new NullReferenceException("Component Reference is cut.");

                        if (structure.HasChangeInterest)
                        {
                            var location = handler.Location;
                            var mutating = structure.HasMutatingChangeHandlers;
                            var alreadyPending =
                                location.PendingRevisionIndex >= 0 &&
                                location.PendingRevisionTypeId == handler.TypeId;

                            if (mutating || !alreadyPending)
                            {
                                structure.NotifyChanged(row, handler.TypeId);

                                if (mutating)
                                {
                                    structure = RequireStructure();
                                    row = handler.Location.Row;
                                }
                            }
                        }

                        var store = handler.GetSparseStore(structure);
                        if (store == null)
                        {
                            throw new InvalidOperationException(
                                $"Sparse component {typeof(T).Name} is not present at row {row}.");
                        }

                        return ref ((SparseStore<T>)store).Get(row);
                    }
                    default:
                        if (!handler.NotNull) throw new NullReferenceException("Component Reference is cut.");
                        throw new InvalidOperationException("Tag components carry no data.");
                }
            }
        }

        /// <summary>Converts to the typeless ref.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        public ComponentRef Untyped()
        {
            if (!NotNull) throw new NullReferenceException("Component Reference is cut.");
            return new ComponentRef(Handler, HandlerGeneration);
        }

        private Structure RequireStructure()
        {
            if (!NotNull) throw new NullReferenceException("Component Reference is cut.");
            return Handler.Location.Structure;
        }

        /// <inheritdoc />
        public bool Equals(ComponentRef<T> other) =>
            ComponentHandlerComparer.Equals(Handler, HandlerGeneration, other.Handler, other.HandlerGeneration);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null) return Handler is null;
            return obj is ComponentRef<T> other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() => ComponentHandlerComparer.GetHashCode(Handler, HandlerGeneration);

        public static bool operator ==(ComponentRef<T> left, ComponentRef<T> right) => left.Equals(right);

        public static bool operator !=(ComponentRef<T> left, ComponentRef<T> right) => !left.Equals(right);

        public static implicit operator ComponentRef(ComponentRef<T> obj) => obj.Untyped();

        public static explicit operator ComponentRef<T>(ComponentRef obj) => obj.Typed<T>();
    }
}
