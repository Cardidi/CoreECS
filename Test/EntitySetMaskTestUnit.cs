using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntitySetMaskTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Mana : IDiscreteComponent<Mana>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private struct LifecycleDense : IComponent<LifecycleDense>
        {
            public int Value;

            public void OnCreate(ulong entityId) => CreateCount += 1;

            public void OnDestroy(ulong entityId) => DestroyCount += 1;

            public static int CreateCount;
            public static int DestroyCount;
        }

        private World m_world;

        [SetUp]
        public void SetUp()
        {
            LifecycleDense.CreateCount = 0;
            LifecycleDense.DestroyCount = 0;
            m_world = new World();
            m_world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            m_world?.Shutdown();
        }

        [Test]
        public void SetMask_MigratesEntityAndUpdatesMask()
        {
            var entity = m_world.CreateEntity(0b01);
            entity.CreateComponent(new Position { X = 7 });

            entity.SetMask(0b10);

            Assert.IsTrue(entity.IsValid);
            Assert.AreEqual(0b10UL, entity.Mask);
            Assert.AreEqual(7, entity.GetComponent<Position>().RO.X);
            Assert.AreEqual(0b10UL, m_world.GetManager<EntityManager>().GetEntity(entity.EntityId).Mask);
        }

        [Test]
        public void SetMask_PreservesDiscreteComponentsAndTags()
        {
            var entity = m_world.CreateEntity(0b01);
            entity.CreateComponent(new Mana { Value = 3 });
            entity.CreateComponent<PlayerTag>();

            entity.SetMask(0b10);

            Assert.IsTrue(entity.HasComponent<Mana>());
            Assert.AreEqual(3, entity.GetComponent<Mana>().RO.Value);
            Assert.IsTrue(entity.HasComponent<PlayerTag>());
            Assert.IsFalse(entity.GetComponent<PlayerTag>().NotNull);
        }

        [Test]
        public void SetMask_MovesRowToStructureWithNewMask()
        {
            var entity = m_world.CreateEntity(0b01);
            entity.CreateComponent(new Position { X = 1 });
            var table = m_world.GetManager<EntityManager>().Table;
            Assert.IsTrue(table.TryGetLocation(entity.EntityId, out var location));
            var original = location.Structure;

            entity.SetMask(0b10);

            Assert.AreNotSame(original, location.Structure);
            Assert.AreEqual(0b10UL, location.Structure.Mask);
            Assert.AreEqual(0b01UL, original.Mask);
            Assert.AreEqual(0, original.Count);
            Assert.AreEqual(1, location.Structure.Count);
        }

        [Test]
        public void SetMask_ToSameMask_DoesNotMigrate()
        {
            var entity = m_world.CreateEntity(0b01);
            var table = m_world.GetManager<EntityManager>().Table;
            Assert.IsTrue(table.TryGetLocation(entity.EntityId, out var location));
            var structure = location.Structure;

            entity.SetMask(0b01);

            Assert.AreSame(structure, location.Structure);
            Assert.IsNull(structure.SpareSetOrNull);
        }

        [Test]
        public void SetMask_DoesNotRunComponentLifecycleHooks()
        {
            var entity = m_world.CreateEntity();
            entity.CreateComponent(new LifecycleDense { Value = 1 });
            Assert.AreEqual(1, LifecycleDense.CreateCount);

            entity.SetMask(0b10);

            Assert.AreEqual(1, LifecycleDense.CreateCount);
            Assert.AreEqual(0, LifecycleDense.DestroyCount);
        }

        [Test]
        public void SetMask_KeepsExistingComponentRefsValid()
        {
            var entity = m_world.CreateEntity();
            var positionRef = entity.CreateComponent(new Position { X = 1 });

            entity.SetMask(0b10);

            Assert.IsTrue(positionRef.NotNull);
            Assert.AreEqual(1, positionRef.RO.X);
            positionRef.RW.X = 5;
            Assert.AreEqual(5, entity.GetComponent<Position>().RO.X);
        }

        [Test]
        public void SetMask_OnDestroyedEntity_Throws()
        {
            var entity = m_world.CreateEntity();
            m_world.DestroyEntity(entity);

            Assert.Throws<InvalidOperationException>(() => entity.SetMask(0b10));
        }

        [Test]
        public void SetMask_ChangesQueryVisibilityByMask()
        {
            var entity = m_world.CreateEntity(0b01);
            entity.CreateComponent<Position>();

            using var query = m_world.Query(EntityMatcher.WithMask(0b10).OfAll<Position>());
            query.Refresh();
            Assert.AreEqual(0, CountEntities(query));

            entity.SetMask(0b10);
            query.Refresh();

            Assert.AreEqual(1, CountEntities(query));
            foreach (var entityId in query.Entities)
            {
                Assert.AreEqual(entity.EntityId, entityId);
            }
        }

        private static int CountEntities(IEntityQuery query)
        {
            var count = 0;
            foreach (var _ in query.Entities)
            {
                count += 1;
            }

            return count;
        }
    }
}
