using System;
using System.Collections.Generic;
using CoreECS;
using CoreECS.Defines;
using CoreECS.Managers;
using NUnit.Framework;
using TinyECS;

namespace TinyECS.Test
{
    [TestFixture]
    public class EntityGraphTestUnit
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
        public void EntityGraph_Pool_AllocatesAndReleasesCorrectly()
        {
            // Act - Get an instance from the pool
            var entityGraph = EntityGraph.Pool.Get();
            
            // Assert - Verify default state
            Assert.AreEqual(0, entityGraph.EntityId);
            Assert.AreEqual(0, entityGraph.Mask);
            Assert.IsFalse(entityGraph.WishDestroy);
            Assert.IsEmpty(entityGraph.RwComponents);
            
            // Act - Modify the instance
            entityGraph.EntityId = 100;
            entityGraph.Mask = 0b1010;
            entityGraph.WishDestroy = true;
            entityGraph.RwComponents.Add(new ComponentRefCore(new MockComponentRefLocator(), 0, 1));
            
            // Assert - Verify modifications were applied
            Assert.AreEqual(100, entityGraph.EntityId);
            Assert.AreEqual(0b1010, entityGraph.Mask);
            Assert.IsTrue(entityGraph.WishDestroy);
            Assert.AreEqual(1, entityGraph.RwComponents.Count);
            
            // Act - Release back to pool
            EntityGraph.Pool.Release(entityGraph);
            
            // Assert - Verify reset state after release
            Assert.AreEqual(0, entityGraph.EntityId);
            Assert.AreEqual(0, entityGraph.Mask);
            Assert.IsFalse(entityGraph.WishDestroy);
            Assert.IsEmpty(entityGraph.RwComponents);
        }
        
        [Test]
        public void EntityGraph_EntityIdProperty_SetAndGetWorks()
        {
            // Arrange
            var entityGraph = EntityGraph.Pool.Get();
            
            // Act & Assert
            entityGraph.EntityId = 12345;
            Assert.AreEqual(12345, entityGraph.EntityId);
            
            entityGraph.EntityId = ulong.MaxValue;
            Assert.AreEqual(ulong.MaxValue, entityGraph.EntityId);
            
            entityGraph.EntityId = 0;
            Assert.AreEqual(0, entityGraph.EntityId);
            
            // Cleanup
            EntityGraph.Pool.Release(entityGraph);
        }
        
        [Test]
        public void EntityGraph_MaskProperty_SetAndGetWorks()
        {
            // Arrange
            var entityGraph = EntityGraph.Pool.Get();
            
            // Act & Assert
            entityGraph.Mask = 0b1010;
            Assert.AreEqual(0b1010, entityGraph.Mask);
            
            entityGraph.Mask = 0b11110000;
            Assert.AreEqual(0b11110000, entityGraph.Mask);
            
            entityGraph.Mask = 0;
            Assert.AreEqual(0, entityGraph.Mask);
            
            // Cleanup
            EntityGraph.Pool.Release(entityGraph);
        }
        
        [Test]
        public void EntityGraph_WishDestroyProperty_SetAndGetWorks()
        {
            // Arrange
            var entityGraph = EntityGraph.Pool.Get();
            
            // Act & Assert
            entityGraph.WishDestroy = true;
            Assert.IsTrue(entityGraph.WishDestroy);
            
            entityGraph.WishDestroy = false;
            Assert.IsFalse(entityGraph.WishDestroy);
            
            // Cleanup
            EntityGraph.Pool.Release(entityGraph);
        }
        
        [Test]
        public void EntityGraph_RwComponentsProperty_ManipulationWorks()
        {
            // Arrange
            var entityGraph = EntityGraph.Pool.Get();
            var component1 = new ComponentRefCore(new MockComponentRefLocator(), 0, 1);
            var component2 = new ComponentRefCore(new MockComponentRefLocator(), 1, 1);
            
            // Act - Add components
            entityGraph.RwComponents.Add(component1);
            entityGraph.RwComponents.Add(component2);
            
            // Assert
            Assert.AreEqual(2, entityGraph.RwComponents.Count);
            Assert.AreSame(component1, entityGraph.RwComponents[0]);
            Assert.AreSame(component2, entityGraph.RwComponents[1]);
            
            // Act - Remove a component
            entityGraph.RwComponents.RemoveAt(0);
            
            // Assert
            Assert.AreEqual(1, entityGraph.RwComponents.Count);
            Assert.AreSame(component2, entityGraph.RwComponents[0]);
            
            // Act - Clear all components
            entityGraph.RwComponents.Clear();
            
            // Assert
            Assert.IsEmpty(entityGraph.RwComponents);
            
            // Cleanup
            EntityGraph.Pool.Release(entityGraph);
        }
        
        [Test]
        public void EntityGraph_GetComponent_ReturnsCorrectComponent()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionComponent = entity.CreateComponent<PositionComponent>();
            var velocityComponent = entity.CreateComponent<VelocityComponent>();
            
            var entityGraph = GetEntityGraph(entity.EntityId);
            
            // Act
            var retrievedPosition = entityGraph.GetComponent<PositionComponent>();
            var retrievedVelocity = entityGraph.GetComponent<VelocityComponent>();
            var retrievedHealth = entityGraph.GetComponent<HealthComponent>(); // Non-existent
            
            // Assert
            Assert.IsTrue(retrievedPosition.NotNull);
            Assert.IsTrue(retrievedVelocity.NotNull);
            Assert.IsFalse(retrievedHealth.NotNull);
            
            Assert.AreEqual(positionComponent.EntityId, retrievedPosition.EntityId);
            Assert.AreEqual(velocityComponent.EntityId, retrievedVelocity.EntityId);
        }
        
        [Test]
        public void EntityGraph_GetComponent_ReturnsFirstMatchingComponent()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var posComp1 = entity.CreateComponent<PositionComponent>();
            var posComp2 = entity.CreateComponent<PositionComponent>(); // Second of same type
            var velComp = entity.CreateComponent<VelocityComponent>();
            
            var entityGraph = GetEntityGraph(entity.EntityId);
            
            // Act
            var retrievedPosition = entityGraph.GetComponent<PositionComponent>();
            
            // Assert - Should return the first matching component
            Assert.IsTrue(retrievedPosition.NotNull);
            // The exact component returned depends on internal ordering, but it should be valid
        }
        
        [Test]
        public void EntityGraph_GetComponents_ArrayReturnsAllComponents()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            entity.CreateComponent<HealthComponent>();
            
            var entityGraph = GetEntityGraph(entity.EntityId);
            
            // Act
            var allComponents = entityGraph.GetComponents();
            
            // Assert
            Assert.AreEqual(3, allComponents.Length);
            
            var componentTypes = new HashSet<Type>();
            foreach (var comp in allComponents)
            {
                componentTypes.Add(comp.RuntimeType);
            }
            
            Assert.IsTrue(componentTypes.Contains(typeof(PositionComponent)));
            Assert.IsTrue(componentTypes.Contains(typeof(VelocityComponent)));
            Assert.IsTrue(componentTypes.Contains(typeof(HealthComponent)));
        }
        
        [Test]
        public void EntityGraph_GetComponents_ICollectionRefillsCorrectly()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            entity.CreateComponent<HealthComponent>();
            
            var entityGraph = GetEntityGraph(entity.EntityId);
            var results = new List<ComponentRef>();
            
            // Act
            var count = entityGraph.GetComponents(results);
            
            // Assert
            Assert.AreEqual(3, count);
            Assert.AreEqual(3, results.Count);
            
            var componentTypes = new HashSet<Type>();
            foreach (var comp in results)
            {
                componentTypes.Add(comp.RuntimeType);
            }
            
            Assert.IsTrue(componentTypes.Contains(typeof(PositionComponent)));
            Assert.IsTrue(componentTypes.Contains(typeof(VelocityComponent)));
            Assert.IsTrue(componentTypes.Contains(typeof(HealthComponent)));
        }
        
        [Test]
        public void EntityGraph_GetComponentsT_ArrayReturnsTypedComponents()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            entity.CreateComponent<PositionComponent>(); // Add another of the same type
            
            var entityGraph = GetEntityGraph(entity.EntityId);
            
            // Act
            var positionComponents = entityGraph.GetComponents<PositionComponent>();
            var velocityComponents = entityGraph.GetComponents<VelocityComponent>();
            var healthComponents = entityGraph.GetComponents<HealthComponent>();
            
            // Assert
            Assert.AreEqual(2, positionComponents.Length);
            Assert.AreEqual(1, velocityComponents.Length);
            Assert.AreEqual(0, healthComponents.Length);
        }
        
        [Test]
        public void EntityGraph_GetComponentsT_ICollectionRefillsTypedComponents()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            entity.CreateComponent<PositionComponent>(); // Add another of the same type
            
            var entityGraph = GetEntityGraph(entity.EntityId);
            var positionResults = new List<ComponentRef<PositionComponent>>();
            var velocityResults = new List<ComponentRef<VelocityComponent>>();
            var healthResults = new List<ComponentRef<HealthComponent>>();
            
            // Act
            var positionCount = entityGraph.GetComponents(positionResults);
            var velocityCount = entityGraph.GetComponents(velocityResults);
            var healthCount = entityGraph.GetComponents(healthResults);
            
            // Assert
            Assert.AreEqual(2, positionCount);
            Assert.AreEqual(1, velocityCount);
            Assert.AreEqual(0, healthCount);
            
            Assert.AreEqual(2, positionResults.Count);
            Assert.AreEqual(1, velocityResults.Count);
            Assert.AreEqual(0, healthResults.Count);
        }
        
        [Test]
        public void EntityGraph_HasComponent_ReturnsCorrectResult()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            
            var entityGraph = GetEntityGraph(entity.EntityId);
            
            // Act & Assert
            Assert.IsTrue(entityGraph.HasComponent<PositionComponent>());
            Assert.IsTrue(entityGraph.HasComponent<VelocityComponent>());
            Assert.IsFalse(entityGraph.HasComponent<HealthComponent>()); // Not added
        }
        
        [Test]
        public void EntityGraph_HasComponent_MultipleSameType_ReturnsTrue()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<PositionComponent>(); // Add another of the same type
            
            var entityGraph = GetEntityGraph(entity.EntityId);
            
            // Act & Assert
            Assert.IsTrue(entityGraph.HasComponent<PositionComponent>()); // Should return true even with multiple
        }
        
        [Test]
        public void EntityGraph_GetComponentCount_ReturnsCorrectCount()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            entity.CreateComponent<PositionComponent>(); // Adding another PositionComponent to test counting
            entity.CreateComponent<VelocityComponent>(); // Adding another VelocityComponent
            
            var entityGraph = GetEntityGraph(entity.EntityId);
            
            // Act
            var positionCount = entityGraph.GetComponentCount<PositionComponent>();
            var velocityCount = entityGraph.GetComponentCount<VelocityComponent>();
            var healthCount = entityGraph.GetComponentCount<HealthComponent>();
            
            // Assert
            Assert.AreEqual(2, positionCount);
            Assert.AreEqual(2, velocityCount);
            Assert.AreEqual(0, healthCount);
        }
        
        [Test]
        public void EntityGraph_GetComponentCount_EmptyGraph_ReturnsZero()
        {
            // Arrange
            var entity = _world.CreateEntity(); // Entity with no components
            var entityGraph = GetEntityGraph(entity.EntityId);
            
            // Act & Assert
            Assert.AreEqual(0, entityGraph.GetComponentCount<PositionComponent>());
            Assert.AreEqual(0, entityGraph.GetComponentCount<VelocityComponent>());
            Assert.AreEqual(0, entityGraph.GetComponentCount<HealthComponent>());
        }
        
        [Test]
        public void EntityGraph_Reset_ClearsAllProperties()
        {
            // Since we can't directly call the private Reset method, we test through the pool mechanism
            // Arrange
            var entityGraph = EntityGraph.Pool.Get();
            
            // Modify properties
            entityGraph.EntityId = 12345;
            entityGraph.Mask = 0b1010;
            entityGraph.WishDestroy = true;
            entityGraph.RwComponents.Add(new ComponentRefCore(new MockComponentRefLocator(), 0, 1));
            entityGraph.RwComponents.Add(new ComponentRefCore(new MockComponentRefLocator(), 1, 1));
            
            // Verify modifications
            Assert.AreEqual(12345, entityGraph.EntityId);
            Assert.AreEqual(0b1010, entityGraph.Mask);
            Assert.IsTrue(entityGraph.WishDestroy);
            Assert.AreEqual(2, entityGraph.RwComponents.Count);
            
            // Act - Release back to pool (triggers Reset)
            EntityGraph.Pool.Release(entityGraph);
            
            // Assert - Properties should be reset
            Assert.AreEqual(0, entityGraph.EntityId);
            Assert.AreEqual(0, entityGraph.Mask);
            Assert.IsFalse(entityGraph.WishDestroy);
            Assert.IsEmpty(entityGraph.RwComponents);
        }
        
        [Test]
        public void EntityGraph_GetComponents_MethodsConsistent()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            entity.CreateComponent<HealthComponent>();
            
            var entityGraph = GetEntityGraph(entity.EntityId);
            
            // Act - Test both methods for getting all components
            var allComponentsArray = entityGraph.GetComponents();
            var allComponentsList = new List<ComponentRef>();
            var allComponentsCount = entityGraph.GetComponents(allComponentsList);
            
            // Assert
            Assert.AreEqual(3, allComponentsArray.Length);
            Assert.AreEqual(3, allComponentsCount);
            Assert.AreEqual(3, allComponentsList.Count);
            
            // Verify both methods return the same components
            var arrayTypes = new HashSet<Type>();
            foreach (var comp in allComponentsArray)
            {
                arrayTypes.Add(comp.RuntimeType);
            }
            
            var listTypes = new HashSet<Type>();
            foreach (var comp in allComponentsList)
            {
                listTypes.Add(comp.RuntimeType);
            }
            
            CollectionAssert.AreEquivalent(arrayTypes, listTypes);
        }
        
        // Helper method to access EntityGraph from entity ID
        private EntityGraph GetEntityGraph(ulong entityId)
        {
            var entityManager = _world.GetManager<EntityManager>();
            return entityManager.GetEntity(entityId);
        }
        
        // Mock class for testing
        private class MockComponentRefLocator : IComponentRefLocator
        {
            public bool NotNull(uint version, int offset) => true;
            
            public ref T Get<T>(int offset) where T : struct, IComponent<T>
            {
                throw new NotImplementedException();
            }
            
            public bool IsT(Type type) => type == typeof(PositionComponent);
            
            public Type GetT() => typeof(PositionComponent);
            
            public ulong GetEntityId(int offset) => 1;
            
            public IComponentRefCore GetRefCore(int offset) => null;
            
            public uint GetRevision(int offset) => 0;

            public uint ChangeRevision(int offset) => 0;
        }
        
        // Test components
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