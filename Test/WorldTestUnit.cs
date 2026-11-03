using System.Reflection;
using CoreECS.Defines;
using CoreECS.Managers;

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
            using IEntityCollector collector = m_world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(), EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            Assert.That(collector.Changed, Is.Empty);

            _ = position.RW;
            EndTick();

            Assert.That(collector.Changed, Is.Empty);
            collector.Flush();
            Assert.That(collector.Changed, Is.EqualTo(new[] { entity.EntityId }));
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
    }
}
