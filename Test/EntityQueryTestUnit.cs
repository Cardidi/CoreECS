using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityQueryTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Velocity : IComponent<Velocity>
        {
            public int Y;
        }

        private struct Health : IComponent<Health>
        {
            public int Value;
        }

        private struct Mana : ISparseComponent<Mana>
        {
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private World _world;

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

        [Test]
        public void Query_OnEmptyWorld_RefreshYieldsEmptySnapshot()
        {
            using var query = _world.CreateQuery(EntityMatcher.With.OfAll<Position>());
            query.Refresh();

            Assert.AreEqual(0, query.Entities.Count());
            Assert.AreEqual(0, query.Structures.Count);
        }

        [Test]
        public void Query_MatchesEntitiesAcrossMultipleStructures()
        {
            var onlyPosition = _world.CreateEntity();
            var positionAndVelocity = _world.CreateEntity();
            var positionAndHealth = _world.CreateEntity();
            var unrelated = _world.CreateEntity();

            onlyPosition.CreateComponent<Position>();
            positionAndVelocity.CreateComponent<Position>();
            positionAndVelocity.CreateComponent<Velocity>();
            positionAndHealth.CreateComponent<Position>();
            positionAndHealth.CreateComponent<Health>();
            unrelated.CreateComponent<Velocity>();

            using var query = _world.CreateQuery(EntityMatcher.With.OfAll<Position>());
            query.Refresh();

            var ids = query.Entities.ToList();
            Assert.AreEqual(3, ids.Count);
            CollectionAssert.Contains(ids, onlyPosition.EntityId);
            CollectionAssert.Contains(ids, positionAndVelocity.EntityId);
            CollectionAssert.Contains(ids, positionAndHealth.EntityId);
            CollectionAssert.DoesNotContain(ids, unrelated.EntityId);
            Assert.AreEqual(3, query.Structures.Count);
        }

        [Test]
        public void Query_Refresh_PicksUpNewEntitiesAndNewComponents()
        {
            using var query = _world.CreateQuery(EntityMatcher.With.OfAll<Position>());
            query.Refresh();
            Assert.AreEqual(0, query.Entities.Count());

            var late = _world.CreateEntity();
            query.Refresh();
            Assert.AreEqual(0, query.Entities.Count());

            late.CreateComponent<Position>();
            query.Refresh();
            Assert.AreEqual(1, query.Entities.Count());
            CollectionAssert.Contains(query.Entities.ToList(), late.EntityId);

            var createdAfter = _world.CreateEntity();
            createdAfter.CreateComponent<Position>();
            query.Refresh();
            Assert.AreEqual(2, query.Entities.Count());
        }

        [Test]
        public void Query_Refresh_DropsDestroyedAndNoLongerMatchingEntities()
        {
            var stays = _world.CreateEntity();
            var destroyed = _world.CreateEntity();
            var losesComponent = _world.CreateEntity();

            stays.CreateComponent<Position>();
            destroyed.CreateComponent<Position>();
            losesComponent.CreateComponent<Position>();
            losesComponent.CreateComponent<Velocity>();

            using var query = _world.CreateQuery(EntityMatcher.With.OfAll<Position>());
            query.Refresh();
            Assert.AreEqual(3, query.Entities.Count());

            _world.DestroyEntity(destroyed);
            losesComponent.DestroyComponent<Position>();
            query.Refresh();

            var ids = query.Entities.ToList();
            Assert.AreEqual(1, ids.Count);
            CollectionAssert.Contains(ids, stays.EntityId);
            CollectionAssert.DoesNotContain(ids, destroyed.EntityId);
            CollectionAssert.DoesNotContain(ids, losesComponent.EntityId);
        }

        [Test]
        public void Query_MaskFiltering_ExcludesNonIntersectingMasks()
        {
            var expected = _world.CreateEntity(0b0001);
            var wrongMask = _world.CreateEntity(0b0010);
            var overlapping = _world.CreateEntity(0b0011);

            expected.CreateComponent<Position>();
            wrongMask.CreateComponent<Position>();
            overlapping.CreateComponent<Position>();

            using var query = _world.CreateQuery(EntityMatcher.WithMask(0b0001).OfAll<Position>());
            query.Refresh();

            var ids = query.Entities.ToList();
            Assert.AreEqual(2, ids.Count);
            CollectionAssert.Contains(ids, expected.EntityId);
            CollectionAssert.Contains(ids, overlapping.EntityId);
            CollectionAssert.DoesNotContain(ids, wrongMask.EntityId);
        }

        [Test]
        public void Query_TagAndSparseFiltering_AreRowLevel()
        {
            var tagged = _world.CreateEntity();
            var plain = _world.CreateEntity();
            var withMana = _world.CreateEntity();
            var withoutMana = _world.CreateEntity();

            tagged.CreateComponent<PlayerTag>();
            withMana.CreateComponent<Mana>();

            using (var tagQuery = _world.CreateQuery(EntityMatcher.With.OfAll<PlayerTag>()))
            {
                tagQuery.Refresh();
                var ids = tagQuery.Entities.ToList();
                Assert.AreEqual(1, ids.Count);
                CollectionAssert.Contains(ids, tagged.EntityId);
                CollectionAssert.DoesNotContain(ids, plain.EntityId);
            }

            using (var manaQuery = _world.CreateQuery(EntityMatcher.With.OfAll<Mana>()))
            {
                manaQuery.Refresh();
                var ids = manaQuery.Entities.ToList();
                Assert.AreEqual(1, ids.Count);
                CollectionAssert.Contains(ids, withMana.EntityId);
                CollectionAssert.DoesNotContain(ids, withoutMana.EntityId);
            }
        }

        [Test]
        public void Query_Structures_AreDistinctAndOnlyContainMatchingEntities()
        {
            var first = _world.CreateEntity();
            var second = _world.CreateEntity();
            var third = _world.CreateEntity();
            var excluded = _world.CreateEntity();

            first.CreateComponent<Position>();
            second.CreateComponent<Position>();
            third.CreateComponent<Position>();
            third.CreateComponent<Velocity>();
            excluded.CreateComponent<Velocity>();

            using var query = _world.CreateQuery(EntityMatcher.With.OfAll<Position>());
            query.Refresh();

            Assert.AreEqual(2, query.Structures.Count);
            CollectionAssert.AllItemsAreUnique(query.Structures);

            var positionTypeId = ComponentTypeRegistry.GetOrRegister<Position>().TypeId;
            var matchedIds = query.Entities.ToList();
            foreach (var structure in query.Structures)
            {
                Assert.IsTrue(structure.HasDense(positionTypeId));

                var containsMatch = false;
                foreach (var entityId in structure.Entities)
                {
                    if (matchedIds.Contains(entityId))
                    {
                        containsMatch = true;
                        break;
                    }
                }

                Assert.IsTrue(containsMatch, "Structure must contain at least one matching entity.");
            }
        }

        [Test]
        public void Query_SnapshotIsStableUntilRefresh()
        {
            var first = _world.CreateEntity();
            first.CreateComponent<Position>();

            using var query = _world.CreateQuery(EntityMatcher.With.OfAll<Position>());
            query.Refresh();
            var snapshot = query.Entities.ToList();
            Assert.AreEqual(1, snapshot.Count);

            var second = _world.CreateEntity();
            second.CreateComponent<Position>();
            _world.DestroyEntity(first);

            CollectionAssert.AreEquivalent(snapshot, query.Entities.ToList());
            CollectionAssert.Contains(query.Entities.ToList(), first.EntityId);
            CollectionAssert.DoesNotContain(query.Entities.ToList(), second.EntityId);

            query.Refresh();
            Assert.AreEqual(1, query.Entities.Count());
            CollectionAssert.DoesNotContain(query.Entities.ToList(), first.EntityId);
            CollectionAssert.Contains(query.Entities.ToList(), second.EntityId);
        }

        [Test]
        public void Query_Dispose_IsNoOp()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();

            var query = _world.CreateQuery(EntityMatcher.With.OfAll<Position>());
            query.Refresh();
            query.Dispose();
            query.Dispose();

            var ids = query.Entities.ToList();
            Assert.AreEqual(1, ids.Count);
            CollectionAssert.Contains(ids, entity.EntityId);
        }

        [Test]
        public void Query_Matcher_ReturnsSameInstance()
        {
            var matcher = EntityMatcher.With.OfAll<Position>().OfNone<Velocity>();

            using var query = _world.CreateQuery(matcher);

            Assert.AreSame(matcher, query.Matcher);
        }

        [Test]
        public void Query_BeforeRefresh_SnapshotIsEmpty()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();

            using var query = _world.CreateQuery(EntityMatcher.With.OfAll<Position>());

            Assert.AreEqual(0, query.Structures.Count);
            Assert.AreEqual(0, query.Entities.ToList().Count);

            query.Refresh();

            CollectionAssert.Contains(query.Entities.ToList(), entity.EntityId);
        }
    }
}
