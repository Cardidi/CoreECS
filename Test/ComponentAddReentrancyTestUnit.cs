using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    /// <summary>
    /// Reentrancy regressions for component adds: a create handler may destroy the entity
    /// and add a component elsewhere, recycling the pooled core that the outer add is
    /// about to hand back. The outer handle must be dead instead of aliasing the rebound
    /// core (same type or a different one).
    /// </summary>
    [TestFixture]
    public class ComponentAddReentrancyTestUnit
    {
        private World _world;
        private EntityManager _entityManager;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
            _entityManager = _world.GetManager<EntityManager>();
        }

        [TearDown]
        public void TearDown() => _world?.Shutdown();

        private struct Position : IComponent<Position> { public int X; }

        private struct Velocity : IComponent<Velocity> { public int X; }

        private struct Mana : ISparseComponent<Mana> { public int Value; }

        [Test]
        public void DenseAdd_HandlerDestroysAndRecreatesSameType_ReturnedHandleIsDead()
        {
            var entity = _world.CreateEntity();
            var created = default(Entity);
            _entityManager.OnEntityGotComp.Add((entityId, compType) =>
            {
                if (compType != typeof(Position) || created.IsValid) return;

                _world.DestroyEntity(entityId);
                created = _world.CreateEntity();
                created.CreateComponent<Position>();
            });

            var handle = entity.CreateComponent<Position>();

            Assert.IsFalse(handle.NotNull,
                "the outer add must not return a live handle to a core rebound by a nested add");
            Assert.AreNotEqual(created.EntityId, handle.EntityId);
        }

        [Test]
        public void SparseAdd_HandlerDestroysAndRecreatesSameType_ReturnedHandleIsDead()
        {
            var entity = _world.CreateEntity();
            var created = default(Entity);
            _entityManager.OnEntityGotComp.Add((entityId, compType) =>
            {
                if (compType != typeof(Mana) || created.IsValid) return;

                _world.DestroyEntity(entityId);
                created = _world.CreateEntity();
                created.CreateComponent<Mana>();
            });

            var handle = entity.CreateComponent<Mana>();

            Assert.IsFalse(handle.NotNull,
                "the outer add must not return a live handle to a core rebound by a nested add");
            Assert.AreNotEqual(created.EntityId, handle.EntityId);
        }

        [Test]
        public void DenseAdd_HandlerDestroysAndRecreatesDifferentType_ReturnedHandleIsDead()
        {
            var entity = _world.CreateEntity();
            var created = default(Entity);
            _entityManager.OnEntityGotComp.Add((entityId, compType) =>
            {
                if (compType != typeof(Position) || created.IsValid) return;

                _world.DestroyEntity(entityId);
                created = _world.CreateEntity();
                created.CreateComponent<Velocity>();
            });

            var handle = entity.CreateComponent<Position>();

            Assert.IsFalse(handle.NotNull,
                "a rebound core of another component type must not surface as a live handle");
        }
    }
}
