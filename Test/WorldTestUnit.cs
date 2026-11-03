using System.Reflection;
using CoreECS.Defines;
using CoreECS.Managers;
using Microsoft.Extensions.DependencyInjection;

namespace CoreECS.Test
{
    [TestFixture]
    public class WorldTestUnit
    {
        private World m_world;

        [SetUp]
        public void SetUp()
        {
            m_world = new World();
            m_world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            m_world?.Shutdown();
        }

        [Test]
        public void CreateAndGetEntity_UseFullEntityIdAndDefaultMask()
        {
            Entity created = m_world.CreateEntity();
            Entity found = m_world.GetEntity(created.EntityId);

            Assert.That(created.EntityId.IsValid, Is.True);
            Assert.That(created.Mask, Is.EqualTo(ulong.MaxValue));
            Assert.That(found, Is.EqualTo(created));
            Assert.That(m_world.GetEntity(default).IsValid, Is.False);
        }

        [Test]
        public void IWorld_ExposesEntityIdLifecycleOperations()
        {
            MethodInfo[] methods = typeof(IWorld).GetMethods();

            Assert.That(methods.Any(method => method.Name == nameof(World.CreateEntity) &&
                                             method.ReturnType == typeof(Entity)), Is.True);
            Assert.That(methods.Any(method => method.Name == nameof(World.GetEntity) &&
                                             method.GetParameters().Single().ParameterType == typeof(EntityId)), Is.True);
            Assert.That(methods.Any(method => method.Name == nameof(World.DestroyEntity) &&
                                             method.GetParameters().Single().ParameterType == typeof(EntityId)), Is.True);
            Assert.That(methods.Any(method => method.Name == nameof(World.DestroyEntity) &&
                                             method.GetParameters().Single().ParameterType == typeof(Entity)), Is.True);
        }

        [Test]
        public void DestroyEntity_ByHandleOrId_IsImmediateAndRepeatedCallsAreNoOp()
        {
            Entity byHandle = m_world.CreateEntity();
            Entity byId = m_world.CreateEntity();

            Assert.DoesNotThrow(() => m_world.DestroyEntity(byHandle));
            Assert.DoesNotThrow(() => m_world.DestroyEntity(byHandle));
            Assert.DoesNotThrow(() => m_world.DestroyEntity(byHandle.EntityId));
            Assert.DoesNotThrow(() => m_world.DestroyEntity(byId.EntityId));
            Assert.DoesNotThrow(() => m_world.DestroyEntity(byId.EntityId));
            Assert.DoesNotThrow(() => m_world.DestroyEntity(default(EntityId)));

            Assert.That(byHandle.IsValid, Is.False);
            Assert.That(byId.IsValid, Is.False);
        }

        [Test]
        public void DestroyEntity_ForeignWorldHandleDoesNotDestroyLocalSameId()
        {
            Entity local = m_world.CreateEntity();
            var otherWorld = new World();
            otherWorld.Startup();
            try
            {
                Entity foreign = otherWorld.CreateEntity();
                Assert.That(foreign.EntityId, Is.EqualTo(local.EntityId));

                m_world.DestroyEntity(foreign);

                Assert.That(local.IsValid, Is.True);
                Assert.That(foreign.IsValid, Is.True);
            }
            finally
            {
                otherWorld.Shutdown();
            }
        }

        [Test]
        public void DestroyEntity_DefersIndexReuseUntilEndTick()
        {
            Entity destroyed = m_world.CreateEntity();
            m_world.DestroyEntity(destroyed);

            Entity sameTick = m_world.CreateEntity();
            Assert.That(sameTick.EntityId.Index, Is.Not.EqualTo(destroyed.EntityId.Index));

            EndTick();
            Entity afterEndTick = m_world.CreateEntity();
            Assert.That(afterEndTick.EntityId.Index, Is.EqualTo(destroyed.EntityId.Index));
            Assert.That(afterEndTick.EntityId.Generation, Is.Not.EqualTo(destroyed.EntityId.Generation));
        }

        [Test]
        public void EndTick_CompactsDirtyStorageAndKeepsMovedReferenceStable()
        {
            Entity first = m_world.CreateEntity();
            Entity removedEntity = m_world.CreateEntity();
            Entity movedEntity = m_world.CreateEntity();
            first.CreateComponent(new Position { Value = 1 });
            ComponentRef<Position> removed = removedEntity.CreateComponent(new Position { Value = 2 });
            ComponentRef<Position> moved = movedEntity.CreateComponent(new Position { Value = 3 });
            int movedSlotBeforeRemove = moved.Handler.PackedOffset;

            m_world.BeginTick();
            m_world.Tick();
            removedEntity.DestroyComponent(removed);

            Assert.That(removed.NotNull, Is.False);
            Assert.That(moved.NotNull, Is.True);
            Assert.That(moved.Handler.PackedOffset, Is.EqualTo(movedSlotBeforeRemove));
            Assert.That(moved.Handler.PackedOffset, Is.EqualTo(2));

            m_world.EndTick();

            Assert.That(moved.NotNull, Is.True);
            Assert.That(moved.Handler.PackedOffset, Is.EqualTo(1));
            Assert.That(moved.EntityId, Is.EqualTo(movedEntity.EntityId));
            Assert.That(moved.RO.Value, Is.EqualTo(3));
        }

        [Test]
        public void EndTick_DoesNotPublishCollectorChanges()
        {
            Entity entity = m_world.CreateEntity();
            ComponentRef<Position> position = entity.CreateComponent<Position>();
            using EntityCollector collector = m_world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(), EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            Assert.That(collector.Changed, Is.Empty);

            _ = position.RW;
            EndTick();

            Assert.That(collector.Changed, Is.Empty);
            collector.Flush();
            Assert.That(collector.Changed, Is.EqualTo(new[] { entity.EntityId }));
        }

        [Test]
        public void EndTick_FinalizesSystemsBeforeCompactionAndEntityRelease()
        {
            var state = new EndTickOrderState();
            var world = new EndTickOrderWorld(state);
            world.Startup();
            try
            {
                Entity first = world.CreateEntity();
                Entity removedEntity = world.CreateEntity();
                Entity movedEntity = world.CreateEntity();
                first.CreateComponent(new Position { Value = 1 });
                ComponentRef<Position> removed = removedEntity.CreateComponent(
                    new Position { Value = 2 });
                state.Moved = movedEntity.CreateComponent(new Position { Value = 3 });
                state.RemovedEntity = removedEntity;

                world.BeginTick();
                world.Tick();
                world.EndTick();

                Assert.Multiple(() =>
                {
                    Assert.That(removed.NotNull, Is.False);
                    Assert.That(state.MovedOffsetAtSystemDestroy, Is.EqualTo(2));
                    Assert.That(state.EntityCreatedAtSystemDestroy.EntityId.Index,
                        Is.Not.EqualTo(removedEntity.EntityId.Index));
                    Assert.That(state.Moved.Handler.PackedOffset, Is.EqualTo(1));
                });

                Entity afterEndTick = world.CreateEntity();
                Assert.That(afterEndTick.EntityId.Index,
                    Is.EqualTo(removedEntity.EntityId.Index));
            }
            finally
            {
                world.Shutdown();
            }
        }

        [Test]
        public void World_LifecycleCallbacksRunInContractOrder()
        {
            var world = new LifecycleWorld();

            world.Startup();
            world.BeginTick();
            world.Tick();
            world.EndTick();
            world.Shutdown();

            Assert.That(world.Events, Is.EqualTo(new[]
            {
                "register",
                "setup",
                "cleanup",
            }));
        }

        [Test]
        public void WorldManager_ReceivesFullLifecycle()
        {
            var world = new ManagerLifecycleWorld();

            world.Startup();
            TestWorldManager manager = world.GetManager<TestWorldManager>();

            Assert.That(manager.Created, Is.True);
            Assert.That(manager.Started, Is.True);
            Assert.That(manager.Ended, Is.False);
            Assert.That(manager.Destroyed, Is.False);

            world.Shutdown();

            Assert.That(manager.Ended, Is.True);
            Assert.That(manager.Destroyed, Is.True);
        }

        [Test]
        public void DuplicateManagerRegistration_IsRejectedWithoutDuplicateLifecycleCalls()
        {
            var world = new DuplicateManagerWorld();

            world.Startup();
            TestWorldManager manager = world.GetManager<TestWorldManager>();

            Assert.That(manager.CreatedCount, Is.EqualTo(1));
            Assert.That(manager.StartedCount, Is.EqualTo(1));

            world.Shutdown();

            Assert.That(manager.EndedCount, Is.EqualTo(1));
            Assert.That(manager.DestroyedCount, Is.EqualTo(1));
        }

        private void EndTick()
        {
            m_world.BeginTick();
            m_world.Tick();
            m_world.EndTick();
        }

        private struct Position : IComponent<Position>
        {
            internal int Value;
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct Velocity : IComponent<Velocity>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private sealed class LifecycleWorld : World
        {
            internal List<string> Events { get; } = new List<string>();

            protected override void OnRegister(IManagerRegister managers,
                IServiceCollection services) => Events.Add("register");

            protected override void OnSetup() => Events.Add("setup");

            protected override void OnCleanup() => Events.Add("cleanup");
        }

        private sealed class ManagerLifecycleWorld : World
        {
            protected override void OnRegister(IManagerRegister managers,
                IServiceCollection services) =>
                managers.RegisterManager<IWorldManager, TestWorldManager>();
        }

        private sealed class DuplicateManagerWorld : World
        {
            protected override void OnRegister(IManagerRegister managers,
                IServiceCollection services)
            {
                managers.RegisterManager<TestWorldManager>();
                managers.RegisterManager<TestWorldManager>();
            }
        }

        private sealed class EndTickOrderWorld : World
        {
            private readonly EndTickOrderState m_state;

            internal EndTickOrderWorld(EndTickOrderState state)
            {
                m_state = state;
            }

            protected override void OnRegister(IManagerRegister managers,
                IServiceCollection services)
            {
                services.AddSingleton(m_state);
            }

            protected override void OnSetup()
            {
                RegisterSystem<EndTickOrderSystem>();
            }
        }

        private sealed class EndTickOrderState
        {
            internal Entity RemovedEntity;
            internal ComponentRef<Position> Moved;
            internal int MovedOffsetAtSystemDestroy;
            internal Entity EntityCreatedAtSystemDestroy;
        }

        private sealed class EndTickOrderSystem : ISystem
        {
            private readonly World m_world;
            private readonly EndTickOrderState m_state;

            public EndTickOrderSystem(IWorld world, EndTickOrderState state)
            {
                m_world = (World)world;
                m_state = state;
            }

            public void OnCreate() { }

            public void OnTick(ulong tickMask)
            {
                m_world.DestroyEntity(m_state.RemovedEntity);
                m_world.UnregisterSystem<EndTickOrderSystem>();
            }

            public void OnDestroy()
            {
                m_state.MovedOffsetAtSystemDestroy = m_state.Moved.Handler.PackedOffset;
                m_state.EntityCreatedAtSystemDestroy = m_world.CreateEntity();
            }
        }

        private sealed class TestWorldManager : IWorldManager
        {
            internal bool Created { get; private set; }
            internal bool Started { get; private set; }
            internal bool Ended { get; private set; }
            internal bool Destroyed { get; private set; }
            internal int CreatedCount { get; private set; }
            internal int StartedCount { get; private set; }
            internal int EndedCount { get; private set; }
            internal int DestroyedCount { get; private set; }

            public void OnManagerCreated()
            {
                Created = true;
                CreatedCount++;
            }

            public void OnWorldStarted()
            {
                Started = true;
                StartedCount++;
            }

            public void OnWorldEnded()
            {
                Ended = true;
                EndedCount++;
            }

            public void OnManagerDestroyed()
            {
                Destroyed = true;
                DestroyedCount++;
            }
        }

    }
}
