using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    /// <summary>
    /// Collector structure-level acceleration contract: the structure-level matcher result
    /// (mask + dense conditions) is evaluated once per structure and reused across its rows,
    /// while tag/sparse conditions stay row-level and collector semantics are unchanged.
    /// Kept separate from <see cref="EntityCollectorTestUnit"/> so the untouched fixture
    /// remains the parity baseline for the accelerated path.
    /// </summary>
    [TestFixture]
    public class CollectorAccelerationTestUnit
    {
        private World _world = null!;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
        }

        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Velocity : IComponent<Velocity>
        {
            public int Y;
        }

        private struct Mana : ISparseComponent<Mana>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        /// <summary>
        /// Third-party matcher that counts <see cref="IEntityMatcher.ComponentFilter"/> calls,
        /// proving the manager's non-<see cref="EntityMatcher"/> fallback stays uncached.
        /// </summary>
        private sealed class CountingPositionMatcher : IEntityMatcher
        {
            public int ComponentFilterCalls;

            public ulong EntityMask => ulong.MaxValue;

            public bool ComponentFilter(Structure structure, int row)
            {
                ComponentFilterCalls += 1;
                return structure.HasDense(ComponentTypeRegistry.GetOrRegister<Position>().TypeId);
            }

            public bool IsRelevantComponent(Type componentType) => true;
        }

        [Test]
        public void Collector_ReusesStructureLevelResultAcrossRowsOfSameStructure()
        {
            var first = _world.CreateEntity();
            var second = _world.CreateEntity();
            var third = _world.CreateEntity();
            first.CreateComponent<Position>();
            second.CreateComponent<Position>();
            third.CreateComponent<Position>();

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>();
            var collector = _world.CreateCollector(matcher);

            Assert.AreEqual(1, matcher.StructureEvaluationCount,
                "the init loop must evaluate the shared structure once");

            var fourth = _world.CreateEntity();
            fourth.CreateComponent<Position>();
            collector.Flush();

            Assert.AreEqual(1, matcher.StructureEvaluationCount,
                "a new row in a known structure must reuse the cached structure result");
            AssertOnly(collector.Matching, first.EntityId, second.EntityId, third.EntityId, fourth.EntityId);
            AssertOnly(collector.Collected, first.EntityId, second.EntityId, third.EntityId, fourth.EntityId);
        }

        [Test]
        public void Collector_RowLevelTagConditions_StillFilterPerRow()
        {
            var tagged = _world.CreateEntity();
            var plain = _world.CreateEntity();
            tagged.CreateComponent<Position>();
            tagged.CreateComponent<PlayerTag>();
            plain.CreateComponent<Position>();

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>().OfAll<PlayerTag>();
            var collector = _world.CreateCollector(matcher);
            collector.Flush();

            Assert.AreEqual(1, matcher.StructureEvaluationCount);
            AssertOnly(collector.Matching, tagged.EntityId);
            AssertOnly(collector.Collected, tagged.EntityId);

            collector.Flush();
            plain.CreateComponent<PlayerTag>();
            collector.Flush();

            Assert.AreEqual(1, matcher.StructureEvaluationCount,
                "tag changes must not re-evaluate the structure-level filter");
            AssertOnly(collector.Matching, plain.EntityId);
            AssertOnly(collector.Collected, tagged.EntityId, plain.EntityId);

            collector.Flush();
            tagged.DestroyComponent<PlayerTag>();
            collector.Flush();

            Assert.AreEqual(1, matcher.StructureEvaluationCount);
            AssertOnly(collector.Clashing, tagged.EntityId);
            AssertOnly(collector.Collected, plain.EntityId);
        }

        [Test]
        public void Collector_RowLevelSparseConditions_StillFilterPerRow()
        {
            var withMana = _world.CreateEntity();
            var withoutMana = _world.CreateEntity();
            withMana.CreateComponent<Position>();
            withMana.CreateComponent<Mana>();
            withoutMana.CreateComponent<Position>();

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>().OfAll<Mana>();
            var collector = _world.CreateCollector(matcher);
            collector.Flush();

            Assert.AreEqual(1, matcher.StructureEvaluationCount);
            AssertOnly(collector.Matching, withMana.EntityId);
            AssertOnly(collector.Collected, withMana.EntityId);

            collector.Flush();
            withoutMana.CreateComponent<Mana>();
            collector.Flush();

            Assert.AreEqual(1, matcher.StructureEvaluationCount,
                "sparse changes must not re-evaluate the structure-level filter");
            AssertOnly(collector.Matching, withoutMana.EntityId);
            AssertOnly(collector.Collected, withMana.EntityId, withoutMana.EntityId);
        }

        [Test]
        public void Collector_MixedKindAny_MatchesDenseOrRowLevelConditions()
        {
            var denseOnly = _world.CreateEntity();
            var tagOnly = _world.CreateEntity();
            var both = _world.CreateEntity();
            var neither = _world.CreateEntity();

            denseOnly.CreateComponent<Position>();
            tagOnly.CreateComponent<PlayerTag>();
            both.CreateComponent<Position>();
            both.CreateComponent<PlayerTag>();

            var matcher = (EntityMatcher)EntityMatcher.With.OfAny<Position>().OfAny<PlayerTag>();
            var collector = _world.CreateCollector(matcher);
            collector.Flush();

            Assert.AreEqual(2, matcher.StructureEvaluationCount,
                "the dense-only and dense-free structures are each evaluated once");
            AssertOnly(collector.Matching, denseOnly.EntityId, tagOnly.EntityId, both.EntityId);
            AssertOnly(collector.Collected, denseOnly.EntityId, tagOnly.EntityId, both.EntityId);
            Assert.IsFalse(collector.Collected.Contains(neither.EntityId));
        }

        [Test]
        public void Collector_NewStructure_IsEvaluatedOnceAndThenCached()
        {
            var first = _world.CreateEntity();
            first.CreateComponent<Position>();

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>();
            var collector = _world.CreateCollector(matcher);
            Assert.AreEqual(1, matcher.StructureEvaluationCount);

            var second = _world.CreateEntity();
            second.CreateComponent<Position>();
            second.CreateComponent<Velocity>();
            collector.Flush();

            Assert.AreEqual(2, matcher.StructureEvaluationCount,
                "a structure seen for the first time must be evaluated exactly once");
            AssertOnly(collector.Matching, first.EntityId, second.EntityId);

            var third = _world.CreateEntity();
            third.CreateComponent<Position>();
            third.CreateComponent<Velocity>();
            collector.Flush();

            Assert.AreEqual(2, matcher.StructureEvaluationCount,
                "rows of the new structure must reuse its cached result");
            AssertOnly(collector.Matching, third.EntityId);
            AssertOnly(collector.Collected, first.EntityId, second.EntityId, third.EntityId);
        }

        [Test]
        public void Collector_AcceleratedPath_PreservesMatchClashAndRevisionSemantics()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.Default);
            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);

            collector.Flush();
            ref var position = ref entity.GetComponent<Position>().RW;
            position.X = 1;
            collector.Flush();

            AssertOnly(collector.Changed, entity.EntityId);

            collector.Flush();
            _world.DestroyEntity(entity);
            collector.Flush();

            AssertEmpty(collector.Collected);
            AssertOnly(collector.Clashing, entity.EntityId);
        }

        [Test]
        public void Collector_ThirdPartyMatcher_UsesComponentFilterFallback()
        {
            var first = _world.CreateEntity();
            var second = _world.CreateEntity();
            first.CreateComponent<Position>();
            second.CreateComponent<Position>();

            var matcher = new CountingPositionMatcher();
            var collector = _world.CreateCollector(matcher);
            collector.Flush();

            Assert.AreEqual(2, matcher.ComponentFilterCalls,
                "a third-party matcher is evaluated once per entity without caching");
            AssertOnly(collector.Matching, first.EntityId, second.EntityId);

            collector.Flush();
            var third = _world.CreateEntity();
            third.CreateComponent<Position>();
            collector.Flush();

            Assert.AreEqual(3, matcher.ComponentFilterCalls);
            AssertOnly(collector.Collected, first.EntityId, second.EntityId, third.EntityId);
        }

        [Test]
        public void Collector_NonMatchingStructure_CachesTheFailureResult()
        {
            var matcher = (EntityMatcher)EntityMatcher.With.OfAny<Position>();
            var collector = _world.CreateCollector(matcher);
            var first = _world.CreateEntity();
            first.CreateComponent<Velocity>();
            collector.Flush();

            AssertEmpty(collector.Matching);
            Assert.AreEqual(1, matcher.StructureEvaluationCount);

            var second = _world.CreateEntity();
            second.CreateComponent<Velocity>();
            collector.Flush();

            AssertEmpty(collector.Matching);
            Assert.AreEqual(1, matcher.StructureEvaluationCount,
                "a cached failing structure-level result must not be re-evaluated");
        }

        [Test]
        public void Collector_CacheIsPerCollector_NotSharedAcrossCollectors()
        {
            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>();
            var firstCollector = _world.CreateCollector(matcher);
            var secondCollector = _world.CreateCollector(matcher);

            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            firstCollector.Flush();
            secondCollector.Flush();

            AssertOnly(firstCollector.Matching, entity.EntityId);
            AssertOnly(secondCollector.Matching, entity.EntityId);
            Assert.AreEqual(2, matcher.StructureEvaluationCount,
                "each collector owns its structure-level cache");

            var second = _world.CreateEntity();
            second.CreateComponent<Position>();
            firstCollector.Flush();
            secondCollector.Flush();

            Assert.AreEqual(2, matcher.StructureEvaluationCount,
                "both collectors reuse their own cached structure-level result");
        }

        private static void AssertEmpty(IReadOnlyList<ulong> actual)
        {
            Assert.AreEqual(0, actual.Count);
        }

        private static void AssertOnly(IReadOnlyList<ulong> actual, params ulong[] expectedIds)
        {
            Assert.AreEqual(expectedIds.Length, actual.Count);
            for (var i = 0; i < expectedIds.Length; i++)
            {
                var occurrences = 0;
                for (var j = 0; j < actual.Count; j++)
                {
                    if (actual[j] == expectedIds[i]) occurrences += 1;
                }

                Assert.AreEqual(1, occurrences, $"entity {expectedIds[i]} must appear exactly once");
            }
        }
    }
}
