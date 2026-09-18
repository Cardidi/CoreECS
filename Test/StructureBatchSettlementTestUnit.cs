using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class StructureBatchSettlementTestUnit
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

        [Test]
        public void RwSpan_MarksEveryCollectedRowOnceAfterFlush()
        {
            var entities = new List<Entity>();
            for (var i = 0; i < 3; i++)
            {
                var entity = _world.CreateEntity();
                entity.CreateComponent<Position>();
                entities.Add(entity);
            }

            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            var structure = _world.GetManager<CoreECS.Managers.EntityManager>().Table
                .TryGetLocation(entities[0].EntityId, out var location) ? location.Structure : null;
            Assert.IsNotNull(structure);

            var span = structure.GetReadWriteDenseColumn<Position>();
            for (var i = 0; i < span.Length; i++) span[i].X = i;

            collector.Flush();

            Assert.AreEqual(3, collector.Changed.Count);
            foreach (var entity in entities)
                CollectionAssert.Contains(collector.Changed, entity.EntityId);
        }
    }
}
