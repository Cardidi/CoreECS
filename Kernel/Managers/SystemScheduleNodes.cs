using System;
using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS.Managers
{
    /// <summary>
    /// Owns the registration tree and its global group and system lookups.
    /// Ordering constraints are declarations on nodes and are resolved by the DAG layer.
    /// </summary>
    internal sealed partial class SystemSchedule
    {
        private readonly Dictionary<string, SystemGroupNode> m_groups =
            new Dictionary<string, SystemGroupNode>();

        private readonly Dictionary<Type, SystemEntryNode> m_systems =
            new Dictionary<Type, SystemEntryNode>();

        internal SystemGroupNode Root { get; } = new SystemGroupNode(null, null);

        internal SystemGroupNode? FindGroup(string? name)
        {
            if (name == null) return null;
            return m_groups.TryGetValue(name, out SystemGroupNode? group) ? group : null;
        }

        internal SystemEntryNode? FindSystem(Type? systemType)
        {
            if (systemType == null) return null;
            return m_systems.TryGetValue(systemType, out SystemEntryNode? system) ? system : null;
        }

        internal SystemGroupNode AddGroup(string name, SystemGroupNode parent, GroupInsertMode mode)
        {
            var group = new SystemGroupNode(name, parent);
            if (mode == GroupInsertMode.Early)
                parent.Children.Insert(0, group);
            else
                parent.Children.Add(group);

            m_groups.Add(name, group);
            MarkDirty();
            return group;
        }

        internal SystemEntryNode AddSystem(Type systemType, SystemGroupNode parent)
        {
            var system = new SystemEntryNode(systemType, parent);
            parent.Children.Add(system);
            m_systems.Add(systemType, system);
            MarkDirty();
            return system;
        }

        internal void RemoveSystem(Type systemType)
        {
            if (!m_systems.TryGetValue(systemType, out SystemEntryNode? system)) return;

            m_systems.Remove(systemType);
            system.Parent!.Children.Remove(system);
            MarkDirty();
        }

        /// <summary>
        /// Clears every system, group, anchor, and root child so a world restart
        /// starts from an empty schedule.
        /// </summary>
        internal void ClearForRestart()
        {
            m_systems.Clear();
            m_groups.Clear();
            Root.Children.Clear();
            MarkDirty();
        }
    }

    /// <summary>A group or system declaration in the registration tree.</summary>
    internal abstract class SystemScheduleNode
    {
        internal SystemGroupNode? Parent { get; }
        internal List<SystemAnchor> Anchors { get; } = new List<SystemAnchor>();

        protected SystemScheduleNode(SystemGroupNode? parent)
        {
            Parent = parent;
        }
    }

    /// <summary>A named sort bucket, or the unnamed implicit root.</summary>
    internal sealed class SystemGroupNode : SystemScheduleNode
    {
        internal string? Name { get; }
        internal List<SystemScheduleNode> Children { get; } = new List<SystemScheduleNode>();

        internal SystemGroupNode(string? name, SystemGroupNode? parent) : base(parent)
        {
            Name = name;
        }
    }

    /// <summary>A system type leaf in the registration tree.</summary>
    internal sealed class SystemEntryNode : SystemScheduleNode
    {
        internal Type SystemType { get; }

        internal SystemEntryNode(Type systemType, SystemGroupNode parent) : base(parent)
        {
            SystemType = systemType;
        }
    }

    internal enum SystemAnchorKind
    {
        Before = 0,
        After = 1,
    }

    /// <summary>
    /// An unresolved ordering declaration. Exactly one target field is populated.
    /// </summary>
    internal readonly struct SystemAnchor
    {
        internal SystemAnchorKind Kind { get; }
        internal Type? SystemType { get; }
        internal string? GroupName { get; }

        internal SystemAnchor(SystemAnchorKind kind, Type? systemType, string? groupName)
        {
            Kind = kind;
            SystemType = systemType;
            GroupName = groupName;
        }
    }
}
