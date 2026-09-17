using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class CollectorSettlementDedupTestUnit
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

        [Test]
        public void Settlement_MultipleTypesSameEntity_ChangedContainsEntityOnce()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            entity.CreateComponent<Velocity>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>().OfAll<Velocity>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            entity.GetComponent<Position>().RW.X = 1;
            entity.GetComponent<Velocity>().RW.X = 2;
            collector.Flush();

            Assert.AreEqual(1, collector.Changed.Count);
            Assert.AreEqual(entity.EntityId, collector.Changed[0]);
        }

        [Test]
        public void Settlement_NextPhase_MarksAgainAfterFlush()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            entity.GetComponent<Position>().RW.X = 1;
            collector.Flush();
            Assert.AreEqual(1, collector.Changed.Count);

            entity.GetComponent<Position>().RW.X = 2;
            collector.Flush();
            Assert.AreEqual(1, collector.Changed.Count);
        }
    }
}
