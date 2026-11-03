using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public sealed class CollectorPumpOrderingTestUnit
    {
        private World m_world = null!;
        private EntityMatchManager m_matches = null!;

        [SetUp]
        public void SetUp()
        {
            m_world = new World();
            m_world.Startup();
            m_matches = m_world.GetManager<EntityMatchManager>();
        }

        [TearDown]
        public void TearDown()
        {
            m_world.Shutdown();
        }

        [Test]
        public void ReverseFlushOrder_PumpsOnceGloballyAndOnlyCallerPublishes()
        {
            Entity entity = m_world.CreateEntity();
            ComponentRef<Position> position = entity.CreateComponent<Position>();
            EntityCollector first = CreateRevisionCollector();
            EntityCollector second = CreateRevisionCollector();
            EstablishAndClear(first, second);
            int pumpBaseline = m_matches.PumpCountForTesting;
            int entryBaseline = m_matches.PumpedEntryCountForTesting;

            position.RW.Value = 1;
            second.Flush();

            AssertOnly(second.Changed, entity.EntityId);
            Assert.That(first.Changed, Is.Empty);
            Assert.That(m_matches.PendingCountForTesting(first), Is.EqualTo(1));
            Assert.That(m_matches.PumpCountForTesting, Is.EqualTo(pumpBaseline + 1));
            Assert.That(m_matches.PumpedEntryCountForTesting, Is.EqualTo(entryBaseline + 1));

            first.Flush();

            AssertOnly(first.Changed, entity.EntityId);
            Assert.That(m_matches.PumpCountForTesting, Is.EqualTo(pumpBaseline + 2));
            Assert.That(m_matches.PumpedEntryCountForTesting, Is.EqualTo(entryBaseline + 1));
        }

        [Test]
        public void InterleavedWritesAndFlushes_PreservePerCollectorPhasesWithoutDuplicates()
        {
            Entity entity = m_world.CreateEntity();
            ComponentRef<Position> position = entity.CreateComponent<Position>();
            EntityCollector first = CreateRevisionCollector();
            EntityCollector second = CreateRevisionCollector();
            EstablishAndClear(first, second);
            int pumpBaseline = m_matches.PumpCountForTesting;
            int entryBaseline = m_matches.PumpedEntryCountForTesting;

            position.RW.Value = 1;
            first.Flush();
            AssertOnly(first.Changed, entity.EntityId);
            Assert.That(second.Changed, Is.Empty);

            position.RW.Value = 2;
            second.Flush();
            AssertOnly(second.Changed, entity.EntityId);
            AssertOnly(first.Changed, entity.EntityId);

            first.Flush();
            AssertOnly(first.Changed, entity.EntityId);

            second.Flush();
            Assert.That(second.Changed, Is.Empty);

            Assert.That(m_matches.PumpCountForTesting, Is.EqualTo(pumpBaseline + 4));
            Assert.That(m_matches.PumpedEntryCountForTesting, Is.EqualTo(entryBaseline + 2));
        }

        private EntityCollector CreateRevisionCollector()
        {
            return m_world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange | EntityCollectorFlag.RelatedComponentOnly);
        }

        private static void EstablishAndClear(
            EntityCollector first,
            EntityCollector second)
        {
            first.Flush();
            second.Flush();
            first.Flush();
            second.Flush();
        }

        private static void AssertOnly(IReadOnlyList<EntityId> actual, EntityId expected)
        {
            Assert.That(actual, Has.Count.EqualTo(1));
            Assert.That(actual[0], Is.EqualTo(expected));
        }

        private struct Position : IComponent<Position>
        {
            public int Value;
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }
    }
}
