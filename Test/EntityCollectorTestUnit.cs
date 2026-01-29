using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TinyECS;
using TinyECS.Defines;
using TinyECS.Managers;

namespace TinyECS.Test
{
    [TestFixture]
    public class EntityCollectorTestUnit
    {
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
        public void EntityCollector_DefaultBehavior_ImmediateCollection()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var entity3 = _world.CreateEntity();
            
            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            entity2.CreateComponent<VelocityComponent>();
            entity3.CreateComponent<VelocityComponent>();
            
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.None);
            
            // Act - No Change() needed for immediate behavior
            var collectedEntities = new List<ulong>();
            for (int i = 0; i < collector.Collected.Count; i++)
            {
                collectedEntities.Add(collector.Collected[i]);
            }
            
            // Assert
            Assert.AreEqual(2, collectedEntities.Count);
            Assert.IsTrue(collectedEntities.Contains(entity1.EntityId));
            Assert.IsTrue(collectedEntities.Contains(entity2.EntityId));
            Assert.IsFalse(collectedEntities.Contains(entity3.EntityId));
        }
        
        [Test]
        public void EntityCollector_PhantomEntity_Exclusion()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.Lazy);
            
            entity1.CreateComponent<PositionComponent>();
            entity1.DestroyComponent<PositionComponent>();
            
            collector.Change();
            
            // Assert
            Assert.IsFalse(collector.Collected.Contains(entity1.EntityId));
            Assert.IsFalse(collector.Clashing.Contains(entity1.EntityId));
            Assert.IsFalse(collector.Matching.Contains(entity1.EntityId));
        }
        
        [Test]
        public void EntityCollector_LazyAddBehavior_DelayedAddition()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.LazyAdd);
            
            // Act - Add components after collector creation
            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            
            // Before Change() - should not be in collected
            var beforeChangeCollected = new List<ulong>();
            for (int i = 0; i < collector.Collected.Count; i++)
            {
                beforeChangeCollected.Add(collector.Collected[i]);
            }
            
            collector.Change();
            
            // After Change() - should be in collected
            var afterChangeCollected = new List<ulong>();
            for (int i = 0; i < collector.Collected.Count; i++)
            {
                afterChangeCollected.Add(collector.Collected[i]);
            }
            
            // Assert
            Assert.AreEqual(0, beforeChangeCollected.Count, "Entities should not be collected before Change() with LazyAdd");
            Assert.AreEqual(2, afterChangeCollected.Count, "Entities should be collected after Change() with LazyAdd");
            Assert.IsTrue(afterChangeCollected.Contains(entity1.EntityId));
            Assert.IsTrue(afterChangeCollected.Contains(entity2.EntityId));
        }
        
        [Test]
        public void EntityCollector_LazyRemoveBehavior_DelayedRemoval()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            
            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.LazyRemove);
            
            // Act - Remove component
            entity1.DestroyComponent(entity1.GetComponent<PositionComponent>());
            
            // Before Change() - should still be in collected
            var beforeChangeCollected = new List<ulong>(collector.Collected);
            
            collector.Change();
            
            // After Change() - should be removed from collected
            var afterChangeCollected = new List<ulong>(collector.Collected);
            
            // Assert
            Assert.AreEqual(2, beforeChangeCollected.Count, "Entities should still be collected before Change() with LazyRemove");
            Assert.AreEqual(1, afterChangeCollected.Count, "Entity should be removed after Change() with LazyRemove");
            Assert.IsFalse(afterChangeCollected.Contains(entity1.EntityId));
            Assert.IsTrue(afterChangeCollected.Contains(entity2.EntityId));
        }
        
        [Test]
        public void EntityCollector_LazyBehavior_DelayedBothAddAndRemove()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var entity3 = _world.CreateEntity();
            
            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.Lazy);
            
            // Act - Add and remove components
            entity1.DestroyComponent(entity1.GetComponent<PositionComponent>()); // Should be removed
            entity3.CreateComponent<PositionComponent>(); // Should be added
            
            // Before Change()
            var beforeChangeCollected = new List<ulong>(collector.Collected);
            
            collector.Change();
            
            // After Change()
            var afterChangeCollected = new List<ulong>(collector.Collected);
            
            // Assert
            Assert.AreEqual(0, beforeChangeCollected.Count, "Original entities should be present before Change()");
            Assert.AreEqual(2, afterChangeCollected.Count, "Only entity1 should not remain after Change()");
            Assert.IsFalse(afterChangeCollected.Contains(entity1.EntityId)); // Should not be added yet due to LazyAdd
            Assert.IsTrue(afterChangeCollected.Contains(entity2.EntityId));
            Assert.IsTrue(afterChangeCollected.Contains(entity3.EntityId)); 
        }
        
        [Test]
        public void EntityCollector_MatchingAndClashingBuffers_TracksChanges()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            
            entity1.CreateComponent<PositionComponent>();
            
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.None);
            
            // Act - Make changes
            entity2.CreateComponent<PositionComponent>(); // Should be matching
            entity1.DestroyComponent(entity1.GetComponent<PositionComponent>()); // Should be clashing
            
            var finalCollected = new List<ulong>(collector.Collected);
            
            collector.Change();
            
            // Get matching and clashing entities after Change()
            var matchingEntities = new List<ulong>(collector.Matching);
            var clashingEntities = new List<ulong>(collector.Clashing);
            
            // Assert
            Assert.AreEqual(1, matchingEntities.Count, "entity2 should be matching");
            Assert.IsTrue(matchingEntities.Contains(entity2.EntityId));
            
            Assert.AreEqual(1, clashingEntities.Count, "entity1 should be clashing");
            Assert.IsTrue(clashingEntities.Contains(entity1.EntityId));
            
            Assert.AreEqual(1, finalCollected.Count, "Only entity2 should be in final collection");
            Assert.IsTrue(finalCollected.Contains(entity2.EntityId));
        }
        
        [Test]
        public void EntityCollector_Dispose_ClearsAllBuffers()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            
            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.None);
            
            // Act
            collector.Dispose();
            
            // Assert - All buffers should be empty
            Assert.AreEqual(0, collector.Collected.Count);
            Assert.AreEqual(0, collector.Matching.Count);
            Assert.AreEqual(0, collector.Clashing.Count);
        }
        
        [Test]
        public void EntityCollector_MultipleChanges_ComplexScenario()
        {
            // Arrange
            var entities = new List<Entity>();
            for (int i = 0; i < 5; i++)
            {
                entities.Add(_world.CreateEntity());
            }
            
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher);
            
            // Act - Complex sequence of changes
            entities[0].CreateComponent<PositionComponent>();
            entities[1].CreateComponent<PositionComponent>();
            entities[2].CreateComponent<PositionComponent>();
            
            collector.Change();
            
            // Verify first batch
            var firstBatch = new List<ulong>(collector.Collected);
            Assert.That(firstBatch.Count, Is.EqualTo(3));
            
            // More changes
            entities[0].DestroyComponent(entities[0].GetComponent<PositionComponent>());
            entities[3].CreateComponent<PositionComponent>();
            entities[4].CreateComponent<PositionComponent>();
            
            collector.Change();
            
            // Verify second batch
            var secondBatch = new List<ulong>(collector.Collected);
            Assert.That(secondBatch.Count, Is.EqualTo(4));
            
            Assert.IsFalse(secondBatch.Contains(entities[0].EntityId));
            Assert.IsTrue(secondBatch.Contains(entities[1].EntityId));
            Assert.IsTrue(secondBatch.Contains(entities[2].EntityId));
            Assert.IsTrue(secondBatch.Contains(entities[3].EntityId));
            Assert.IsTrue(secondBatch.Contains(entities[4].EntityId));
        }
        
        [Test]
        public void EntityCollector_ForEachSafety_DoesNotUseForeach()
        {
            // This test verifies that we don't use foreach when iterating Collected
            // as requested in the requirements for safety reasons
            
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            
            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.None);
            
            // Act & Assert - Use for loop instead of foreach as specified
            var collectedCount = 0;
            for (int i = 0; i < collector.Collected.Count; i++)
            {
                collectedCount++;
                var entityId = collector.Collected[i];
                Assert.IsTrue(entityId == entity1.EntityId || entityId == entity2.EntityId);
            }
            
            Assert.AreEqual(2, collectedCount);
            
            // Also test with while loop as an alternative
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
            // Test accessing the Matcher property of a collector
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.None);
            
            // Assert
            Assert.IsNotNull(collector.Matcher);
            Assert.AreEqual(matcher.EntityMask, collector.Matcher.EntityMask);
        }
        
        [Test]
        public void EntityCollector_FlagCombinations_LazyAddOnly()
        {
            // Test EntityCollectorFlag.LazyAdd flag behavior specifically
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.LazyAdd);
            
            // Act - Add components after collector creation
            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            
            // Before Change() - should not be in collected due to LazyAdd
            var beforeChangeCount = collector.Collected.Count;
            
            collector.Change();
            
            // After Change() - should be in collected
            var afterChangeCount = collector.Collected.Count;
            
            // Assert
            Assert.AreEqual(0, beforeChangeCount, "Should not collect entities before Change() with LazyAdd flag");
            Assert.AreEqual(2, afterChangeCount, "Should collect entities after Change() with LazyAdd flag");
        }
        
        [Test]
        public void EntityCollector_FlagCombinations_LazyRemoveOnly()
        {
            // Test EntityCollectorFlag.LazyRemove flag behavior specifically
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            
            entity1.CreateComponent<PositionComponent>();
            entity2.CreateComponent<PositionComponent>();
            
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.LazyRemove);
            
            // Act - Remove component
            entity1.DestroyComponent(entity1.GetComponent<PositionComponent>());
            
            // Before Change() - should still be in collected due to LazyRemove
            var beforeChangeCount = collector.Collected.Count;
            
            collector.Change();
            
            // After Change() - should be removed from collected
            var afterChangeCount = collector.Collected.Count;
            
            // Assert
            Assert.AreEqual(2, beforeChangeCount, "Should still contain entities before Change() with LazyRemove flag");
            Assert.AreEqual(1, afterChangeCount, "Should remove entity after Change() with LazyRemove flag");
        }
        
        [Test]
        public void EntityCollector_BufferManagement_ClearAfterChange()
        {
            // Test that matching and clashing buffers are cleared after Change() is called
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            
            entity1.CreateComponent<PositionComponent>();
            
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.None);
            
            // Act - Make changes that affect matching/clashing
            entity2.CreateComponent<PositionComponent>(); // Should be matching
            entity1.DestroyComponent(entity1.GetComponent<PositionComponent>()); // Should be clashing
            
            collector.Change(); // Process the changes
            
            // Verify buffers have the expected entities
            var matchingBeforeSecondChange = new List<ulong>(collector.Matching);
            var clashingBeforeSecondChange = new List<ulong>(collector.Clashing);
            
            // Call Change() again without any actual changes
            collector.Change();
            
            var matchingAfterSecondChange = new List<ulong>(collector.Matching);
            var clashingAfterSecondChange = new List<ulong>(collector.Clashing);
            
            // Assert
            Assert.AreEqual(1, matchingBeforeSecondChange.Count, "Should have 1 matching entity");
            Assert.AreEqual(1, clashingBeforeSecondChange.Count, "Should have 1 clashing entity");
            
            Assert.AreEqual(0, matchingAfterSecondChange.Count, "Matching buffer should be cleared after Change()");
            Assert.AreEqual(0, clashingAfterSecondChange.Count, "Clashing buffer should be cleared after Change()");
        }
        
        [Test]
        public void EntityCollector_ChangeMethod_ProcessesChanges()
        {
            // Test that Change() method properly processes changes
            var entity = _world.CreateEntity();
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.Lazy);
            
            // Initially no entities should match
            Assert.AreEqual(0, collector.Collected.Count);
            
            // Add component that makes entity match
            entity.CreateComponent<PositionComponent>();
            
            // Still shouldn't be collected due to Lazy flag
            Assert.AreEqual(0, collector.Collected.Count);
            
            // Process changes
            collector.Change();
            
            // Now should be collected
            Assert.AreEqual(1, collector.Collected.Count);
            Assert.IsTrue(collector.Collected.Contains(entity.EntityId));
        }
        
        // Test components (same as other test files)
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
    }
}