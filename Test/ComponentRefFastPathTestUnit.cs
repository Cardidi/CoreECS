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
        private struct Velocity : IComponent<Velocity> { public int X; }
        private struct Health : IComponent<Health> { public int X; }

        [Test]
        public void Rw_AfterMigration_RecomputesSlotAndWritesLiveData()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            position.RW.X = 1;

            entity.CreateComponent<Velocity>();   // 迁移，Position slot 可能变化
            position.RW.X = 9;
            entity.CreateComponent<Health>();     // 再次迁移

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
        public void Ro_And_Revision_UseFastPathWithoutBreakingSemantics()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            position.RW.X = 3;
            var revision = position.Revision;

            Assert.AreEqual(3, position.RO.X);
            Assert.GreaterOrEqual(position.Revision, revision);
        }
    }
}
