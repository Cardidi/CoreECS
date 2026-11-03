using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public sealed class EntityQueryTestUnit
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
            if (m_world.Ready)
            {
                m_world.Shutdown();
            }
        }

        [Test]
        public void CreateQuery_DefaultRefreshMaterializesFullEntityIdsAndExposesConcreteContract()
        {
            Entity expected = CreateEntityWith<Position>();
            _ = m_world.CreateEntity();
            EntityMatcher matcher = EntityMatcher.With.OfAll<Position>();

            EntityQuery query = m_world.CreateQuery(matcher);

            Assert.Multiple(() =>
            {
                Assert.That(query.Matcher, Is.SameAs(matcher));
                Assert.That(query.Entities, Is.AssignableTo<IReadOnlyList<EntityId>>());
                Assert.That(query.Count, Is.EqualTo(1));
                Assert.That(query[0], Is.EqualTo(expected.EntityId));
                Assert.That(query.Entities, Is.EqualTo(new[] { expected.EntityId }));
                Assert.That(typeof(EntityQuery).IsSealed, Is.True);
                Assert.That(typeof(EntityQuery).GetInterfaces(), Is.Empty);
            });
        }

        [Test]
        public void CreateQuery_WithoutImmediateRefreshStartsEmptyAndReusesItsResultBuffer()
        {
            Entity expected = CreateEntityWith<Position>();
            var removed = new List<Entity>();
            for (int i = 0; i < 16; i++)
            {
                removed.Add(CreateEntityWith<Position>());
            }

            EntityQuery query = m_world.CreateQuery(EntityMatcher.With.OfAll<Position>(), false);
            IReadOnlyList<EntityId> entities = query.Entities;

            Assert.That(query.Count, Is.Zero);

            query.Refresh();
            int populatedCapacity = query.BufferCapacityForTesting;
            foreach (Entity entity in removed)
            {
                entity.DestroyComponent<Position>();
            }

            query.Refresh();

            Assert.Multiple(() =>
            {
                Assert.That(query.Entities, Is.SameAs(entities));
                Assert.That(entities, Is.EqualTo(new[] { expected.EntityId }));
                Assert.That(query.BufferCapacityForTesting, Is.EqualTo(populatedCapacity));
            });
        }

        [Test]
        public void Snapshot_RemainsStableUntilRefreshThenReplacesItsContents()
        {
            Entity removed = CreateEntityWith<Position>();
            EntityQuery query = m_world.CreateQuery(EntityMatcher.With.OfAll<Position>());
            IReadOnlyList<EntityId> entities = query.Entities;

            Entity added = CreateEntityWith<Position>();
            removed.DestroyComponent<Position>();

            Assert.That(entities, Is.EqualTo(new[] { removed.EntityId }));

            query.Refresh();

            Assert.Multiple(() =>
            {
                Assert.That(query.Entities, Is.SameAs(entities));
                Assert.That(entities, Is.EqualTo(new[] { added.EntityId }));
            });
        }

        [Test]
        public void Snapshot_DestroyedEntityKeepsOldGenerationAndNeverAliasesReusedIndex()
        {
            Entity destroyed = CreateEntityWith<Position>();
            EntityQuery query = m_world.CreateQuery(EntityMatcher.With.OfAll<Position>());
            EntityId staleId = query[0];

            m_world.DestroyEntity(destroyed);
            EndTick();
            Entity replacement = CreateEntityWith<Position>();

            Assert.Multiple(() =>
            {
                Assert.That(replacement.EntityId.Index, Is.EqualTo(staleId.Index));
                Assert.That(replacement.EntityId.Generation, Is.Not.EqualTo(staleId.Generation));
                Assert.That(query.Count, Is.EqualTo(1));
                Assert.That(query[0], Is.EqualTo(staleId));
                Assert.That(query[0], Is.Not.EqualTo(replacement.EntityId));
            });

            query.Refresh();

            Assert.That(query.Entities, Is.EqualTo(new[] { replacement.EntityId }));
        }

        [Test]
        public void Refresh_AfterWorldShutdownIsExplicitlyInvalid()
        {
            EntityQuery query = m_world.CreateQuery(EntityMatcher.With);
            m_world.Shutdown();

            InvalidOperationException? exception =
                Assert.Throws<InvalidOperationException>(() => query.Refresh());

            Assert.That(exception!.Message, Does.Contain("World"));
        }

        [Test]
        public void CreateQuery_RejectsNullMatcherAndWorldThatIsNotReady()
        {
            var inactive = new World();

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() => m_world.CreateQuery(null!));
                Assert.Throws<InvalidOperationException>(() =>
                    inactive.CreateQuery(EntityMatcher.With));
            });
        }

        [Test]
        public void World_DoesNotExposeLegacyCollectionQueryOverloads()
        {
            Assert.That(typeof(World).GetMethods()
                .Where(method => method.Name == "Query"), Is.Empty);
        }

        [Test]
        public void Planner_AllUsesSmallestRequiredStorageLiveCount()
        {
            Entity expected = CreateEntityWith<Common>();
            expected.CreateComponent<Rare>();
            for (int i = 0; i < 4; i++)
            {
                _ = CreateEntityWith<Common>();
                Entity rareTombstone = CreateEntityWith<Rare>();
                rareTombstone.DestroyComponent<Rare>();
            }

            EntityQuery query = m_world.CreateQuery(
                EntityMatcher.With.OfAll<Common>().OfAll<Rare>());

            Assert.Multiple(() =>
            {
                Assert.That(query.Entities, Is.EqualTo(new[] { expected.EntityId }));
                Assert.That(query.CandidateStrategyForTesting,
                    Is.EqualTo(EntityMatcherCandidateStrategy.SmallestAllStorage));
                Assert.That(query.CandidateCountForTesting, Is.EqualTo(1));
            });
        }

        [Test]
        public void Planner_AnyStorageUnionDeduplicatesEntitiesOwningMultipleTerms()
        {
            Entity common = CreateEntityWith<Common>();
            Entity rare = CreateEntityWith<Rare>();
            Entity both = CreateEntityWith<Common>();
            both.CreateComponent<Rare>();

            EntityQuery query = m_world.CreateQuery(
                EntityMatcher.With.OfAny<Common>().OfAny<Rare>());

            Assert.Multiple(() =>
            {
                Assert.That(query.Entities,
                    Is.EquivalentTo(new[] { common.EntityId, rare.EntityId, both.EntityId }));
                Assert.That(query.CandidateStrategyForTesting,
                    Is.EqualTo(EntityMatcherCandidateStrategy.AnyStorageUnion));
                Assert.That(query.CandidateCountForTesting, Is.EqualTo(3));
            });
        }

        [Test]
        public void Planner_AllAndAnyUsesAllCandidatesBeforeApplyingAnyCondition()
        {
            Entity matchingCommon = CreateEntityWith<Common>();
            matchingCommon.CreateComponent<Rare>();
            Entity matchingAlternate = CreateEntityWith<Common>();
            matchingAlternate.CreateComponent<Alternate>();
            _ = CreateEntityWith<Common>();
            _ = CreateEntityWith<Rare>();

            EntityQuery query = m_world.CreateQuery(EntityMatcher.With
                .OfAll<Common>()
                .OfAny<Rare>()
                .OfAny<Alternate>());

            Assert.Multiple(() =>
            {
                Assert.That(query.Entities,
                    Is.EquivalentTo(new[] { matchingCommon.EntityId, matchingAlternate.EntityId }));
                Assert.That(query.CandidateStrategyForTesting,
                    Is.EqualTo(EntityMatcherCandidateStrategy.SmallestAllStorage));
                Assert.That(query.CandidateCountForTesting, Is.EqualTo(3));
            });
        }

        [Test]
        public void Planner_NoneOnlyScansOnlyLiveEntityTableCandidates()
        {
            Entity included = m_world.CreateEntity();
            Entity excluded = CreateEntityWith<Excluded>();
            Entity destroyed = m_world.CreateEntity();
            m_world.DestroyEntity(destroyed);

            EntityQuery query = m_world.CreateQuery(EntityMatcher.With.OfNone<Excluded>());

            Assert.Multiple(() =>
            {
                Assert.That(query.Entities, Is.EqualTo(new[] { included.EntityId }));
                Assert.That(query.Entities, Does.Not.Contain(excluded.EntityId));
                Assert.That(query.CandidateStrategyForTesting,
                    Is.EqualTo(EntityMatcherCandidateStrategy.LiveEntityTable));
                Assert.That(query.CandidateCountForTesting, Is.EqualTo(2));
            });
        }

        [Test]
        public void Planner_EmptyMatcherScansEveryLiveEntity()
        {
            Entity first = m_world.CreateEntity();
            Entity second = CreateEntityWith<Common>();
            Entity destroyed = m_world.CreateEntity();
            m_world.DestroyEntity(destroyed);

            EntityQuery query = m_world.CreateQuery(EntityMatcher.With);

            Assert.Multiple(() =>
            {
                Assert.That(query.Entities, Is.EquivalentTo(new[] { first.EntityId, second.EntityId }));
                Assert.That(query.CandidateCountForTesting, Is.EqualTo(2));
                Assert.That(query.ComponentMatchCountForTesting, Is.EqualTo(2));
            });
        }

        [Test]
        public void Planner_AppliesMaskBeforeComponentMatching()
        {
            Entity expected = m_world.CreateEntity(0b0001);
            expected.CreateComponent<Common>();
            for (int i = 0; i < 3; i++)
            {
                Entity filtered = m_world.CreateEntity(0b0010);
                filtered.CreateComponent<Common>();
            }

            EntityQuery query = m_world.CreateQuery(
                EntityMatcher.WithMask(0b0001).OfAll<Common>());

            Assert.Multiple(() =>
            {
                Assert.That(query.Entities, Is.EqualTo(new[] { expected.EntityId }));
                Assert.That(query.CandidateCountForTesting, Is.EqualTo(4));
                Assert.That(query.ComponentMatchCountForTesting, Is.EqualTo(1));
            });
        }

        [Test]
        public void Planner_TombstonesDoNotInflateLiveCandidateCounts()
        {
            Entity live = CreateEntityWith<Common>();
            for (int i = 0; i < 4; i++)
            {
                Entity tombstone = CreateEntityWith<Common>();
                tombstone.DestroyComponent<Common>();
            }

            EntityQuery query = m_world.CreateQuery(EntityMatcher.With.OfAll<Common>());

            Assert.Multiple(() =>
            {
                Assert.That(query.Entities, Is.EqualTo(new[] { live.EntityId }));
                Assert.That(query.CandidateCountForTesting, Is.EqualTo(1));
            });
        }

        private Entity CreateEntityWith<T>() where T : struct, IComponent<T>
        {
            Entity entity = m_world.CreateEntity();
            entity.CreateComponent<T>();
            return entity;
        }

        private void EndTick()
        {
            m_world.BeginTick();
            m_world.Tick();
            m_world.EndTick();
        }

        private struct Position : IComponent<Position>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct Common : IComponent<Common>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct Rare : IComponent<Rare>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct Alternate : IComponent<Alternate>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct Excluded : ITagComponent<Excluded>
        {
        }
    }
}
