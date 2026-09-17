using System;
using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS
{
    /// <summary>
    /// Records entity and component commands for deferred application, so a batch of
    /// structural changes can be applied in one explicit <see cref="Playback"/>. Recording
    /// never touches the world: while commands are pending the entity table and structure
    /// registry are unchanged. The buffer is created by <see cref="World.CreateCommandBuffer"/>
    /// and is not thread-safe (worlds are single-threaded).
    /// </summary>
    public sealed class CommandBuffer : IDisposable
    {
        private delegate void CommandHandler(CommandBuffer buffer, Command command);

        private enum CommandKind
        {
            CreateEntity,
            CreateComponent,
            DestroyComponent,
            SetMask,
            DestroyEntity,
        }

        private struct Command
        {
            public CommandKind Kind;
            public Entity Target;
            public ulong Mask;
            public object Value;
            public CommandHandler Handler;
        }

        private static class Handlers<T> where T : struct, IComponent<T>
        {
            public static readonly CommandHandler CreateComponent =
                static (buffer, command) => command.Target.CreateComponent<T>();

            public static readonly CommandHandler CreateComponentWithValue =
                static (buffer, command) => command.Target.CreateComponent((T)command.Value);

            public static readonly CommandHandler DestroyComponent =
                static (buffer, command) => command.Target.DestroyComponent<T>();
        }

        private static readonly CommandHandler s_createEntityHandler =
            static (buffer, command) =>
            {
                var entity = buffer.m_world.CreateEntity(command.Mask);
                buffer.m_resolved[command.Target.EntityId] = entity;
            };

        private static readonly CommandHandler s_destroyEntityHandler =
            static (buffer, command) => buffer.m_world.DestroyEntity(command.Target);

        private static readonly CommandHandler s_setMaskHandler =
            static (buffer, command) => command.Target.SetMask(command.Mask);

        private static ulong s_nextPlaceholderId = 1UL << 63;

        private readonly List<Command> m_commands = new();
        private readonly HashSet<ulong> m_placeholders = new();
        private readonly Dictionary<ulong, Entity> m_resolved = new();
        private World m_world;
        private bool m_disposed;

        internal CommandBuffer(World world)
        {
            m_world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>
        /// Records the creation of an entity with the given mask and returns the placeholder
        /// handle used to address it in later commands of this buffer. The real entity is
        /// created when the buffer is played back; the placeholder is never a live entity.
        /// </summary>
        /// <param name="mask">Initial entity mask; defaults to <see cref="ulong.MaxValue"/>.</param>
        /// <returns>A buffer-local placeholder handle.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the buffer has been disposed.</exception>
        public Entity CreateEntity(ulong mask = ulong.MaxValue)
        {
            EnsureOpen();

            var placeholderId = s_nextPlaceholderId++;
            var placeholder = new Entity(m_world, placeholderId, null, 0);
            m_placeholders.Add(placeholderId);
            m_commands.Add(new Command
            {
                Kind = CommandKind.CreateEntity,
                Target = placeholder,
                Mask = mask,
                Handler = s_createEntityHandler,
            });

            return placeholder;
        }

        /// <summary>Records adding a default-valued component to the entity.</summary>
        /// <typeparam name="T">Component type to create.</typeparam>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void CreateComponent<T>(Entity entity) where T : struct, IComponent<T>
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.CreateComponent,
                Target = entity,
                Handler = Handlers<T>.CreateComponent,
            });
        }

        /// <summary>Records adding a component with an initial value to the entity.</summary>
        /// <typeparam name="T">Component type to create.</typeparam>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <param name="value">Initial component value.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void CreateComponent<T>(Entity entity, T value) where T : struct, IComponent<T>
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.CreateComponent,
                Target = entity,
                Value = value,
                Handler = Handlers<T>.CreateComponentWithValue,
            });
        }

        /// <summary>Records destroying the component of type <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">Component type to destroy.</typeparam>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void DestroyComponent<T>(Entity entity) where T : struct, IComponent<T>
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.DestroyComponent,
                Target = entity,
                Handler = Handlers<T>.DestroyComponent,
            });
        }

        /// <summary>
        /// Records changing the entity mask. The mask is part of the structure key, so
        /// playback migrates the entity into the structure with the new mask; dense data,
        /// discrete components and tags are preserved and no component hook runs.
        /// </summary>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <param name="mask">New entity mask.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void SetMask(Entity entity, ulong mask)
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.SetMask,
                Target = entity,
                Mask = mask,
                Handler = s_setMaskHandler,
            });
        }

        /// <summary>Records destroying the entity (placeholder or live handle).</summary>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void DestroyEntity(Entity entity)
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.DestroyEntity,
                Target = entity,
                Handler = s_destroyEntityHandler,
            });
        }

        /// <summary>
        /// Applies every recorded command to the world in recording order, then clears the
        /// buffer so it can be reused. Placeholder entities are resolved to the real entities
        /// created earlier in the same playback. Records are consumed even when a command
        /// throws: the exception propagates and the buffer stays reusable.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or a target entity is no longer valid.</exception>
        public void Playback()
        {
            EnsureOpen();

            try
            {
                for (var i = 0; i < m_commands.Count; i++)
                {
                    var command = m_commands[i];
                    if (command.Kind != CommandKind.CreateEntity)
                    {
                        command.Target = Resolve(command.Target);
                    }

                    command.Handler(this, command);
                }
            }
            finally
            {
                m_commands.Clear();
                m_placeholders.Clear();
                m_resolved.Clear();
            }
        }

        /// <summary>
        /// Discards all pending commands and releases the buffer. No recorded command is
        /// applied. Calling Dispose twice is a no-op; every other member throws afterwards.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed) return;

            m_disposed = true;
            m_commands.Clear();
            m_placeholders.Clear();
            m_resolved.Clear();
            m_world = null;
        }

        private void EnsureOpen()
        {
            if (m_disposed) throw new InvalidOperationException("CommandBuffer has been disposed.");
        }

        private void EnsureTarget(Entity entity)
        {
            if (m_placeholders.Contains(entity.EntityId)) return;
            if (entity.IsValid && ReferenceEquals(entity.World, m_world)) return;

            throw new InvalidOperationException("Entity is not valid for this command buffer.");
        }

        private Entity Resolve(Entity target)
        {
            if (target.IsValid) return target;
            if (m_resolved.TryGetValue(target.EntityId, out var resolved)) return resolved;

            throw new InvalidOperationException("CommandBuffer target entity is no longer valid.");
        }
    }
}
