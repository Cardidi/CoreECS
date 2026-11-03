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
        public void EntityMatcher_OfAll_CanMatchEntitiesWithAllComponents()
        {
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var entity3 = _world.CreateEntity();

            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            entity2.CreateComponent<VelocityComponent>();
            entity3.CreateComponent<VelocityComponent>();

            EntityMatcher matcher = EntityMatcher.With.OfAll<PositionComponent>();
            EntityQuery matchedEntities = _world.CreateQuery(matcher);

            Assert.That(matchedEntities, Has.Count.EqualTo(2));
            Assert.That(matchedEntities.Entities, Does.Contain(entity1.EntityId));
            Assert.That(matchedEntities.Entities, Does.Contain(entity2.EntityId));
            Assert.That(matchedEntities.Entities, Does.Not.Contain(entity3.EntityId));
        }

        [Test]
        public void EntityMatcher_OfAny_CanMatchEntitiesWithAnyComponent()
        {
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var entity3 = _world.CreateEntity();

            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<VelocityComponent>();
            entity3.CreateComponent<HealthComponent>();

            EntityMatcher matcher = EntityMatcher.With
                .OfAny<PositionComponent>()
                .OfAny<VelocityComponent>();
            EntityQuery matchedEntities = _world.CreateQuery(matcher);

            Assert.That(matchedEntities, Has.Count.EqualTo(2));
            Assert.That(matchedEntities.Entities, Does.Contain(entity1.EntityId));
            Assert.That(matchedEntities.Entities, Does.Contain(entity2.EntityId));
            Assert.That(matchedEntities.Entities, Does.Not.Contain(entity3.EntityId));
        }

        [Test]
        public void EntityMatcher_OfNone_CanExcludeEntitiesWithComponent()
        {
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var entity3 = _world.CreateEntity();

            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            entity2.CreateComponent<VelocityComponent>();
            entity3.CreateComponent<VelocityComponent>();

            EntityMatcher matcher = EntityMatcher.With
                .OfAll<PositionComponent>()
                .OfNone<VelocityComponent>();
            EntityQuery matchedEntities = _world.CreateQuery(matcher);

            Assert.That(matchedEntities.Entities, Is.EqualTo(new[] { entity1.EntityId }));
            Assert.That(matchedEntities.Entities, Does.Not.Contain(entity2.EntityId));
            Assert.That(matchedEntities.Entities, Does.Not.Contain(entity3.EntityId));
        }

        [Test]
        public void EntityMask_CanFilterEntitiesByMask()
        {
            var entity1 = _world.CreateEntity(0b0001);
            var entity2 = _world.CreateEntity(0b0010);
            var entity3 = _world.CreateEntity(0b0011);
            EntityMatcher matcher = EntityMatcher.WithMask(0b0001);
            EntityQuery matchedEntities = _world.CreateQuery(matcher);

            Assert.That(matchedEntities, Has.Count.EqualTo(2));
            Assert.That(matchedEntities.Entities, Does.Contain(entity1.EntityId));
            Assert.That(matchedEntities.Entities, Does.Not.Contain(entity2.EntityId));
            Assert.That(matchedEntities.Entities, Does.Contain(entity3.EntityId));
        }

        [Test]
        public void EntityMatcher_ComplexFiltering()
        {
            var entities = new List<Entity>();
            for (int i = 0; i < 20; i++)
            {
                Entity entity = _world.CreateEntity();
                if (i % 2 == 0) entity.CreateComponent<PositionComponent>();
                if (i % 3 == 0) entity.CreateComponent<VelocityComponent>();
                if (i % 5 == 0) entity.CreateComponent<HealthComponent>();
                entities.Add(entity);
            }

            EntityMatcher positionMatcher = EntityMatcher.With.OfAll<PositionComponent>();
            EntityMatcher positionOrVelocityMatcher = EntityMatcher.With
                .OfAny<PositionComponent>()
                .OfAny<VelocityComponent>();
            EntityMatcher positionWithoutHealthMatcher = EntityMatcher.With
                .OfAll<PositionComponent>()
                .OfNone<HealthComponent>();
            var positionEntities = new List<Entity>();
            var positionOrVelocityEntities = new List<Entity>();
            var positionWithoutHealthEntities = new List<Entity>();

            foreach (Entity entity in entities)
            {
                var components = new List<ComponentRef>();
                entity.GetComponents(components);
                IComponentRefCore[] cores = components.Select(component => component.Core).ToArray();
                if (positionMatcher.ComponentFilter(cores)) positionEntities.Add(entity);
                if (positionOrVelocityMatcher.ComponentFilter(cores)) positionOrVelocityEntities.Add(entity);
                if (positionWithoutHealthMatcher.ComponentFilter(cores)) positionWithoutHealthEntities.Add(entity);
            }

            Assert.That(positionEntities, Has.Count.EqualTo(10));
            Assert.That(positionOrVelocityEntities, Has.Count.EqualTo(13));
            Assert.That(positionWithoutHealthEntities, Has.Count.EqualTo(8));
        }

        [Test]
        public void EntityMatcher_CanHandleEmptyComponentList()
        {
            Entity entity = _world.CreateEntity();
            EntityMatcher matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var components = new List<ComponentRef>();
            entity.GetComponents(components);

            bool result = matcher.ComponentFilter(components.Select(component => component.Core).ToArray());

            Assert.That(result, Is.False);
        }

        [Test]
        public void EntityMatcher_CanHandleMultipleOfAny()
        {
            Entity entity1 = CreateEntityWith<PositionComponent>();
            Entity entity2 = CreateEntityWith<VelocityComponent>();
            Entity entity3 = CreateEntityWith<HealthComponent>();
            Entity entity4 = CreateEntityWith<PositionComponent, VelocityComponent>();
            EntityMatcher matcher = EntityMatcher.With
                .OfAny<PositionComponent>()
                .OfAny<VelocityComponent>()
                .OfAny<HealthComponent>();
            EntityQuery matchedEntities = _world.CreateQuery(matcher);

            Assert.That(matchedEntities.Entities, Is.EquivalentTo(new[]
                { entity1.EntityId, entity2.EntityId, entity3.EntityId, entity4.EntityId }));
        }

        [Test]
        public void EntityMatcher_CanHandleMultipleOfNone()
        {
            Entity position = CreateEntityWith<PositionComponent>();
            Entity velocity = CreateEntityWith<VelocityComponent>();
            Entity health = CreateEntityWith<HealthComponent>();
            Entity positionVelocity = CreateEntityWith<PositionComponent, VelocityComponent>();
            EntityMatcher matcher = EntityMatcher.With
                .OfNone<PositionComponent>()
                .OfNone<VelocityComponent>();
            EntityQuery matchedEntities = _world.CreateQuery(matcher);

            Assert.That(matchedEntities.Entities, Is.EqualTo(new[] { health.EntityId }));
            Assert.That(matchedEntities.Entities, Does.Not.Contain(position.EntityId));
            Assert.That(matchedEntities.Entities, Does.Not.Contain(velocity.EntityId));
            Assert.That(matchedEntities.Entities, Does.Not.Contain(positionVelocity.EntityId));
        }

        [Test]
        public void EntityMatcher_PropertyAccess_Getter()
        {
            EntityMatcher matcher = EntityMatcher.WithMask(0b1010);

            Assert.That(matcher.EntityMask, Is.EqualTo(0b1010));
        }

        [Test]
        public void EntityMatcher_ChainFilters_CorrectlyMatches()
        {
            _ = CreateEntityWith<PositionComponent>();
            _ = CreateEntityWith<PositionComponent, VelocityComponent>();
            _ = CreateEntityWith<PositionComponent, HealthComponent>();
            _ = CreateEntityWith<PositionComponent, VelocityComponent, HealthComponent>();
            EntityMatcher matcher = EntityMatcher.With
                .OfAll<PositionComponent>()
                .OfAny<VelocityComponent>()
                .OfAny<HealthComponent>()
                .OfNone<VelocityComponent>()
                .OfNone<HealthComponent>();
            EntityQuery matchedEntities = _world.CreateQuery(matcher);

            Assert.That(matchedEntities, Is.Empty);
        }

        [Test]
        public void EntityMatcher_WithProperty_Access()
        {
            EntityMatcher matcher = EntityMatcher.With;

            Assert.That(matcher.EntityMask, Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void EntityMatcher_MixedFilters_CombinedLogic()
        {
            _ = CreateEntityWith<PositionComponent>();
            Entity expected = CreateEntityWith<PositionComponent, VelocityComponent>();
            _ = CreateEntityWith<PositionComponent, HealthComponent>();
            _ = CreateEntityWith<PositionComponent, VelocityComponent, HealthComponent>();
            EntityMatcher matcher = EntityMatcher.With
                .OfAll<PositionComponent>()
                .OfAll<VelocityComponent>()
                .OfNone<HealthComponent>();
            EntityQuery matchedEntities = _world.CreateQuery(matcher);

            Assert.That(matchedEntities.Entities, Is.EqualTo(new[] { expected.EntityId }));
        }

        [Test]
        public void EntityMatcher_WithMask_CreatesCorrectMatcher()
        {
            const ulong expectedMask = 0b1100;
            EntityMatcher matcher = EntityMatcher.WithMask(expectedMask);

            Assert.That(matcher.EntityMask, Is.EqualTo(expectedMask));
        }

        [Test]
        public void EntityMatcher_Change_Existing_Component()
        {
            Entity entity = _world.CreateEntity();
            using EntityCollector collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.RevisionAsChange);

            collector.Flush();
            Assert.That(collector.Collected, Does.Not.Contain(entity.EntityId));

            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            Assert.That(collector.Collected, Does.Contain(entity.EntityId));

            _ = entity.GetComponent<PositionComponent>().RW;
            Assert.That(collector.Changed, Does.Not.Contain(entity.EntityId));
            collector.Flush();

            Assert.That(collector.Changed, Does.Contain(entity.EntityId));
            Assert.That(collector.Collected, Does.Contain(entity.EntityId));
        }

        [Test]
        public void EntityMatcher_IsRelevantComponent_CoversAllAnyNone()
        {
            EntityMatcher matcher = EntityMatcher.With
                .OfAll<PositionComponent>()
                .OfAny<VelocityComponent>()
                .OfNone<HealthComponent>();

            Assert.Multiple(() =>
            {
                Assert.That(matcher.IsRelevantComponent(typeof(PositionComponent)), Is.True);
                Assert.That(matcher.IsRelevantComponent(typeof(VelocityComponent)), Is.True);
                Assert.That(matcher.IsRelevantComponent(typeof(HealthComponent)), Is.True);
                Assert.That(matcher.IsRelevantComponent(typeof(UnrelatedTagComponent)), Is.False);
            });
        }

        [Test]
        public void EntityMatcher_IsRelevantComponent_EmptyMatcher_TreatsAllAsRelevant()
        {
            EntityMatcher matcher = EntityMatcher.With;

            Assert.Multiple(() =>
            {
                Assert.That(matcher.IsRelevantComponent(typeof(PositionComponent)), Is.True);
                Assert.That(matcher.IsRelevantComponent(typeof(VelocityComponent)), Is.True);
                Assert.That(matcher.IsRelevantComponent(typeof(HealthComponent)), Is.True);
                Assert.That(matcher.IsRelevantComponent(typeof(UnrelatedTagComponent)), Is.True);
            });
        }

        [Test]
        public void EntityMatcher_IsRelevantComponent_WithMaskNoConditions_TreatsAllAsRelevant()
        {
            EntityMatcher matcher = EntityMatcher.WithMask(0b1010);

            Assert.Multiple(() =>
            {
                Assert.That(matcher.IsRelevantComponent(typeof(PositionComponent)), Is.True);
                Assert.That(matcher.IsRelevantComponent(typeof(UnrelatedTagComponent)), Is.True);
            });
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
            EntityQuery actual = _world.CreateQuery(matcher);

            Assert.That(actual.Count, Is.EqualTo(2));
            Assert.That(actual.Entities,
                Is.EquivalentTo(new[] { positionVelocity.EntityId, positionHealth.EntityId }));
            Assert.That(actual.Entities, Does.Not.Contain(none.EntityId));
            Assert.That(actual.Entities, Does.Not.Contain(positionOnly.EntityId));
            Assert.That(actual.Entities, Does.Not.Contain(positionVelocityDisabled.EntityId));
        }

        [Test]
        public void EmptyMatcher_MatchesEveryLiveEntitySubjectToMask()
        {
            Entity bitZero = _world.CreateEntity(0b0001);
            Entity bitOne = _world.CreateEntity(0b0010);
            Entity both = _world.CreateEntity(0b0011);
            Entity destroyed = _world.CreateEntity(0b0001);
            _world.DestroyEntity(destroyed);
            EntityQuery allLive = _world.CreateQuery(EntityMatcher.With);
            EntityQuery masked = _world.CreateQuery(EntityMatcher.WithMask(0b0001));

            Assert.That(allLive.Entities,
                Is.EquivalentTo(new[] { bitZero.EntityId, bitOne.EntityId, both.EntityId }));
            Assert.That(masked.Entities, Is.EquivalentTo(new[] { bitZero.EntityId, both.EntityId }));
        }

        [Test]
        public void Mask_MatchesWhenAnyBitIntersects()
        {
            Entity lowBit = _world.CreateEntity(0b0001);
            Entity highBit = _world.CreateEntity(0b1000);
            Entity noIntersection = _world.CreateEntity(0b0100);
            EntityQuery actual = _world.CreateQuery(EntityMatcher.WithMask(0b1001));

            Assert.That(actual.Entities,
                Is.EquivalentTo(new[] { lowBit.EntityId, highBit.EntityId }));
            Assert.That(actual.Entities, Does.Not.Contain(noIntersection.EntityId));
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
            EntityQuery actual = _world.CreateQuery(matcher);

            Assert.That(actual.Entities, Is.EqualTo(new[] { expected.EntityId }));
        }

        [Test]
        public void FirstQueryBind_FreezesEveryMutatorIncludingDuplicates()
        {
            EntityMatcher matcher = EntityMatcher.With
                .OfAll<PositionComponent>()
                .OfAny<VelocityComponent>()
                .OfNone<DisabledComponent>();

            _world.CreateQuery(matcher, false);

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

            using EntityCollector collector = _world.CreateCollector(matcher);

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

        private struct UnrelatedTagComponent : ITagComponent<UnrelatedTagComponent>
        {
        }
    }
}
