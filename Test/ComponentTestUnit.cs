using System;
using CoreECS.Defines;
using CoreECS.Managers;

#if COREECS_ULONG_ENTITY
using RevisionValue = System.UInt64;
#else
using RevisionValue = System.UInt32;
#endif

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentTestUnit
    {
        private World m_world;
        private ComponentManager m_components;
        private EntityManager m_entities;

        [SetUp]
        public void SetUp()
        {
            m_world = new World();
            m_world.Startup();
            m_components = m_world.GetManager<ComponentManager>();
            m_entities = m_world.GetManager<EntityManager>();
        }

        [TearDown]
        public void TearDown()
        {
            m_world?.Shutdown();
        }

        [Test]
        public void TypedAndUntypedConversions_PreserveIdentityAndValidateRuntimeType()
        {
            EntityId entityId = m_entities.Create(0);
            ComponentRef<Position> typed = m_components.Create(entityId, new Position { X = 7 });

            ComponentRef untyped = typed;
            ComponentRef<Position> roundTrip = untyped.Typed<Position>();

            Assert.That(untyped.NotNull, Is.True);
            Assert.That(untyped.EntityId, Is.EqualTo(entityId));
            Assert.That(untyped.Inspect<Position>(), Is.True);
            Assert.That(untyped.Inspect(typeof(Position)), Is.True);
            Assert.That(untyped.Inspect<Velocity>(), Is.False);
            Assert.That(roundTrip, Is.EqualTo(typed));
            Assert.That(roundTrip.RO.X, Is.EqualTo(7));
            Assert.Throws<InvalidCastException>(() => untyped.Typed<Velocity>());
        }

        [Test]
        public void RemovedAndReusedHandler_NeverRevivesCapturedReferenceOrEquality()
        {
            EntityId firstEntity = m_entities.Create(0);
            ComponentRef<Position> stale = m_components.Create(firstEntity, new Position { X = 1 });
            ComponentRef staleUntyped = stale;
            int staleHash = stale.GetHashCode();
            var staleHandler = stale.Handler;

            m_components.Destroy(stale);
            EntityId secondEntity = m_entities.Create(0);
            ComponentRef<Position> current = m_components.Create(secondEntity, new Position { X = 2 });

            Assert.That(current.Handler, Is.SameAs(staleHandler));
            Assert.That(stale.NotNull, Is.False);
            Assert.That(staleUntyped.NotNull, Is.False);
            Assert.That(stale.EntityId, Is.EqualTo(default(EntityId)));
            Assert.That(staleUntyped.EntityId, Is.EqualTo(default(EntityId)));
            Assert.That(stale, Is.Not.EqualTo(current));
            Assert.That(staleUntyped, Is.Not.EqualTo((ComponentRef)current));
            Assert.That(staleHash, Is.Not.EqualTo(current.GetHashCode()));
            Assert.Throws<InvalidOperationException>(() => _ = stale.RO.X);
            Assert.Throws<InvalidOperationException>(() => stale.Untyped());
        }

        [Test]
        public void ReadAndWriteAccess_ApplyRevisionRulesAndRawRefOnlyBumpsOnce()
        {
            EntityId entityId = m_entities.Create(0);
            ComponentRef<Position> component = m_components.Create(entityId, new Position { X = 1 });

            _ = component.RO.X;
            Assert.That(component.Revision, Is.Zero);

            _ = component.RW.X;
            Assert.That(component.Revision, Is.EqualTo((RevisionValue)1));
            _ = component.RW.X;
            Assert.That(component.Revision, Is.EqualTo((RevisionValue)2));

            ref Position cached = ref component.RW;
            Assert.That(component.Revision, Is.EqualTo((RevisionValue)3));
            cached.X = 9;
            cached.X = 10;
            Assert.That(component.Revision, Is.EqualTo((RevisionValue)3));
            Assert.That(component.RO.X, Is.EqualTo(10));
        }

        [Test]
        public void Revision_UsesConfiguredWidthAndWrapsWithoutZero()
        {
            EntityId entityId = m_entities.Create(0);
            ComponentRef<Position> component = m_components.Create<Position>(entityId);
#if COREECS_ULONG_ENTITY
            ulong compileTimeWidth = component.Revision;
#else
            uint compileTimeWidth = component.Revision;
#endif
            Assert.That(compileTimeWidth, Is.Zero);

            component.Handler.Revision = RevisionValue.MaxValue;
            _ = component.RW;

            Assert.That(component.Revision, Is.EqualTo((RevisionValue)1));
        }

        [Test]
        public void TagReference_UsesSharedDefaultButTracksRevisionPerEntity()
        {
            EntityId firstEntity = m_entities.Create(0);
            EntityId secondEntity = m_entities.Create(0);
            ComponentRef<Sleeping> first = m_components.Create<Sleeping>(firstEntity);
            ComponentRef<Sleeping> second = m_components.Create<Sleeping>(secondEntity);

            Assert.That(first.NotNull, Is.True);
            Assert.That(first.RO, Is.EqualTo(default(Sleeping)));
            _ = first.RW;
            _ = first.RW;

            Assert.That(first.Revision, Is.EqualTo((RevisionValue)2));
            Assert.That(second.Revision, Is.Zero);
            Assert.That(second.RO, Is.EqualTo(default(Sleeping)));
        }

        [Test]
        public void Compact_RelocatesHandlerWithoutInvalidatingExistingReference()
        {
            EntityId firstEntity = m_entities.Create(0);
            EntityId removedEntity = m_entities.Create(0);
            EntityId movedEntity = m_entities.Create(0);
            m_components.Create(firstEntity, new Position { X = 1 });
            ComponentRef<Position> removed = m_components.Create(removedEntity, new Position { X = 2 });
            ComponentRef<Position> moved = m_components.Create(movedEntity, new Position { X = 3 });
            int oldOffset = moved.Handler.PackedOffset;

            m_components.Destroy(removed);
            m_components.CompactDirtyStores();

            Assert.That(oldOffset, Is.EqualTo(2));
            Assert.That(moved.Handler.PackedOffset, Is.EqualTo(1));
            Assert.That(moved.NotNull, Is.True);
            Assert.That(moved.EntityId, Is.EqualTo(movedEntity));
            Assert.That(moved.RO.X, Is.EqualTo(3));
        }

        private struct Position : IComponent<Position>
        {
            internal int X;
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct Velocity : IComponent<Velocity>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct Sleeping : ITagComponent<Sleeping>
        {
        }
    }
}
