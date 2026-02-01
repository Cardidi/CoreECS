using System;

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
    /// Typeless component reference accessor that provides access to component data without knowing its type.
    /// This struct acts as a wrapper around IComponentRefCore and provides methods to inspect and cast
    /// the component reference to a specific type.
    /// </summary>
    public readonly struct ComponentRef : IEquatable<ComponentRef>
    {
        /// <summary>
        /// Core reference object containing locator, offset, and version information.
        /// </summary>
        public readonly IComponentRefCore Core;

        /// <summary>
        /// Checks if this component reference is valid and not null.
        /// </summary>
        public bool NotNull => Core?.RefLocator != null && Core.RefLocator.NotNull(Core.Version, Core.Offset);

        /// <summary>
        /// Gets the actual runtime type of the component this reference points to.
        /// </summary>
        /// <returns>The runtime type of the component, or null if the reference is invalid</returns>
        public Type RuntimeType
        {
            get
            {
                if (Core?.RefLocator == null || !Core.RefLocator.NotNull(Core.Version, Core.Offset)) return null;
                return Core.RefLocator.GetT();
            }
        }

        /// <summary>
        /// Gets the entity ID that owns this component.
        /// </summary>
        public ulong EntityId
        {
            get
            {
                if (Core?.RefLocator == null || !Core.RefLocator.NotNull(Core.Version, Core.Offset)) return 0;
                return Core.RefLocator.GetEntityId(Core.Offset);
            }
        }

        /// <summary>
        /// Checks if the component this reference points to is of type T.
        /// </summary>
        /// <typeparam name="T">Component type to check against, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>True if the component is of type T, false otherwise</returns>
        public bool Inspect<T>() where T : struct, IComponent<T>
        {
            if (Core?.RefLocator == null || !Core.RefLocator.NotNull(Core.Version, Core.Offset)) return false;
            return Core.RefLocator.IsT(typeof(T));
        }
        
        /// <summary>
        /// Checks if the component this reference points to is of the specified type.
        /// </summary>
        /// <param name="type">Type to check against</param>
        /// <returns>True if the component is of the specified type, false otherwise</returns>
        public bool Inspect(Type type)
        {
            if (Core?.RefLocator == null || !Core.RefLocator.NotNull(Core.Version, Core.Offset)) return false;
            return Core.RefLocator.IsT(type);
        }

        /// <summary>
        /// Converts this typeless component reference to a typed component reference.
        /// </summary>
        /// <typeparam name="T">Target component type, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <param name="noSafeCheck">If true, skips type safety check for better performance</param>
        /// <returns>A typed component reference of type ComponentRef&lt;T&gt;</returns>
        /// <exception cref="InvalidCastException">Thrown when the component type doesn't match T</exception>
        /// <exception cref="NullReferenceException">Thrown when the component reference is invalid</exception>
        public ComponentRef<T> Typed<T>(bool noSafeCheck = false) where T : struct, IComponent<T>
        {
            if (noSafeCheck || Core?.RefLocator != null && Core.RefLocator.NotNull(Core.Version, Core.Offset))
            {
                if (noSafeCheck || Core.RefLocator.IsT(typeof(T))) return new ComponentRef<T>(Core);
                throw new InvalidCastException("Given type is unmatched with actual component type.");
            }

            throw new NullReferenceException("Component Reference is cut.");
        }
        
        /// <summary>
        /// Constructor used by the ECS framework to create a component reference.
        /// </summary>
        /// <param name="core">Core reference object containing locator, offset, and version</param>
        public ComponentRef(IComponentRefCore core)
        {
            Core = core;
        }

        /// <summary>
        /// Determines if this component reference is equal to another component reference.
        /// </summary>
        /// <param name="other">The other component reference to compare with</param>
        /// <returns>True if the references are equal, false otherwise</returns>
        public bool Equals(ComponentRef other)
        {
            return Core == other.Core;  // Compare the core objects directly
        }

        /// <summary>
        /// Determines if this component reference is equal to the specified object.
        /// </summary>
        /// <param name="obj">The object to compare with</param>
        /// <returns>True if the object is a component reference and is equal, false otherwise</returns>
        public override bool Equals(object obj)
        {
            if (obj is null) return Core is null;  // Check if Core is null for null comparison
            return obj is ComponentRef other && Equals(other);
        }

        /// <summary>
        /// Gets the hash code for this component reference.
        /// </summary>
        /// <returns>The hash code</returns>
        public override int GetHashCode()
        {
            return (Core != null ? Core.GetHashCode() : 0);
        }

        /// <summary>
        /// Equality operator for component references.
        /// </summary>
        /// <param name="left">Left operand</param>
        /// <param name="right">Right operand</param>
        /// <returns>True if the references are equal, false otherwise</returns>
        public static bool operator ==(ComponentRef left, ComponentRef right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for component references.
        /// </summary>
        /// <param name="left">Left operand</param>
        /// <param name="right">Right operand</param>
        /// <returns>True if the references are not equal, false otherwise</returns>
        public static bool operator !=(ComponentRef left, ComponentRef right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Typed component reference accessor that provides direct access to component data of a specific type.
    /// This struct wraps IComponentRefCore and provides typed access to component data through
    /// readonly (RO) and read-write (RW) properties.
    /// </summary>
    /// <typeparam name="T">Component type, must be a struct implementing IComponent&lt;T&gt;</typeparam>
    public readonly struct ComponentRef<T> : IEquatable<ComponentRef<T>> where T : struct, IComponent<T>
    {
        /// <summary>
        /// Core reference object containing locator, offset, and version information.
        /// </summary>
        public readonly IComponentRefCore Core;
        
        /// <summary>
        /// Checks if this component reference is valid and not null.
        /// </summary>
        public bool NotNull => Core?.RefLocator != null && Core.RefLocator.NotNull(Core.Version, Core.Offset);

        /// <summary>
        /// Gets the entity ID that owns this component.
        /// </summary>
        public ulong EntityId
        {
            get
            {
                if (Core?.RefLocator == null || !Core.RefLocator.NotNull(Core.Version, Core.Offset)) return 0;
                return Core.RefLocator.GetEntityId(Core.Offset);
            }
        }
        
        /// <summary>
        /// Gets a readonly reference to the component data.
        /// Provides read-only access to the component data without allowing modification.
        /// </summary>
        /// <exception cref="NullReferenceException">Thrown when the component reference is invalid</exception>
        public ref readonly T RO
        {
            get 
            {
                if (Core?.RefLocator == null || !Core.RefLocator.NotNull(Core.Version, Core.Offset))
                    throw new NullReferenceException("Component Reference is cut.");
            
                return ref Core.RefLocator.Get<T>(Core.Offset);
            }
        }
        
        /// <summary>
        /// Gets a read-write reference to the component data.
        /// Provides both read and write access to the component data.
        /// </summary>
        /// <exception cref="NullReferenceException">Thrown when the component reference is invalid</exception>
        public ref T RW
        {
            get 
            {
                if (Core?.RefLocator == null || !Core.RefLocator.NotNull(Core.Version, Core.Offset))
                    throw new NullReferenceException("Component Reference is cut.");
            
                return ref Core.RefLocator.Get<T>(Core.Offset);
            }
        }
        
        /// <summary>
        /// Converts this typed component reference to a typeless component reference.
        /// </summary>
        /// <returns>A typeless component reference</returns>
        /// <exception cref="NullReferenceException">Thrown when the component reference is invalid</exception>
        public ComponentRef Untyped()
        {
            if (Core?.RefLocator != null && Core.RefLocator.NotNull(Core.Version, Core.Offset))
                return new ComponentRef(Core);

            throw new NullReferenceException("Component Reference is cut.");
        }

        /// <summary>
        /// Constructor used by the ECS framework to create a typed component reference.
        /// </summary>
        /// <param name="core">Core reference object containing locator, offset, and version</param>
        public ComponentRef(IComponentRefCore core)
        {
            Core = core;
        }
        
        /// <summary>
        /// Determines if this typed component reference is equal to another typed component reference.
        /// </summary>
        /// <param name="other">The other typed component reference to compare with</param>
        /// <returns>True if the references are equal, false otherwise</returns>
        public bool Equals(ComponentRef<T> other)
        {
            return Core == other.Core;  // Compare the core objects directly
        }

        /// <summary>
        /// Determines if this typed component reference is equal to the specified object.
        /// </summary>
        /// <param name="obj">The object to compare with</param>
        /// <returns>True if the object is a typed component reference and is equal, false otherwise</returns>
        public override bool Equals(object obj)
        {
            if (obj is null) return Core is null;  // Check if Core is null for null comparison
            return obj is ComponentRef<T> other && Equals(other);
        }

        /// <summary>
        /// Gets the hash code for this typed component reference.
        /// </summary>
        /// <returns>The hash code</returns>
        public override int GetHashCode()
        {
            return (Core != null ? Core.GetHashCode() : 0);
        }

        /// <summary>
        /// Equality operator for typed component references.
        /// </summary>
        /// <param name="left">Left operand</param>
        /// <param name="right">Right operand</param>
        /// <returns>True if the references are equal, false otherwise</returns>
        public static bool operator ==(ComponentRef<T> left, ComponentRef<T> right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for typed component references.
        /// </summary>
        /// <param name="left">Left operand</param>
        /// <param name="right">Right operand</param>
        /// <returns>True if the references are not equal, false otherwise</returns>
        public static bool operator !=(ComponentRef<T> left, ComponentRef<T> right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Implicit conversion from typed component reference to typeless component reference.
        /// </summary>
        /// <param name="obj">The typed component reference to convert</param>
        /// <returns>A typeless component reference</returns>
        public static implicit operator ComponentRef(ComponentRef<T> obj)
        {
            return obj.Untyped();
        }

        /// <summary>
        /// Explicit conversion from typeless component reference to typed component reference.
        /// </summary>
        /// <param name="obj">The typeless component reference to convert</param>
        /// <returns>A typed component reference</returns>
        public static explicit operator ComponentRef<T>(ComponentRef obj)
        {
            return obj.Typed<T>();
        }
    }
}