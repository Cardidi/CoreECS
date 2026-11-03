using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityMatcherTestUnit
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
            _world.Shutdown();
        }

        [Test]
        public void AllAnyNone_ApplyTheirCombinedTruthTable()
        {
            Entity none = _world.CreateEntity();
            Entity positionOnly = CreateEntityWith<PositionComponent>();
            Entity positionVelocity = CreateEntityWith<PositionComponent, VelocityComponent>();
            Entity positionHealth = CreateEntityWith<PositionComponent, HealthComponent>();
            Entity positionVelocityDisabled =
                CreateEntityWith<PositionComponent, VelocityComponent, DisabledComponent>();
            _ = CreateEntityWith<VelocityComponent>();
            _ = CreateEntityWith<HealthComponent>();

            EntityMatcher matcher = EntityMatcher.With
                .OfAll<PositionComponent>()
                .OfAny<VelocityComponent>()
                .OfAny<HealthComponent>()
                .OfNone<DisabledComponent>();
            var actual = new List<Entity>();

            int added = _world.Query(matcher, actual);

            Assert.That(added, Is.EqualTo(2));
            Assert.That(actual, Is.EquivalentTo(new[] { positionVelocity, positionHealth }));
            Assert.That(actual, Does.Not.Contain(none));
            Assert.That(actual, Does.Not.Contain(positionOnly));
            Assert.That(actual, Does.Not.Contain(positionVelocityDisabled));
        }

        [Test]
        public void EmptyMatcher_MatchesEveryLiveEntitySubjectToMask()
        {
            Entity bitZero = _world.CreateEntity(0b0001);
            Entity bitOne = _world.CreateEntity(0b0010);
            Entity both = _world.CreateEntity(0b0011);
            Entity destroyed = _world.CreateEntity(0b0001);
            _world.DestroyEntity(destroyed);
            var allLive = new List<Entity>();
            var masked = new List<Entity>();

            _world.Query(EntityMatcher.With, allLive);
            _world.Query(EntityMatcher.WithMask(0b0001), masked);

            Assert.That(allLive, Is.EquivalentTo(new[] { bitZero, bitOne, both }));
            Assert.That(masked, Is.EquivalentTo(new[] { bitZero, both }));
        }

        [Test]
        public void Mask_MatchesWhenAnyBitIntersects()
        {
            Entity lowBit = _world.CreateEntity(0b0001);
            Entity highBit = _world.CreateEntity(0b1000);
            Entity noIntersection = _world.CreateEntity(0b0100);
            var actual = new List<Entity>();

            _world.Query(EntityMatcher.WithMask(0b1001), actual);

            Assert.That(actual, Is.EquivalentTo(new[] { lowBit, highBit }));
            Assert.That(actual, Does.Not.Contain(noIntersection));
        }

        [Test]
        public void DuplicateTermsBeforeBind_AreIdempotent()
        {
            Entity expected = CreateEntityWith<PositionComponent, VelocityComponent>();
            _ = CreateEntityWith<PositionComponent, DisabledComponent>();
            EntityMatcher matcher = EntityMatcher.With
                .OfAll<PositionComponent>()
                .OfAll<PositionComponent>()
                .OfAny<VelocityComponent>()
                .OfAny<VelocityComponent>()
                .OfNone<DisabledComponent>()
                .OfNone<DisabledComponent>();
            var actual = new List<Entity>();

            _world.Query(matcher, actual);

            Assert.That(actual, Is.EqualTo(new[] { expected }));
        }

        [Test]
        public void FirstQueryBind_FreezesEveryMutatorIncludingDuplicates()
        {
            EntityMatcher matcher = EntityMatcher.With
                .OfAll<PositionComponent>()
                .OfAny<VelocityComponent>()
                .OfNone<DisabledComponent>();

            _world.Query(matcher, new List<Entity>());

            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidOperationException>(() => matcher.OfAll<PositionComponent>());
                Assert.Throws<InvalidOperationException>(() => matcher.OfAny<VelocityComponent>());
                Assert.Throws<InvalidOperationException>(() => matcher.OfNone<DisabledComponent>());
                Assert.Throws<InvalidOperationException>(() => matcher.OfAll<HealthComponent>());
                Assert.Throws<InvalidOperationException>(() => matcher.OfAny<HealthComponent>());
                Assert.Throws<InvalidOperationException>(() => matcher.OfNone<HealthComponent>());
            });
        }

        [Test]
        public void FirstCollectorBind_FreezesMatcher()
        {
            EntityMatcher matcher = EntityMatcher.With.OfAll<PositionComponent>();

            using IEntityCollector collector = _world.CreateCollector(matcher);

            Assert.Throws<InvalidOperationException>(() => matcher.OfAll<VelocityComponent>());
        }

        [Test]
        public void RelevantComponent_IncludesAllAnyAndNoneTermsOnly()
        {
            EntityMatcher matcher = EntityMatcher.With
                .OfAll<PositionComponent>()
                .OfAny<VelocityComponent>()
                .OfNone<DisabledComponent>();

            Assert.Multiple(() =>
            {
                Assert.That(matcher.IsRelevantComponent(typeof(PositionComponent)), Is.True);
                Assert.That(matcher.IsRelevantComponent(typeof(VelocityComponent)), Is.True);
                Assert.That(matcher.IsRelevantComponent(typeof(DisabledComponent)), Is.True);
                Assert.That(matcher.IsRelevantComponent(typeof(HealthComponent)), Is.False);
            });
        }

        [Test]
        public void RelevantComponent_EmptyMatcherTreatsEveryTypeAsRelevant()
        {
            EntityMatcher matcher = EntityMatcher.With;

            Assert.Multiple(() =>
            {
                Assert.That(matcher.IsRelevantComponent(typeof(PositionComponent)), Is.True);
                Assert.That(matcher.IsRelevantComponent(typeof(HealthComponent)), Is.True);
            });
        }

        private Entity CreateEntityWith<T1>()
            where T1 : struct, IComponent<T1>
        {
            Entity entity = _world.CreateEntity();
            entity.CreateComponent<T1>();
            return entity;
        }

        private Entity CreateEntityWith<T1, T2>()
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
        {
            Entity entity = CreateEntityWith<T1>();
            entity.CreateComponent<T2>();
            return entity;
        }

        private Entity CreateEntityWith<T1, T2, T3>()
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
        {
            Entity entity = CreateEntityWith<T1, T2>();
            entity.CreateComponent<T3>();
            return entity;
        }

        private struct PositionComponent : IComponent<PositionComponent>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct VelocityComponent : IComponent<VelocityComponent>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct HealthComponent : IComponent<HealthComponent>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct DisabledComponent : ITagComponent<DisabledComponent>
        {
        }
    }
}
