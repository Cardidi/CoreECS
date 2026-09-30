using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityCollectorTestUnit
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

        [Test]
        public void EntityCollectorFlag_Masks_RemainStable()
        {
            Assert.AreEqual(0, (int)EntityCollectorFlag.None);
            Assert.AreEqual(1 << 0, (int)EntityCollectorFlag.RevisionAsChange);
            Assert.AreEqual(1 << 1, (int)EntityCollectorFlag.MatchAsChange);
            Assert.AreEqual(1 << 2, (int)EntityCollectorFlag.ClashAsChange);
            Assert.AreEqual(1 << 3, (int)EntityCollectorFlag.RelatedComponentOnly);
            Assert.AreEqual(
                EntityCollectorFlag.RevisionAsChange
                | EntityCollectorFlag.MatchAsChange
                | EntityCollectorFlag.RelatedComponentOnly,
                EntityCollectorFlag.Default);
        }

        [Test]
        public void EntityCollector_ExistingMatchingEntities_PublishOnFirstFlush()
        {
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var entity3 = _world.CreateEntity();
            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            entity2.CreateComponent<VelocityComponent>();
            entity3.CreateComponent<VelocityComponent>();

            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default);

            AssertAllEmpty(collector);

            collector.Flush();

            AssertOnly(collector.Matching, entity1.EntityId, entity2.EntityId);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Collected, entity1.EntityId, entity2.EntityId);
            AssertOnly(collector.Changed, entity1.EntityId, entity2.EntityId);
        }

        [Test]
        public void EntityCollector_MatchingEntity_PublishesOnlyAfterFlush()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default);
            var beforeChange = new CollectorSnapshot(collector);

            entity.CreateComponent<PositionComponent>();

            beforeChange.AssertMatches(collector, "pending matching entity must not be visible before Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_ClashingEntity_PublishesOnlyAfterFlush()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);

            entity.DestroyComponent<PositionComponent>();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);

            collector.Flush();

            AssertEmpty(collector.Matching);
            AssertOnly(collector.Clashing, entity.EntityId);
            AssertEmpty(collector.Collected);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_RevisionChanges_PublishOnceAfterFlush()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default);
            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            ref var firstWrite = ref entity.GetComponent<PositionComponent>().RW;
            firstWrite.X = 1;
            ref var secondWrite = ref entity.GetComponent<PositionComponent>().RW;
            secondWrite.Y = 2;
            ref var thirdWrite = ref entity.GetComponent<PositionComponent>().RW;
            thirdWrite.X = 3;

            beforeChange.AssertMatches(collector, "revision changes must remain pending before Flush");

            collector.Flush();

            AssertEmpty(collector.Matching);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_MatchThenClashBeforeFirstFlush_DropsEntityFromAllBuffers()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.None);
            var beforeChange = new CollectorSnapshot(collector);

            entity.CreateComponent<PositionComponent>();
            entity.DestroyComponent<PositionComponent>();

            beforeChange.AssertMatches(collector, "transient entity must remain pending before Flush");

            collector.Flush();

            AssertAllEmpty(collector);
        }

        [Test]
        public void EntityCollector_EmptyFlush_ClearsPhaseBuffersAndKeepsCollected()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default);
            entity.CreateComponent<PositionComponent>();
            collector.Flush();

            collector.Flush();

            AssertEmpty(collector.Matching);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertEmpty(collector.Changed);
        }

        [Test]
        public void EntityCollector_ClashThenMatchBetweenFlushes_KeepsCollectedAndDedupsChanged()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entity.DestroyComponent<PositionComponent>();
            entity.CreateComponent<PositionComponent>();

            beforeChange.AssertMatches(collector, "membership bounce must remain pending before Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_MultipleEnterAndLeaveBetweenFlushes_UsesFinalStateAndDedupsChanged()
        {
            var entering = _world.CreateEntity();
            var leaving = _world.CreateEntity();
            leaving.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entering.CreateComponent<PositionComponent>();
            entering.DestroyComponent<PositionComponent>();
            entering.CreateComponent<PositionComponent>();

            leaving.DestroyComponent<PositionComponent>();
            leaving.CreateComponent<PositionComponent>();
            leaving.DestroyComponent<PositionComponent>();

            beforeChange.AssertMatches(collector, "multiple membership changes must remain pending before Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entering.EntityId);
            AssertOnly(collector.Clashing, leaving.EntityId);
            AssertOnly(collector.Collected, entering.EntityId);
            AssertOnly(collector.Changed, entering.EntityId, leaving.EntityId);
        }

        [Test]
        public void EntityCollector_MixedEntityChangesBetweenFlushes_PublishesEachEntityInExpectedBuffer()
        {
            var entering = _world.CreateEntity();
            var leaving = _world.CreateEntity();
            var updating = _world.CreateEntity();
            leaving.CreateComponent<PositionComponent>();
            updating.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entering.CreateComponent<PositionComponent>();
            leaving.DestroyComponent<PositionComponent>();
            ref var writable = ref updating.GetComponent<PositionComponent>().RW;
            writable.X = 10;

            beforeChange.AssertMatches(collector, "mixed changes must remain pending before Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entering.EntityId);
            AssertOnly(collector.Clashing, leaving.EntityId);
            AssertOnly(collector.Collected, updating.EntityId, entering.EntityId);
            AssertOnly(collector.Changed, entering.EntityId, leaving.EntityId, updating.EntityId);
        }

        [Test]
        public void EntityCollector_NoneFlag_DoesNotMarkMembershipOrRevisionChanged()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.None);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertEmpty(collector.Clashing);
            AssertEmpty(collector.Changed);

            collector.Flush();
            ref var writable = ref entity.GetComponent<PositionComponent>().RW;
            writable.X = 5;
            collector.Flush();

            AssertOnly(collector.Collected, entity.EntityId);
            AssertEmpty(collector.Changed);

            entity.DestroyComponent<PositionComponent>();
            collector.Flush();

            AssertOnly(collector.Clashing, entity.EntityId);
            AssertEmpty(collector.Collected);
            AssertEmpty(collector.Changed);
        }

        [Test]
        public void EntityCollector_ChangedOnMatching_OnlyMarksMatchingEntitiesChanged()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.MatchAsChange);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);

            collector.Flush();
            ref var writable = ref entity.GetComponent<PositionComponent>().RW;
            writable.X = 1;
            collector.Flush();
            AssertEmpty(collector.Changed);

            entity.DestroyComponent<PositionComponent>();
            collector.Flush();
            AssertOnly(collector.Clashing, entity.EntityId);
            AssertEmpty(collector.Changed);
        }

        [Test]
        public void EntityCollector_ChangedOnClashing_OnlyMarksClashingEntitiesChanged()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.ClashAsChange);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertEmpty(collector.Changed);

            collector.Flush();
            entity.DestroyComponent<PositionComponent>();
            collector.Flush();

            AssertOnly(collector.Clashing, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_ChangedOnRevision_OnlyMarksRevisionChangesChanged()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.RevisionAsChange);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertEmpty(collector.Changed);

            collector.Flush();
            ref var writable = ref entity.GetComponent<PositionComponent>().RW;
            writable.X = 7;
            collector.Flush();

            AssertOnly(collector.Changed, entity.EntityId);

            collector.Flush();
            entity.DestroyComponent<PositionComponent>();
            collector.Flush();

            AssertOnly(collector.Clashing, entity.EntityId);
            AssertEmpty(collector.Changed);
        }

        [Test]
        public void EntityCollector_ChangedOnRevisionWithRelatedComponentFlag_FiltersIrrelevantRevisions()
        {
            const EntityCollectorFlag flag =
                EntityCollectorFlag.RevisionAsChange
                | EntityCollectorFlag.RelatedComponentOnly;

            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>(), flag);
            collector.Flush();
            collector.Flush();

            ref var velocity = ref entity.GetComponent<VelocityComponent>().RW;
            velocity.X = 1;
            collector.Flush();
            AssertEmpty(collector.Changed);

            ref var position = ref entity.GetComponent<PositionComponent>().RW;
            position.X = 2;
            collector.Flush();
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_DefaultFlag_TracksMatchingAndRelevantRevisionOnly()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);

            collector.Flush();
            entity.CreateComponent<VelocityComponent>();
            collector.Flush();
            AssertEmpty(collector.Changed);

            ref var position = ref entity.GetComponent<PositionComponent>().RW;
            position.X = 3;
            collector.Flush();
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_DefaultFlags_DeduplicatesMatchingAndRevisionInSamePhase()
        {
            var entity = _world.CreateEntity();
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher);

            entity.CreateComponent<PositionComponent>();
            ref var writable = ref entity.GetComponent<PositionComponent>().RW;
            writable.X = 7;

            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);
        }

        [Test]
        public void EntityCollector_DefaultFlags_ExcludeClashingFromChanged()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>());

            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            collector.Flush();

            entity.DestroyComponent<PositionComponent>();
            collector.Flush();

            AssertOnly(collector.Clashing, entity.EntityId);
            AssertEmpty(collector.Changed);
        }

        [Test]
        public void EntityCollector_ChangedOnClashing_IncludesRemovedEntities()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.ClashAsChange);

            collector.Flush();
            entity.DestroyComponent<PositionComponent>();
            collector.Flush();

            AssertOnly(collector.Clashing, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_StructureChangeWhileStillMatched_IncludesChanged()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>(),
                EntityCollectorFlag.None);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            collector.Flush();

            entity.CreateComponent<VelocityComponent>();
            collector.Flush();

            AssertEmpty(collector.Matching);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_RevisionChanged_IsVisibleOnlyAfterFlush()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.RevisionAsChange);
            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            ref var writable = ref entity.GetComponent<PositionComponent>().RW;
            writable.X = 99;

            beforeChange.AssertMatches(collector, "revision change must not be visible before Flush");

            collector.Flush();

            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_Flush_ProcessesPendingChanges()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>());

            entity.CreateComponent<PositionComponent>();
            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);
        }
        
        [Test]
        public void EntityCollector_Dispose_ClearsAllBuffers()
        {
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>());
            collector.Flush();
            
            collector.Dispose();
            
            AssertAllEmpty(collector);
        }
        
        [Test]
        public void EntityCollector_MultipleChanges_ComplexScenario()
        {
            var entities = new List<Entity>();
            for (int i = 0; i < 5; i++)
            {
                entities.Add(_world.CreateEntity());
            }
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>());
            
            entities[0].CreateComponent<PositionComponent>();
            entities[1].CreateComponent<PositionComponent>();
            entities[2].CreateComponent<PositionComponent>();
            collector.Flush();
            
            AssertOnly(collector.Collected, entities[0].EntityId, entities[1].EntityId, entities[2].EntityId);
            
            entities[0].DestroyComponent<PositionComponent>();
            entities[3].CreateComponent<PositionComponent>();
            entities[4].CreateComponent<PositionComponent>();
            collector.Flush();
            
            AssertOnly(collector.Collected, entities[1].EntityId, entities[2].EntityId, entities[3].EntityId, entities[4].EntityId);
        }
        
        [Test]
        public void EntityCollector_ForEachSafety_DoesNotUseForeach()
        {
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>());
            collector.Flush();
            
            var collectedCount = 0;
            for (int i = 0; i < collector.Collected.Count; i++)
            {
                collectedCount++;
                var entityId = collector.Collected[i];
                Assert.IsTrue(entityId == entity1.EntityId || entityId == entity2.EntityId);
            }
            Assert.AreEqual(2, collectedCount);
            
            var whileCount = 0;
            var index = 0;
            while (index < collector.Collected.Count)
            {
                whileCount++;
                index++;
            }
            Assert.AreEqual(2, whileCount);
        }
        
        [Test]
        public void EntityCollector_PropertyAccess_Matcher()
        {
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.None);
            
            Assert.IsNotNull(collector.Matcher);
            Assert.AreEqual(matcher.EntityMask, collector.Matcher.EntityMask);
        }
        
        [Test]
        public void EntityCollector_BufferManagement_ClearAfterFlush()
        {
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            entity1.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.ClashAsChange);
            collector.Flush();
            collector.Flush();
            
            entity2.CreateComponent<PositionComponent>();
            entity1.DestroyComponent<PositionComponent>();
            collector.Flush();
            
            AssertOnly(collector.Matching, entity2.EntityId);
            AssertOnly(collector.Clashing, entity1.EntityId);
            
            collector.Flush();
            
            AssertEmpty(collector.Matching);
            AssertEmpty(collector.Clashing);
        }

        [Test]
        public void EntityCollector_ChangeComponent_AddIrrelevantComponent_DoesNotMarkChanged()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>(), EntityCollectorFlag.Default);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            collector.Flush();

            entity.CreateComponent<VelocityComponent>();
            collector.Flush();

            AssertEmpty(collector.Changed);
            AssertOnly(collector.Collected, entity.EntityId);
        }

        [Test]
        public void EntityCollector_ChangeComponent_ModifyIrrelevantComponent_DoesNotMarkChanged()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>(), EntityCollectorFlag.Default);

            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            collector.Flush();
            collector.Flush();

            ref var rw = ref entity.GetComponent<VelocityComponent>().RW;
            rw.X = 42;
            collector.Flush();

            AssertEmpty(collector.Changed);
            AssertOnly(collector.Collected, entity.EntityId);
        }

        [Test]
        public void EntityCollector_WithoutChangeComponent_AddIrrelevantComponent_MarksChanged()
        {
            const EntityCollectorFlag flag =
                EntityCollectorFlag.MatchAsChange
                | EntityCollectorFlag.RevisionAsChange;

            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>(), flag);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            collector.Flush();

            entity.CreateComponent<VelocityComponent>();
            collector.Flush();

            AssertOnly(collector.Changed, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);
        }

        [Test]
        public void EntityCollector_WithoutChangeComponent_ModifyIrrelevantComponent_MarksChanged()
        {
            const EntityCollectorFlag flag =
                EntityCollectorFlag.MatchAsChange
                | EntityCollectorFlag.RevisionAsChange;

            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>(), flag);

            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            collector.Flush();
            collector.Flush();

            ref var rw = ref entity.GetComponent<VelocityComponent>().RW;
            rw.X = 7;
            collector.Flush();

            AssertOnly(collector.Changed, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);
        }

        [Test]
        public void EntityCollector_ChangeComponent_ModifyRelevantComponent_StillMarksChanged()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>(), EntityCollectorFlag.Default);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            collector.Flush();

            ref var rw = ref entity.GetComponent<PositionComponent>().RW;
            rw.X = 12;
            collector.Flush();

            AssertOnly(collector.Changed, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);
        }

        [Test]
        public void EntityCollector_ChangeComponent_RemoveRelevantComponent_MarksClashingAndChanged()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);

            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            collector.Flush();
            collector.Flush();

            entity.DestroyComponent<PositionComponent>();
            collector.Flush();

            AssertEmpty(collector.Collected);
            AssertOnly(collector.Clashing, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_ChangeComponent_RemoveIrrelevantComponent_DoesNotClashOrChange()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);

            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            collector.Flush();
            collector.Flush();

            entity.DestroyComponent<VelocityComponent>();
            collector.Flush();

            AssertOnly(collector.Collected, entity.EntityId);
            AssertEmpty(collector.Clashing);
            AssertEmpty(collector.Changed);
        }

        [Test]
        public void EntityCollector_WithoutChangeComponent_RemoveIrrelevantComponent_MarksChanged()
        {
            const EntityCollectorFlag flag =
                EntityCollectorFlag.MatchAsChange
                | EntityCollectorFlag.RevisionAsChange
                | EntityCollectorFlag.ClashAsChange;

            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>(), flag);

            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            collector.Flush();
            collector.Flush();

            entity.DestroyComponent<VelocityComponent>();
            collector.Flush();

            AssertOnly(collector.Collected, entity.EntityId);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_ChangeComponent_EmptyMatcher_DoesNotDropChanges()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(EntityMatcher.With, EntityCollectorFlag.Default);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);

            collector.Flush();
            ref var rwPos = ref entity.GetComponent<PositionComponent>().RW;
            rwPos.X = 7;
            collector.Flush();
            AssertOnly(collector.Changed, entity.EntityId);

            collector.Flush();
            entity.CreateComponent<VelocityComponent>();
            collector.Flush();
            AssertOnly(collector.Changed, entity.EntityId);

            collector.Flush();
            ref var rwVel = ref entity.GetComponent<VelocityComponent>().RW;
            rwVel.X = 11;
            collector.Flush();
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_ChangeComponent_MixRelevantAndIrrelevant_BetweenFlushes_DedupsToRelevantOnly()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>(), EntityCollectorFlag.Default);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            collector.Flush();

            ref var rwPosA = ref entity.GetComponent<PositionComponent>().RW;
            rwPosA.X = 1;
            entity.CreateComponent<VelocityComponent>();
            ref var rwVel = ref entity.GetComponent<VelocityComponent>().RW;
            rwVel.X = 2;
            ref var rwPosB = ref entity.GetComponent<PositionComponent>().RW;
            rwPosB.Y = 3;
            collector.Flush();

            AssertOnly(collector.Changed, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);
        }

        [Test]
        public void EntityCollector_ChangeComponent_OnlyIrrelevant_BetweenFlushes_KeepsChangedEmpty()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>(), EntityCollectorFlag.Default);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            collector.Flush();

            entity.CreateComponent<VelocityComponent>();
            ref var rwVel1 = ref entity.GetComponent<VelocityComponent>().RW;
            rwVel1.X = 1;
            ref var rwVel2 = ref entity.GetComponent<VelocityComponent>().RW;
            rwVel2.Y = 2;
            collector.Flush();

            AssertEmpty(collector.Changed);
            AssertOnly(collector.Collected, entity.EntityId);
        }

        [Test]
        public void EntityCollector_ChangeComponent_MultipleRelevantComponents_BetweenFlushes_DedupChanged()
        {
            var entity = _world.CreateEntity();
            var matcher = EntityMatcher.With
                .OfAll<PositionComponent>()
                .OfNone<VelocityComponent>();
            var collector = _world.CreateCollector(
                matcher,
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            collector.Flush();

            ref var rwPos = ref entity.GetComponent<PositionComponent>().RW;
            rwPos.X = 5;
            entity.CreateComponent<VelocityComponent>();
            collector.Flush();

            AssertOnly(collector.Changed, entity.EntityId);
            AssertOnly(collector.Clashing, entity.EntityId);
            AssertEmpty(collector.Collected);
        }

        [Test]
        public void EntityCollector_AlternatingModifyAndFlush_EachFlushExposesOneChange()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>(), EntityCollectorFlag.Default);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            collector.Flush();
            AssertEmpty(collector.Changed);

            for (var i = 0; i < 4; i++)
            {
                ref var rw = ref entity.GetComponent<PositionComponent>().RW;
                rw.X = i;

                collector.Flush();

                AssertOnly(collector.Changed, entity.EntityId);
            }

            collector.Flush();
            AssertEmpty(collector.Changed);
        }


        [Test]
        public void EntityCollector_OfAny_MatchingAndClashing_PublishAfterFlush()
        {
            var entering = _world.CreateEntity();
            var leaving = _world.CreateEntity();
            leaving.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entering.CreateComponent<VelocityComponent>();
            leaving.DestroyComponent<PositionComponent>();

            beforeChange.AssertMatches(collector, "OfAny membership changes must wait for Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entering.EntityId);
            AssertOnly(collector.Clashing, leaving.EntityId);
            AssertOnly(collector.Collected, entering.EntityId);
            AssertOnly(collector.Changed, entering.EntityId);
        }

        [Test]
        public void EntityCollector_OfAny_StillMatchedStructuralChanges_MarkChangedOnce()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entity.CreateComponent<VelocityComponent>();
            entity.DestroyComponent<PositionComponent>();
            entity.CreateComponent<PositionComponent>();
            entity.DestroyComponent<VelocityComponent>();

            beforeChange.AssertMatches(collector, "OfAny structural changes must remain pending before Flush");

            collector.Flush();

            AssertEmpty(collector.Matching);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_OfAny_RevisionChanges_DedupChanged()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            ref var first = ref entity.GetComponent<PositionComponent>().RW;
            first.X = 1;
            ref var second = ref entity.GetComponent<PositionComponent>().RW;
            second.Y = 2;

            beforeChange.AssertMatches(collector, "OfAny revisions must remain pending before Flush");

            collector.Flush();

            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_OfAny_ClashingFlagControlsChanged()
        {
            var defaultEntity = _world.CreateEntity();
            defaultEntity.CreateComponent<PositionComponent>();
            var defaultCollector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>(),
                EntityCollectorFlag.Default);
            defaultCollector.Flush();
            defaultCollector.Flush();

            defaultEntity.DestroyComponent<PositionComponent>();
            defaultCollector.Flush();

            AssertOnly(defaultCollector.Clashing, defaultEntity.EntityId);
            AssertEmpty(defaultCollector.Changed);

            var clashEntity = _world.CreateEntity();
            clashEntity.CreateComponent<PositionComponent>();
            var clashCollector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
            clashCollector.Flush();
            clashCollector.Flush();

            clashEntity.DestroyComponent<PositionComponent>();
            clashCollector.Flush();

            AssertOnly(clashCollector.Clashing, clashEntity.EntityId);
            AssertOnly(clashCollector.Changed, clashEntity.EntityId);
        }

        [Test]
        public void EntityCollector_OfAny_NoneFlagSuppressesChanged()
        {
            var entity = _world.CreateEntity();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>(),
                EntityCollectorFlag.None);

            entity.CreateComponent<PositionComponent>();
            collector.Flush();
            AssertOnly(collector.Matching, entity.EntityId);
            AssertEmpty(collector.Changed);

            collector.Flush();
            ref var writable = ref entity.GetComponent<PositionComponent>().RW;
            writable.X = 3;
            collector.Flush();
            AssertEmpty(collector.Changed);

            entity.DestroyComponent<PositionComponent>();
            collector.Flush();
            AssertOnly(collector.Clashing, entity.EntityId);
            AssertEmpty(collector.Changed);
        }

        [Test]
        public void EntityCollector_OfNone_MatchingAndClashing_PublishAfterFlush()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfNone<HealthComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entity.CreateComponent<HealthComponent>();

            beforeChange.AssertMatches(collector, "OfNone clashing must remain pending before Flush");

            collector.Flush();

            AssertEmpty(collector.Matching);
            AssertOnly(collector.Clashing, entity.EntityId);
            AssertEmpty(collector.Collected);
            AssertEmpty(collector.Changed);
        }

        [Test]
        public void EntityCollector_OfNone_RemoveForbidden_MatchesAfterFlush()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<HealthComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfNone<HealthComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            AssertAllEmpty(collector);
            var beforeChange = new CollectorSnapshot(collector);

            entity.DestroyComponent<HealthComponent>();

            beforeChange.AssertMatches(collector, "removing OfNone blocker must remain pending before Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_OfNone_BounceForbidden_DedupsChanged()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfNone<HealthComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entity.CreateComponent<HealthComponent>();
            entity.DestroyComponent<HealthComponent>();

            beforeChange.AssertMatches(collector, "OfNone bounce must remain pending before Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_OfNone_ClashingFlagControlsChanged()
        {
            var defaultEntity = _world.CreateEntity();
            defaultEntity.CreateComponent<PositionComponent>();
            var defaultCollector = _world.CreateCollector(
                EntityMatcher.With.OfNone<HealthComponent>(),
                EntityCollectorFlag.Default);
            defaultCollector.Flush();
            defaultCollector.Flush();

            defaultEntity.CreateComponent<HealthComponent>();
            defaultCollector.Flush();

            AssertOnly(defaultCollector.Clashing, defaultEntity.EntityId);
            AssertEmpty(defaultCollector.Changed);

            var clashEntity = _world.CreateEntity();
            clashEntity.CreateComponent<PositionComponent>();
            var clashCollector = _world.CreateCollector(
                EntityMatcher.With.OfNone<HealthComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
            clashCollector.Flush();
            clashCollector.Flush();

            clashEntity.CreateComponent<HealthComponent>();
            clashCollector.Flush();

            AssertOnly(clashCollector.Clashing, clashEntity.EntityId);
            AssertOnly(clashCollector.Changed, clashEntity.EntityId);
        }

        [Test]
        public void EntityCollector_OfNone_NoneFlagSuppressesChanged()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfNone<HealthComponent>(),
                EntityCollectorFlag.None);
            collector.Flush();
            AssertOnly(collector.Matching, entity.EntityId);
            AssertEmpty(collector.Changed);

            collector.Flush();
            ref var writable = ref entity.GetComponent<PositionComponent>().RW;
            writable.X = 4;
            collector.Flush();
            AssertEmpty(collector.Changed);

            entity.CreateComponent<HealthComponent>();
            collector.Flush();
            AssertOnly(collector.Clashing, entity.EntityId);
            AssertEmpty(collector.Changed);
        }

        [Test]
        public void EntityCollector_OfAllAndOfNone_ForbiddenAndRequiredChanges_PublishExpectedBuffers()
        {
            var forbiddenExit = _world.CreateEntity();
            var requiredExit = _world.CreateEntity();
            var entering = _world.CreateEntity();
            forbiddenExit.CreateComponent<PositionComponent>();
            requiredExit.CreateComponent<PositionComponent>();
            entering.CreateComponent<PositionComponent>();
            entering.CreateComponent<HealthComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            forbiddenExit.CreateComponent<HealthComponent>();
            requiredExit.DestroyComponent<PositionComponent>();
            entering.DestroyComponent<HealthComponent>();

            beforeChange.AssertMatches(collector, "OfAll/OfNone changes must remain pending before Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entering.EntityId);
            AssertOnly(collector.Clashing, forbiddenExit.EntityId, requiredExit.EntityId);
            AssertOnly(collector.Collected, entering.EntityId);
            AssertOnly(collector.Changed, entering.EntityId);
        }

        [Test]
        public void EntityCollector_OfAllAndOfNone_StillMatchedChanges_MarkChangedOnce()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entity.CreateComponent<HealthComponent>();
            entity.DestroyComponent<HealthComponent>();
            ref var writable = ref entity.GetComponent<PositionComponent>().RW;
            writable.X = 9;
            entity.CreateComponent<ManaComponent>();

            beforeChange.AssertMatches(collector, "OfAll/OfNone still-matched changes must wait for Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_OfAllAndOfNone_ClashingFlagControlsForbiddenAndRequiredExit()
        {
            var defaultEntity = _world.CreateEntity();
            defaultEntity.CreateComponent<PositionComponent>();
            var defaultCollector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.Default);
            defaultCollector.Flush();
            defaultCollector.Flush();

            defaultEntity.CreateComponent<HealthComponent>();
            defaultCollector.Flush();
            AssertOnly(defaultCollector.Clashing, defaultEntity.EntityId);
            AssertEmpty(defaultCollector.Changed);

            var clashEntity = _world.CreateEntity();
            clashEntity.CreateComponent<PositionComponent>();
            var clashCollector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
            clashCollector.Flush();
            clashCollector.Flush();

            clashEntity.DestroyComponent<PositionComponent>();
            clashCollector.Flush();
            AssertOnly(clashCollector.Clashing, clashEntity.EntityId);
            AssertOnly(clashCollector.Changed, clashEntity.EntityId);
        }

        [Test]
        public void EntityCollector_OfAllAndOfAny_MissingAllOrAny_MatchesAfterCompletion()
        {
            var missingAny = _world.CreateEntity();
            var missingAll = _world.CreateEntity();
            missingAny.CreateComponent<PositionComponent>();
            missingAll.CreateComponent<VelocityComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>().OfAny<VelocityComponent>().OfAny<HealthComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            AssertAllEmpty(collector);
            var beforeChange = new CollectorSnapshot(collector);

            missingAny.CreateComponent<VelocityComponent>();
            missingAll.CreateComponent<PositionComponent>();

            beforeChange.AssertMatches(collector, "OfAll/OfAny completion must remain pending before Flush");

            collector.Flush();

            AssertOnly(collector.Matching, missingAny.EntityId, missingAll.EntityId);
            AssertOnly(collector.Collected, missingAny.EntityId, missingAll.EntityId);
            AssertOnly(collector.Changed, missingAny.EntityId, missingAll.EntityId);
        }

        [Test]
        public void EntityCollector_OfAllAndOfAny_AllOrLastAnyRemoval_ClashesAfterFlush()
        {
            var missingAll = _world.CreateEntity();
            var missingAny = _world.CreateEntity();
            missingAll.CreateComponent<PositionComponent>();
            missingAll.CreateComponent<VelocityComponent>();
            missingAny.CreateComponent<PositionComponent>();
            missingAny.CreateComponent<HealthComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>().OfAny<VelocityComponent>().OfAny<HealthComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            missingAll.DestroyComponent<PositionComponent>();
            missingAny.DestroyComponent<HealthComponent>();

            beforeChange.AssertMatches(collector, "OfAll/OfAny removals must remain pending before Flush");

            collector.Flush();

            AssertOnly(collector.Clashing, missingAll.EntityId, missingAny.EntityId);
            AssertEmpty(collector.Collected);
            AssertEmpty(collector.Changed);
        }

        [Test]
        public void EntityCollector_OfAllAndOfAny_StillMatchedAnyChanges_DedupChanged()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>().OfAny<VelocityComponent>().OfAny<HealthComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entity.CreateComponent<HealthComponent>();
            entity.DestroyComponent<VelocityComponent>();
            entity.CreateComponent<VelocityComponent>();
            entity.DestroyComponent<HealthComponent>();

            beforeChange.AssertMatches(collector, "OfAll/OfAny still-matched changes must remain pending before Flush");

            collector.Flush();

            AssertEmpty(collector.Matching);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_OfAnyAndOfNone_MatchingAndClashing_PublishExpectedBuffers()
        {
            var entering = _world.CreateEntity();
            var forbiddenExit = _world.CreateEntity();
            var lastAnyExit = _world.CreateEntity();
            forbiddenExit.CreateComponent<PositionComponent>();
            lastAnyExit.CreateComponent<VelocityComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entering.CreateComponent<PositionComponent>();
            forbiddenExit.CreateComponent<HealthComponent>();
            lastAnyExit.DestroyComponent<VelocityComponent>();

            beforeChange.AssertMatches(collector, "OfAny/OfNone changes must remain pending before Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entering.EntityId);
            AssertOnly(collector.Clashing, forbiddenExit.EntityId, lastAnyExit.EntityId);
            AssertOnly(collector.Collected, entering.EntityId);
            AssertOnly(collector.Changed, entering.EntityId);
        }

        [Test]
        public void EntityCollector_OfAnyAndOfNone_RemoveForbidden_MatchesAfterFlush()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<HealthComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            AssertAllEmpty(collector);
            var beforeChange = new CollectorSnapshot(collector);

            entity.DestroyComponent<HealthComponent>();

            beforeChange.AssertMatches(collector, "OfAny/OfNone unblock must remain pending before Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_OfAnyAndOfNone_StillMatchedAnyChanges_DedupChanged()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entity.CreateComponent<VelocityComponent>();
            entity.CreateComponent<ManaComponent>();
            entity.DestroyComponent<PositionComponent>();

            beforeChange.AssertMatches(collector, "OfAny/OfNone still-matched changes must wait for Flush");

            collector.Flush();

            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_CombinedMatcher_MatchesWhenAllAnyAndNoneSatisfied()
        {
            var alreadyMatched = _world.CreateEntity();
            var missingAll = _world.CreateEntity();
            var missingAny = _world.CreateEntity();
            alreadyMatched.CreateComponent<PositionComponent>();
            alreadyMatched.CreateComponent<VelocityComponent>();
            missingAll.CreateComponent<VelocityComponent>();
            missingAny.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>().OfAny<VelocityComponent>().OfAny<HealthComponent>().OfNone<DamageComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            AssertOnly(collector.Matching, alreadyMatched.EntityId);
            AssertOnly(collector.Collected, alreadyMatched.EntityId);
            AssertOnly(collector.Changed, alreadyMatched.EntityId);

            collector.Flush();
            missingAll.CreateComponent<PositionComponent>();
            missingAny.CreateComponent<HealthComponent>();
            collector.Flush();

            AssertOnly(collector.Matching, missingAll.EntityId, missingAny.EntityId);
            AssertOnly(collector.Collected, alreadyMatched.EntityId, missingAll.EntityId, missingAny.EntityId);
            AssertOnly(collector.Changed, missingAll.EntityId, missingAny.EntityId);
        }

        [Test]
        public void EntityCollector_CombinedMatcher_ClashesWhenAnyRequirementBreaks()
        {
            var forbiddenExit = _world.CreateEntity();
            var missingAll = _world.CreateEntity();
            var missingAny = _world.CreateEntity();
            forbiddenExit.CreateComponent<PositionComponent>();
            forbiddenExit.CreateComponent<VelocityComponent>();
            missingAll.CreateComponent<PositionComponent>();
            missingAll.CreateComponent<VelocityComponent>();
            missingAny.CreateComponent<PositionComponent>();
            missingAny.CreateComponent<HealthComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>().OfAny<VelocityComponent>().OfAny<HealthComponent>().OfNone<DamageComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            forbiddenExit.CreateComponent<DamageComponent>();
            missingAll.DestroyComponent<PositionComponent>();
            missingAny.DestroyComponent<HealthComponent>();

            beforeChange.AssertMatches(collector, "combined matcher clashing changes must wait for Flush");

            collector.Flush();

            AssertOnly(collector.Clashing, forbiddenExit.EntityId, missingAll.EntityId, missingAny.EntityId);
            AssertEmpty(collector.Collected);
            AssertEmpty(collector.Changed);
        }

        [Test]
        public void EntityCollector_CombinedMatcher_StillMatchedMixedChanges_DedupChanged()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>().OfAny<VelocityComponent>().OfAny<HealthComponent>().OfNone<DamageComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entity.DestroyComponent<VelocityComponent>();
            entity.CreateComponent<HealthComponent>();
            ref var writable = ref entity.GetComponent<PositionComponent>().RW;
            writable.X = 6;
            entity.CreateComponent<ManaComponent>();

            beforeChange.AssertMatches(collector, "combined still-matched changes must wait for Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertEmpty(collector.Clashing);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);
        }

        [Test]
        public void EntityCollector_CombinedMatcher_MixedEntities_DoNotPolluteBuffers()
        {
            var entering = _world.CreateEntity();
            var forbiddenExit = _world.CreateEntity();
            var lastAnyExit = _world.CreateEntity();
            var updating = _world.CreateEntity();
            entering.CreateComponent<PositionComponent>();
            forbiddenExit.CreateComponent<PositionComponent>();
            forbiddenExit.CreateComponent<VelocityComponent>();
            lastAnyExit.CreateComponent<PositionComponent>();
            lastAnyExit.CreateComponent<HealthComponent>();
            updating.CreateComponent<PositionComponent>();
            updating.CreateComponent<VelocityComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>().OfAny<VelocityComponent>().OfAny<HealthComponent>().OfNone<DamageComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            collector.Flush();
            var beforeChange = new CollectorSnapshot(collector);

            entering.CreateComponent<VelocityComponent>();
            forbiddenExit.CreateComponent<DamageComponent>();
            lastAnyExit.DestroyComponent<HealthComponent>();
            ref var writable = ref updating.GetComponent<PositionComponent>().RW;
            writable.X = 8;

            beforeChange.AssertMatches(collector, "combined mixed changes must wait for Flush");

            collector.Flush();

            AssertOnly(collector.Matching, entering.EntityId);
            AssertOnly(collector.Clashing, forbiddenExit.EntityId, lastAnyExit.EntityId);
            AssertOnly(collector.Collected, updating.EntityId, entering.EntityId);
            AssertOnly(collector.Changed, entering.EntityId, updating.EntityId);
        }

        [Test]
        public void EntityCollector_MatcherCombinations_FlagMatrix_ControlsChanged()
        {
            var noneEntity = _world.CreateEntity();
            var noneCollector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.None);
            noneEntity.CreateComponent<PositionComponent>();
            noneCollector.Flush();
            AssertOnly(noneCollector.Matching, noneEntity.EntityId);
            AssertEmpty(noneCollector.Changed);

            var matchingEntity = _world.CreateEntity();
            var matchingCollector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.MatchAsChange);
            matchingCollector.Flush();
            matchingCollector.Flush();
            matchingEntity.CreateComponent<PositionComponent>();
            matchingCollector.Flush();
            AssertOnly(matchingCollector.Changed, matchingEntity.EntityId);

            var clashingForbidden = _world.CreateEntity();
            var clashingLastAny = _world.CreateEntity();
            clashingForbidden.CreateComponent<PositionComponent>();
            clashingLastAny.CreateComponent<VelocityComponent>();
            var clashingCollector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.ClashAsChange);
            clashingCollector.Flush();
            clashingCollector.Flush();
            clashingForbidden.CreateComponent<HealthComponent>();
            clashingLastAny.DestroyComponent<VelocityComponent>();
            clashingCollector.Flush();
            AssertOnly(clashingCollector.Clashing, clashingForbidden.EntityId, clashingLastAny.EntityId);
            AssertOnly(clashingCollector.Changed, clashingForbidden.EntityId, clashingLastAny.EntityId);

            var revisionEntity = _world.CreateEntity();
            revisionEntity.CreateComponent<PositionComponent>();
            var revisionCollector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.RevisionAsChange);
            revisionCollector.Flush();
            revisionCollector.Flush();
            ref var revision = ref revisionEntity.GetComponent<PositionComponent>().RW;
            revision.X = 1;
            revisionCollector.Flush();
            AssertOnly(revisionCollector.Changed, revisionEntity.EntityId);

            var relatedEntity = _world.CreateEntity();
            relatedEntity.CreateComponent<PositionComponent>();
            relatedEntity.CreateComponent<ManaComponent>();
            var relatedCollector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.RevisionAsChange | EntityCollectorFlag.RelatedComponentOnly);
            relatedCollector.Flush();
            relatedCollector.Flush();
            ref var unrelated = ref relatedEntity.GetComponent<ManaComponent>().RW;
            unrelated.Value = 1;
            relatedCollector.Flush();
            AssertEmpty(relatedCollector.Changed);
            ref var related = ref relatedEntity.GetComponent<PositionComponent>().RW;
            related.X = 2;
            relatedCollector.Flush();
            AssertOnly(relatedCollector.Changed, relatedEntity.EntityId);

            var defaultEntity = _world.CreateEntity();
            defaultEntity.CreateComponent<PositionComponent>();
            var defaultCollector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.Default);
            defaultCollector.Flush();
            defaultCollector.Flush();
            defaultEntity.CreateComponent<HealthComponent>();
            defaultCollector.Flush();
            AssertOnly(defaultCollector.Clashing, defaultEntity.EntityId);
            AssertEmpty(defaultCollector.Changed);

            var defaultClashEntity = _world.CreateEntity();
            defaultClashEntity.CreateComponent<VelocityComponent>();
            var defaultClashCollector = _world.CreateCollector(
                EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>().OfNone<HealthComponent>(),
                EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
            defaultClashCollector.Flush();
            defaultClashCollector.Flush();
            defaultClashEntity.DestroyComponent<VelocityComponent>();
            defaultClashCollector.Flush();
            AssertOnly(defaultClashCollector.Clashing, defaultClashEntity.EntityId);
            AssertOnly(defaultClashCollector.Changed, defaultClashEntity.EntityId);
        }

        [Test]
        public void EntityCollector_DestroyedEntity_LeavesCollectedOnNextFlush()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            AssertOnly(collector.Collected, entity.EntityId);

            _world.DestroyEntity(entity);
            collector.Flush();

            AssertEmpty(collector.Collected);
            AssertOnly(collector.Clashing, entity.EntityId);
        }

        private static void AssertAllEmpty(IEntityCollector collector)
        {
            AssertEmpty(collector.Matching);
            AssertEmpty(collector.Clashing);
            AssertEmpty(collector.Collected);
            AssertEmpty(collector.Changed);
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
                AssertContainsOnce(actual, expectedIds[i]);
            }
        }

        private static void AssertContainsOnce(IReadOnlyList<ulong> actual, ulong entityId)
        {
            var count = 0;
            for (var i = 0; i < actual.Count; i++)
            {
                if (actual[i] == entityId)
                    count += 1;
            }
            Assert.AreEqual(1, count);
        }

        private sealed class CollectorSnapshot
        {
            private readonly List<ulong> m_collected;
            private readonly List<ulong> m_matching;
            private readonly List<ulong> m_clashing;
            private readonly List<ulong> m_changed;

            public CollectorSnapshot(IEntityCollector collector)
            {
                m_collected = new List<ulong>(collector.Collected);
                m_matching = new List<ulong>(collector.Matching);
                m_clashing = new List<ulong>(collector.Clashing);
                m_changed = new List<ulong>(collector.Changed);
            }

            public void AssertMatches(IEntityCollector collector, string message)
            {
                CollectionAssert.AreEqual(m_collected, new List<ulong>(collector.Collected), message);
                CollectionAssert.AreEqual(m_matching, new List<ulong>(collector.Matching), message);
                CollectionAssert.AreEqual(m_clashing, new List<ulong>(collector.Clashing), message);
                CollectionAssert.AreEqual(m_changed, new List<ulong>(collector.Changed), message);
            }
        }

        private struct PositionComponent : IComponent<PositionComponent>
        {
            public float X;
            public float Y;
        }
        
        private struct VelocityComponent : IComponent<VelocityComponent>
        {
            public float X;
            public float Y;
        }

        private struct HealthComponent : IComponent<HealthComponent>
        {
            public float Value;
        }

        private struct DamageComponent : IComponent<DamageComponent>
        {
            public float Value;
        }

        private struct ManaComponent : IComponent<ManaComponent>
        {
            public float Value;
        }
    }
}
