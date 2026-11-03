using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public sealed class CollectorLifecycleTestUnit
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
            if (m_world.Ready)
            {
                m_world.Shutdown();
            }
        }

        [Test]
        public void CollectorFactory_IsExposedOnlyThroughWorld()
        {
            bool hasPublicFactory = typeof(EntityMatchManager)
                .GetMethods(System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public)
                .Any(method => method.Name == "MakeCollector");

            Assert.That(hasPublicFactory, Is.False);
        }

        [Test]
        public void Dispose_IsIdempotentAndUnlinksOnlyDisposedCollector()
        {
            EntityCollector disposed = m_world.CreateCollector(
                EntityMatcher.With, EntityCollectorFlag.RevisionAsChange);
            EntityCollector remaining = m_world.CreateCollector(EntityMatcher.With);

            disposed.Dispose();
            disposed.Dispose();
            disposed.Dispose();

            Entity entity = m_world.CreateEntity();
            remaining.Flush();

            Assert.Multiple(() =>
            {
                Assert.That(m_matches.RegisteredCollectorCountForTesting, Is.EqualTo(1));
                Assert.That(remaining.Matching, Is.EqualTo(new[] { entity.EntityId }));
            });
        }

        [Test]
        public void Dispose_UnlinksStructuralRevisionWildcardAndLifecycleRegistrations()
        {
            EntityCollector collector = m_world.CreateCollector(
                EntityMatcher.With, EntityCollectorFlag.RevisionAsChange);

            Assert.That(m_matches.SubscriptionLinkCountForTesting, Is.EqualTo(3));

            collector.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(m_matches.RegisteredCollectorCountForTesting, Is.Zero);
                Assert.That(m_matches.SubscriptionLinkCountForTesting, Is.Zero);
            });
        }

        [Test]
        public void ExplicitDispose_MakesEveryMemberExceptDisposeThrowObjectDisposedException()
        {
            EntityCollector collector = m_world.CreateCollector(EntityMatcher.With);

            collector.Dispose();

            AssertDisposedMemberAccess(collector);
            Assert.DoesNotThrow(collector.Dispose);
        }

        [Test]
        public void WorldShutdown_InvalidatesLiveCollectorAndLaterDisposeIsNoOp()
        {
            EntityCollector collector = m_world.CreateCollector(EntityMatcher.With);

            m_world.Shutdown();

            AssertDisposedMemberAccess(collector);
            Assert.DoesNotThrow(collector.Dispose);
            Assert.DoesNotThrow(collector.Dispose);
        }

        [Test]
        public void EndTick_DoesNotPumpCollectorJournal()
        {
            Entity entity = m_world.CreateEntity();
            ComponentRef<Position> position = entity.CreateComponent<Position>();
            EntityCollector collector = m_world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(), EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();
            int pumpCount = m_matches.PumpCountForTesting;

            _ = position.RW;
            m_world.BeginTick();
            m_world.Tick();
            m_world.EndTick();

            Assert.That(m_matches.PumpCountForTesting, Is.EqualTo(pumpCount));
            Assert.That(collector.Changed, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyAndNoneOnlyCollectors_SeeBareEntityNetLifecycle(bool noneOnly)
        {
            EntityMatcher matcher = noneOnly
                ? EntityMatcher.With.OfNone<Position>()
                : EntityMatcher.With;
            EntityCollector collector = m_world.CreateCollector(
                matcher, EntityCollectorFlag.ClashAsChange);

            Entity entity = m_world.CreateEntity();
            collector.Flush();

            Assert.Multiple(() =>
            {
                Assert.That(collector.Matching, Is.EqualTo(new[] { entity.EntityId }));
                Assert.That(collector.Collected, Is.EqualTo(new[] { entity.EntityId }));
            });

            m_world.DestroyEntity(entity);
            collector.Flush();

            Assert.Multiple(() =>
            {
                Assert.That(collector.Matching, Is.Empty);
                Assert.That(collector.Clashing, Is.EqualTo(new[] { entity.EntityId }));
                Assert.That(collector.Collected, Is.Empty);
                Assert.That(collector.Changed, Is.EqualTo(new[] { entity.EntityId }));
            });
        }

        private static void AssertDisposedMemberAccess(EntityCollector collector)
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ObjectDisposedException>(() => _ = collector.Matcher);
                Assert.Throws<ObjectDisposedException>(() => _ = collector.Flag);
                Assert.Throws<ObjectDisposedException>(() => _ = collector.Collected);
                Assert.Throws<ObjectDisposedException>(() => _ = collector.Matching);
                Assert.Throws<ObjectDisposedException>(() => _ = collector.Clashing);
                Assert.Throws<ObjectDisposedException>(() => _ = collector.Changed);
                Assert.Throws<ObjectDisposedException>(collector.Flush);
            });
        }

        private struct Position : IComponent<Position>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }
    }
}
