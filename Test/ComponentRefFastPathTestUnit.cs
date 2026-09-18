using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentRefFastPathTestUnit
    {
        private World _world;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown() => _world?.Shutdown();

        private struct Position : IComponent<Position> { public int X; }
        private struct Velocity : IComponent<Velocity> { }
        private struct Health : IComponent<Health> { }
        private struct Mana : ISparseComponent<Mana> { public int Value; }

        [Test]
        public void Rw_AfterMigration_RecomputesSlotAndWritesLiveData()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            position.RW.X = 1;

            entity.CreateComponent<Velocity>();   // migration may change the Position slot
            position.RW.X = 9;
            entity.CreateComponent<Health>();     // migrate again

            Assert.AreEqual(9, entity.GetComponent<Position>().RW.X);
        }

        [Test]
        public void Core_CachesDenseSlotAfterFirstAccess()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            _ = position.RW.X;

            var structure = _world.GetManager<CoreECS.Managers.EntityManager>().Table
                .TryGetLocation(entity.EntityId, out var location) ? location.Structure : null;
            Assert.IsNotNull(structure);
            Assert.AreEqual(structure.IndexOfDense(ComponentTypeRegistry.GetOrRegister<Position>().TypeId),
                position.Core.CachedSlot);
        }

        [Test]
        public void Core_CachesSparseStoreAfterFirstAccess()
        {
            var entity = _world.CreateEntity();
            var mana = entity.CreateComponent(new Mana { Value = 7 });
            _ = mana.RO.Value;

            var structure = _world.GetManager<CoreECS.Managers.EntityManager>().Table
                .TryGetLocation(entity.EntityId, out var location) ? location.Structure : null;
            Assert.IsNotNull(structure);
            Assert.AreSame(structure.SparseOrNull.GetStore(ComponentTypeRegistry.GetOrRegister<Mana>().TypeId),
                mana.Core.CachedSparseStore);
        }

        [Test]
        public void Ro_And_Revision_UseFastPathWithoutBreakingSemantics()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            position.RW.X = 3;
            var revision = position.Revision;

            Assert.AreEqual(3, position.RO.X);
            Assert.AreEqual(revision, position.Revision);
        }
    }
}
