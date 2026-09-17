using System;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;

namespace CoreECS
{
    /// <summary>
    /// Fluent registration handle for a system. Declares Before / After anchors relative
    /// to other systems or registered groups; anchors are resolved when the execution
    /// order is rebuilt, so forward references (targets registered later) are allowed.
    /// </summary>
    public sealed class SystemRegistration
    {
        /// <summary>Manager that owns the registered system.</summary>
        private readonly SystemManager m_manager;

        /// <summary>Registered system type.</summary>
        private readonly Type m_systemType;

        /// <summary>Creates a handle for a registered system.</summary>
        /// <param name="manager">Manager owning the system.</param>
        /// <param name="systemType">Registered system type.</param>
        internal SystemRegistration(SystemManager manager, Type systemType)
        {
            m_manager = manager;
            m_systemType = systemType;
        }

        /// <summary>Declares that this system runs before the specified system type.</summary>
        /// <typeparam name="T">Anchor system type; may be registered later.</typeparam>
        /// <returns>This handle for chaining.</returns>
        public SystemRegistration Before<T>() where T : class, ISystem
        {
            m_manager.AddSystemAnchor(m_systemType, new SystemAnchor(SystemAnchorKind.Before, typeof(T), null));
            return this;
        }

        /// <summary>Declares that this system runs after the specified system type.</summary>
        /// <typeparam name="T">Anchor system type; may be registered later.</typeparam>
        /// <returns>This handle for chaining.</returns>
        public SystemRegistration After<T>() where T : class, ISystem
        {
            m_manager.AddSystemAnchor(m_systemType, new SystemAnchor(SystemAnchorKind.After, typeof(T), null));
            return this;
        }

        /// <summary>Declares that this system runs before the specified group.</summary>
        /// <param name="groupName">Anchor group name; may be registered later.</param>
        /// <returns>This handle for chaining.</returns>
        public SystemRegistration Before(string groupName)
        {
            Assertion.ArgumentNotNull(groupName, nameof(groupName));
            m_manager.AddSystemAnchor(m_systemType, new SystemAnchor(SystemAnchorKind.Before, null, groupName));
            return this;
        }

        /// <summary>Declares that this system runs after the specified group.</summary>
        /// <param name="groupName">Anchor group name; may be registered later.</param>
        /// <returns>This handle for chaining.</returns>
        public SystemRegistration After(string groupName)
        {
            Assertion.ArgumentNotNull(groupName, nameof(groupName));
            m_manager.AddSystemAnchor(m_systemType, new SystemAnchor(SystemAnchorKind.After, null, groupName));
            return this;
        }
    }
}
