using System;
using System.Collections.Generic;
using CoreECS.Utils;

namespace CoreECS.Managers
{
    /// <summary>
    /// Resolves the registration tree into a deterministic system execution order.
    /// </summary>
    internal sealed partial class SystemSchedule
    {
        /// <summary>Whether the tree or one of its anchor declarations changed.</summary>
        internal bool IsDirty { get; private set; }

        /// <summary>Number of execution-order rebuilds, exposed for cache diagnostics.</summary>
        internal int RebuildCount { get; private set; }

        /// <summary>Number of unique edges resolved by the most recent rebuild.</summary>
        internal int LastResolvedEdgeCount { get; private set; }

        internal void MarkDirty()
        {
            IsDirty = true;
        }

        /// <summary>
        /// Flattens the registration tree and applies all resolvable anchors with a stable
        /// topological sort. A cycle falls back the whole graph to flatten order.
        /// </summary>
        internal IReadOnlyList<Type> BuildExecutionOrder()
        {
            unchecked { RebuildCount++; }
            LastResolvedEdgeCount = 0;

            var flattened = new List<SystemEntryNode>();
            Flatten(Root, flattened);

            var indexes = new Dictionary<SystemEntryNode, int>(flattened.Count);
            for (int i = 0; i < flattened.Count; i++)
                indexes.Add(flattened[i], i);

            var indegrees = new int[flattened.Count];
            var successors = new HashSet<int>[flattened.Count];
            for (int i = 0; i < successors.Length; i++)
                successors[i] = new HashSet<int>();

            CollectConstraints(Root, indexes, indegrees, successors);

            var emitted = new bool[flattened.Count];
            var ordered = new List<SystemEntryNode>(flattened.Count);
            for (int step = 0; step < flattened.Count; step++)
            {
                int selected = -1;
                for (int i = 0; i < flattened.Count; i++)
                {
                    if (!emitted[i] && indegrees[i] == 0)
                    {
                        selected = i;
                        break;
                    }
                }

                if (selected < 0)
                {
                    IsDirty = false;
                    Log.Err("System execution order contains a cycle; falling back to " +
                        "registration order. Blocked systems: " +
                        BlockedSystemNames(flattened, emitted) + ".");
                    return ToTypes(flattened);
                }

                emitted[selected] = true;
                ordered.Add(flattened[selected]);
                foreach (int successor in successors[selected])
                    indegrees[successor]--;
            }

            IsDirty = false;
            return ToTypes(ordered);
        }

        private static void Flatten(SystemGroupNode group, List<SystemEntryNode> output)
        {
            for (int i = 0; i < group.Children.Count; i++)
            {
                SystemScheduleNode child = group.Children[i];
                if (child is SystemGroupNode childGroup)
                    Flatten(childGroup, output);
                else
                    output.Add((SystemEntryNode)child);
            }
        }

        private void CollectConstraints(SystemGroupNode group,
            Dictionary<SystemEntryNode, int> indexes, int[] indegrees,
            HashSet<int>[] successors)
        {
            ApplyAnchors(group, indexes, indegrees, successors);

            for (int i = 0; i < group.Children.Count; i++)
            {
                SystemScheduleNode child = group.Children[i];
                if (child is SystemGroupNode childGroup)
                    CollectConstraints(childGroup, indexes, indegrees, successors);
                else
                    ApplyAnchors(child, indexes, indegrees, successors);
            }
        }

        private void ApplyAnchors(SystemScheduleNode node,
            Dictionary<SystemEntryNode, int> indexes, int[] indegrees,
            HashSet<int>[] successors)
        {
            if (node.Anchors.Count == 0) return;

            var sources = new List<SystemEntryNode>();
            if (node is SystemEntryNode system)
                sources.Add(system);
            else
                Flatten((SystemGroupNode)node, sources);

            for (int i = 0; i < node.Anchors.Count; i++)
            {
                SystemAnchor anchor = node.Anchors[i];
                if (anchor.SystemType != null)
                {
                    SystemEntryNode? targetSystem = FindSystem(anchor.SystemType);
                    if (targetSystem == null)
                    {
                        Log.Err($"System schedule anchor target '{anchor.SystemType.FullName}' " +
                            "is not registered; constraint ignored.");
                        continue;
                    }

                    AddEdges(sources, targetSystem, anchor.Kind, indexes,
                        indegrees, successors);
                    continue;
                }

                SystemGroupNode? targetGroup = FindGroup(anchor.GroupName);
                if (targetGroup == null)
                {
                    Log.Err($"System schedule anchor target group '{anchor.GroupName}' " +
                        "is not registered; constraint ignored.");
                    continue;
                }

                var targets = new List<SystemEntryNode>();
                Flatten(targetGroup, targets);

                // A group constrained against itself or an ancestor is already a subset of
                // the target. Expanding it would create artificial mutual edges.
                if (node is SystemGroupNode && IsSubset(sources, targets)) continue;

                AddEdges(sources, targets, anchor.Kind, indexes, indegrees, successors);
            }
        }

        private void AddEdges(List<SystemEntryNode> sources, SystemEntryNode target,
            SystemAnchorKind kind, Dictionary<SystemEntryNode, int> indexes,
            int[] indegrees, HashSet<int>[] successors)
        {
            for (int i = 0; i < sources.Count; i++)
                AddEdge(sources[i], target, kind, indexes, indegrees, successors);
        }

        private void AddEdges(List<SystemEntryNode> sources, List<SystemEntryNode> targets,
            SystemAnchorKind kind, Dictionary<SystemEntryNode, int> indexes,
            int[] indegrees, HashSet<int>[] successors)
        {
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                {
                    AddEdge(sources[sourceIndex], targets[targetIndex], kind, indexes,
                        indegrees, successors);
                }
            }
        }

        private void AddEdge(SystemEntryNode source, SystemEntryNode target,
            SystemAnchorKind kind, Dictionary<SystemEntryNode, int> indexes,
            int[] indegrees, HashSet<int>[] successors)
        {
            if (ReferenceEquals(source, target)) return;

            int sourceIndex = indexes[source];
            int targetIndex = indexes[target];
            int from = kind == SystemAnchorKind.Before ? sourceIndex : targetIndex;
            int to = kind == SystemAnchorKind.Before ? targetIndex : sourceIndex;

            // Indegree changes only when the edge is new. This keeps overlapping group
            // expansions and repeated declarations from producing a false cycle.
            if (!successors[from].Add(to)) return;

            indegrees[to]++;
            LastResolvedEdgeCount++;
        }

        private static bool IsSubset(List<SystemEntryNode> subset,
            List<SystemEntryNode> superset)
        {
            for (int i = 0; i < subset.Count; i++)
            {
                if (!superset.Contains(subset[i])) return false;
            }

            return true;
        }

        private static IReadOnlyList<Type> ToTypes(List<SystemEntryNode> entries)
        {
            var types = new Type[entries.Count];
            for (int i = 0; i < entries.Count; i++)
                types[i] = entries[i].SystemType;
            return types;
        }

        private static string BlockedSystemNames(List<SystemEntryNode> flattened,
            bool[] emitted)
        {
            var names = new List<string>();
            for (int i = 0; i < flattened.Count; i++)
            {
                if (!emitted[i]) names.Add(flattened[i].SystemType.Name);
            }

            return string.Join(", ", names);
        }
    }
}
