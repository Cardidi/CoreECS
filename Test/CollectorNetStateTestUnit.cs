using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public sealed class CollectorNetStateTestUnit
    {
        private World m_world = null!;

        [SetUp]
        public void SetUp()
        {
            m_world = new World();
            m_world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            m_world.Shutdown();
        }

        [Test]
        public void InitialMatchThenExitBeforeFirstFlush_PublishesNoMembershipOrChange()
        {
            Entity entity = m_world.CreateEntity();
            entity.CreateComponent<Position>();
            EntityCollector collector = m_world.CreateCollector(
                EntityMatcher.With.OfAll<Position>());

            entity.DestroyComponent<Position>();
            collector.Flush();

            AssertAllEmpty(collector);
        }

        [Test]
        public void UnmatchedAddRemoveBounce_PublishesNothing()
        {
            Entity entity = m_world.CreateEntity();
            EntityCollector collector = m_world.CreateCollector(
                EntityMatcher.With.OfAll<Position>());

            entity.CreateComponent<Position>();
            entity.DestroyComponent<Position>();
            collector.Flush();

            AssertAllEmpty(collector);
        }

        [Test]
        public void MatchedRemoveAddBounce_PreservesMembershipAndPublishesOnlyChanged()
        {
            Entity entity = m_world.CreateEntity();
            entity.CreateComponent<Position>();
            EntityCollector collector = m_world.CreateCollector(
                EntityMatcher.With.OfAll<Position>());
            collector.Flush();
            collector.Flush();

            entity.DestroyComponent<Position>();
            entity.CreateComponent<Position>();
            collector.Flush();

            Assert.That(collector.Matching, Is.Empty);
            Assert.That(collector.Clashing, Is.Empty);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void FinalStateTransitions_PublishOneNetEnterAndOneNetExit()
        {
            Entity entity = m_world.CreateEntity();
            EntityCollector collector = m_world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.MatchAsChange | EntityCollectorFlag.ClashAsChange);

            entity.CreateComponent<Position>();
            collector.Flush();
            AssertOnly(collector.Matching, entity.EntityId);
            Assert.That(collector.Clashing, Is.Empty);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);

            entity.DestroyComponent<Position>();
            collector.Flush();
            Assert.That(collector.Matching, Is.Empty);
            AssertOnly(collector.Clashing, entity.EntityId);
            Assert.That(collector.Collected, Is.Empty);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void RelevantStructuralBounce_WithNoneFlagsSuppressesChanged()
        {
            Entity entity = m_world.CreateEntity();
            entity.CreateComponent<Position>();
            EntityCollector collector = m_world.CreateCollector(
                EntityMatcher.With.OfNone<Blocked>(), EntityCollectorFlag.None);
            collector.Flush();
            collector.Flush();

            entity.CreateComponent<Blocked>();
            entity.DestroyComponent<Blocked>();
            collector.Flush();

            Assert.That(collector.Matching, Is.Empty);
            Assert.That(collector.Clashing, Is.Empty);
            AssertOnly(collector.Collected, entity.EntityId);
            Assert.That(collector.Changed, Is.Empty);
        }

        [Test]
        public void WriteThenExit_DropsRevisionChangeBecauseFinalStateDoesNotMatch()
        {
            Entity entity = m_world.CreateEntity();
            ComponentRef<Position> position = entity.CreateComponent<Position>();
            EntityCollector collector = m_world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(), EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            position.RW.Value = 1;
            entity.DestroyComponent<Position>();
            collector.Flush();

            Assert.That(collector.Matching, Is.Empty);
            AssertOnly(collector.Clashing, entity.EntityId);
            Assert.That(collector.Collected, Is.Empty);
            Assert.That(collector.Changed, Is.Empty);
        }

        [Test]
        public void WriteThenEnter_IncludesRevisionChangeWhenFinalStateMatches()
        {
            Entity entity = m_world.CreateEntity();
            ComponentRef<Position> position = entity.CreateComponent<Position>();
            EntityCollector collector = m_world.CreateCollector(
                EntityMatcher.With.OfAll<Position>().OfAll<Velocity>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();

            position.RW.Value = 1;
            entity.CreateComponent<Velocity>();
            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            Assert.That(collector.Clashing, Is.Empty);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void RepeatedReasonsForOnePendingEntity_EvaluateMatcherExactlyOnce()
        {
            Entity entity = m_world.CreateEntity();
            EntityCollector collector = m_world.CreateCollector(
                EntityMatcher.With.OfAll<Position>());

            entity.CreateComponent<Position>();
            entity.DestroyComponent<Position>();
            entity.CreateComponent<Position>();
            collector.Flush();

            Assert.That(collector.LastMatcherEvaluationCountForTesting, Is.EqualTo(1));
            AssertOnly(collector.Collected, entity.EntityId);
        }

        private static void AssertAllEmpty(EntityCollector collector)
        {
            Assert.That(collector.Matching, Is.Empty);
            Assert.That(collector.Clashing, Is.Empty);
            Assert.That(collector.Collected, Is.Empty);
            Assert.That(collector.Changed, Is.Empty);
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

        private struct Velocity : IComponent<Velocity>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct Blocked : IComponent<Blocked>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }
    }
}
