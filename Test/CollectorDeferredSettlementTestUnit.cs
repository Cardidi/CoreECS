using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class CollectorDeferredSettlementTestUnit
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
        private struct Mana : ISparseComponent<Mana> { public int Value; }

        [Test]
        public void Settlement_SameEntitySameType_CoalescesToOneChangedEntry()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            var position = entity.GetComponent<Position>();
            for (var i = 0; i < 100; i++) position.RW.X = i;
            collector.Flush();

            Assert.AreEqual(1, collector.Changed.Count);
            Assert.AreEqual(entity.EntityId, collector.Changed[0]);
        }

        [Test]
        public void Settlement_CollectorCreatedAfterWrite_DoesNotMark()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();

            // An existing collector activates journaling before the write.
            var first = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            first.Flush();
            first.Flush();

            position.RW.X = 1;

            var late = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            late.Flush();
            first.Flush();

            Assert.AreEqual(0, late.Changed.Count, "a collector must not settle writes that predate its creation");
            Assert.AreEqual(1, first.Changed.Count, "the existing collector still sees the write");
        }

        [Test]
        public void Settlement_DestroyBeforeFlush_DropsRevisionChanged()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            entity.GetComponent<Position>().RW.X = 1;
            _world.DestroyEntity(entity);
            collector.Flush();

            Assert.AreEqual(0, collector.Changed.Count);
            Assert.AreEqual(1, collector.Clashing.Count);
        }

        [Test]
        public void Settlement_RelevantRevisionMarks_IrrelevantDoesNot()
        {
            const EntityCollectorFlag flags =
                EntityCollectorFlag.RevisionAsChange | EntityCollectorFlag.RelatedComponentOnly;

            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            entity.CreateComponent<Velocity>();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<Position>(), flags);
            collector.Flush();
            collector.Flush();

            entity.GetComponent<Velocity>().RW.X = 1;
            collector.Flush();
            Assert.AreEqual(0, collector.Changed.Count);

            entity.GetComponent<Position>().RW.X = 2;
            collector.Flush();
            Assert.AreEqual(1, collector.Changed.Count);
            Assert.AreEqual(entity.EntityId, collector.Changed[0]);
        }

        [Test]
        public void Settlement_MigratedBeforeFlush_UsesLiveStructure()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            entity.GetComponent<Position>().RW.X = 1;
            entity.CreateComponent<Velocity>();   // 迁移，但仍匹配
            collector.Flush();

            Assert.AreEqual(1, collector.Changed.Count);
        }
    }
}
