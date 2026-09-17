using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;

namespace CoreECS.Test
{
    /// <summary>
    /// Reentrancy regressions around revision-change handlers that migrate, destroy or
    /// recreate entities while a write is being notified. The carried entity location
    /// and the post-notification re-resolution must stay anchored to the writer.
    /// </summary>
    [TestFixture]
    public class RevisionHandlerReentrancyTestUnit
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

        private const EntityCollectorFlag RevisionOnly =
            EntityCollectorFlag.RevisionAsChange | EntityCollectorFlag.RelatedComponentOnly;

        private IEntityCollector MakeRevisionCollector()
        {
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<Position>(), RevisionOnly);
            collector.Flush();
            collector.Flush();
            return collector;
        }

        [Test]
        public void MigrationHandler_ThenOtherEntityWrite_BothSettled()
        {
            var entities = new Entity[8];
            for (var i = 0; i < entities.Length; i++)
            {
                entities[i] = _world.CreateEntity();
                entities[i].CreateComponent<Position>().RW.X = i;
            }

            var collector = MakeRevisionCollector();

            // Subscribed to the component-level signal: its handlers run before the
            // observer reads the location, so a migration here must not re-anchor the
            // journal marker onto the row's new owner.
            var componentManager = _world.GetManager<ComponentManager>();
            componentManager.OnComponentChanged.Add((entityId, _) =>
            {
                if (entityId == entities[3].EntityId && !entities[3].HasComponent<Velocity>())
                    entities[3].CreateComponent<Velocity>();
            });

            entities[3].GetComponent<Position>().RW.X = 30;
            entities[7].GetComponent<Position>().RW.X = 70;
            collector.Flush();

            CollectionAssert.Contains(collector.Changed, entities[3].EntityId);
            CollectionAssert.Contains(collector.Changed, entities[7].EntityId);
        }

        [Test]
        public void SelfRemovingMigratingHandler_WritesLiveRow()
        {
            var target = _world.CreateEntity();
            var other = _world.CreateEntity();
            target.CreateComponent<Position>().RW.X = 1;
            other.CreateComponent<Position>().RW.X = 2;

            Signal<EntityChangeComponent>.SignalDisposal sub = default;
            sub = _entityManager.OnEntityChangeComp.Add((entityId, _) =>
            {
                if (entityId == target.EntityId && !target.HasComponent<Velocity>())
                    target.CreateComponent<Velocity>();
                sub.Dispose();
            });

            target.GetComponent<Position>().RW.X = 42;

            Assert.AreEqual(42, target.GetComponent<Position>().RW.X);
            Assert.AreEqual(2, other.GetComponent<Position>().RW.X);
        }

        [Test]
        public void DestroyAndRecreateInHandler_NewEntityWriteIsSettled()
        {
            var target = _world.CreateEntity();
            var targetRef = target.CreateComponent<Position>();
            targetRef.RW.X = 1;

            var collector = MakeRevisionCollector();

            var created = default(Entity);
            _entityManager.OnEntityChangeComp.Add((entityId, _) =>
            {
                if (entityId != target.EntityId || created.IsValid) return;

                _world.DestroyEntity(target.EntityId);
                created = _world.CreateEntity();
                created.CreateComponent<Position>();
            });

            Assert.Throws<NullReferenceException>(() => targetRef.RW.X = 42);

            created.GetComponent<Position>().RW.X = 50;
            collector.Flush();

            CollectionAssert.Contains(collector.Changed, created.EntityId);
        }
    }
}
