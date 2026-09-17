using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    /// <summary>
    /// Reentrancy regressions for component adds: a create handler that creates another
    /// component re-enters the component-created event, which the dispatch guard rejects
    /// with an <see cref="System.InvalidOperationException"/> (nested same-event dispatch
    /// is not allowed in v3).
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
        public void DenseAdd_NestedComponentCreate_ThrowsOnReentrantDispatch()
        {
            var entity = _world.CreateEntity();
            var created = default(Entity);
            _entityManager.OnEntityGotComp += (entityId, compType) =>
            {
                if (compType != typeof(Position) || created.IsValid) return;

                _world.DestroyEntity(entityId);
                created = _world.CreateEntity();
                created.CreateComponent<Position>();
            };

            Assert.Throws<InvalidOperationException>(() => entity.CreateComponent<Position>());
        }

        [Test]
        public void SparseAdd_NestedComponentCreate_ThrowsOnReentrantDispatch()
        {
            var entity = _world.CreateEntity();
            var created = default(Entity);
            _entityManager.OnEntityGotComp += (entityId, compType) =>
            {
                if (compType != typeof(Mana) || created.IsValid) return;

                _world.DestroyEntity(entityId);
                created = _world.CreateEntity();
                created.CreateComponent<Mana>();
            };

            Assert.Throws<InvalidOperationException>(() => entity.CreateComponent<Mana>());
        }

        [Test]
        public void DenseAdd_DifferentTypeNestedComponentCreate_ThrowsOnReentrantDispatch()
        {
            var entity = _world.CreateEntity();
            var created = default(Entity);
            _entityManager.OnEntityGotComp += (entityId, compType) =>
            {
                if (compType != typeof(Position) || created.IsValid) return;

                _world.DestroyEntity(entityId);
                created = _world.CreateEntity();
                created.CreateComponent<Velocity>();
            };

            Assert.Throws<InvalidOperationException>(() => entity.CreateComponent<Position>());
        }
    }
}
