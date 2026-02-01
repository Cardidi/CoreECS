using System;
using System.Collections.Generic;
using NUnit.Framework;
using TinyECS;
using TinyECS.Defines;
using TinyECS.Managers;

namespace TinyECS.Test
{
    [TestFixture]
    public class ComponentManagerTestUnit
    {
        private World _world;
        private ComponentManager _componentManager;
        
        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
            _componentManager = _world.GetManager<ComponentManager>();
        }
        
        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
        }
        
        [Test]
        public void ComponentManager_GetComponentStore_CreatesNewStoreIfNotExists()
        {
            // Arrange - No setup needed, store should be created on demand
            
            // Act
            var store = _componentManager.GetComponentStore<PositionComponent>();
            
            // Assert
            Assert.IsNotNull(store);
            Assert.AreEqual(0, store.Allocated);
            Assert.AreEqual(100, store.Capacity); // Default initial size
        }
        
        [Test]
        public void ComponentManager_GetComponentStore_ReturnsSameInstanceForSameType()
        {
            // Act
            var store1 = _componentManager.GetComponentStore<PositionComponent>();
            var store2 = _componentManager.GetComponentStore<PositionComponent>();
            
            // Assert
            Assert.AreSame(store1, store2);
        }
        
        [Test]
        public void ComponentManager_GetComponentStore_GenericAndNonGeneric_ReturnSameStore()
        {
            // Act
            var genericStore = _componentManager.GetComponentStore<PositionComponent>();
            var nonGenericStore = _componentManager.GetComponentStore(typeof(PositionComponent));
            
            // Assert
            Assert.IsNotNull(genericStore);
            Assert.IsNotNull(nonGenericStore);
            Assert.AreEqual(typeof(PositionComponent), nonGenericStore.RefLocator.GetT());
        }
        
        [Test]
        public void ComponentManager_CreateComponent_AddsComponentSuccessfully()
        {
            // Arrange
            var entity = _world.CreateEntity();
            
            // Act
            var componentCore = _componentManager.CreateComponent<PositionComponent>(entity.EntityId);
            
            // Assert
            Assert.IsNotNull(componentCore);
            Assert.IsNotNull(componentCore.RefLocator);
            Assert.AreEqual(typeof(PositionComponent), componentCore.RefLocator.GetT());
            Assert.AreEqual(entity.EntityId, componentCore.RefLocator.GetEntityId(componentCore.Offset));
            
            var store = _componentManager.GetComponentStore<PositionComponent>();
            Assert.AreEqual(1, store.Allocated);
        }
        
        [Test]
        public void ComponentManager_DestroyComponent_RemovesComponentSuccessfully()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentCore = _componentManager.CreateComponent<PositionComponent>(entity.EntityId);
            var store = _componentManager.GetComponentStore<PositionComponent>();
            
            // Verify component was created
            Assert.AreEqual(1, store.Allocated);
            
            // Act
            _componentManager.DestroyComponent(componentCore);
            _componentManager.CleanupComponents();
            
            // Assert
            Assert.AreEqual(0, store.Allocated);
        }
        
        [Test]
        public void ComponentManager_DestroyComponent_ThrowsOnAlreadyDestroyedComponent()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentCore = _componentManager.CreateComponent<PositionComponent>(entity.EntityId);
            
            // First destroy
            _componentManager.DestroyComponent(componentCore);
            
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => {
                _componentManager.DestroyComponent(componentCore);
            });
        }
        
        [Test]
        public void ComponentManager_GetAllComponentStores_ReturnsCorrectStores()
        {
            // Arrange - Create several different component types
            var entity = _world.CreateEntity();
            _componentManager.CreateComponent<PositionComponent>(entity.EntityId);
            _componentManager.CreateComponent<VelocityComponent>(entity.EntityId);
            _componentManager.CreateComponent<HealthComponent>(entity.EntityId);
            
            // Act
            var allStores = _componentManager.GetAllComponentStores();
            
            // Assert
            var storesList = new List<ComponentStore>(allStores);
            Assert.AreEqual(3, storesList.Count);
            
            var hasPositionStore = false;
            var hasVelocityStore = false;
            var hasHealthStore = false;
            
            foreach (var store in storesList)
            {
                var type = store.RefLocator.GetT();
                if (type == typeof(PositionComponent)) hasPositionStore = true;
                if (type == typeof(VelocityComponent)) hasVelocityStore = true;
                if (type == typeof(HealthComponent)) hasHealthStore = true;
            }
            
            Assert.IsTrue(hasPositionStore);
            Assert.IsTrue(hasVelocityStore);
            Assert.IsTrue(hasHealthStore);
        }
        
        [Test]
        public void ComponentManager_ComponentCreatedEvent_IsTriggered()
        {
            // Arrange
            var eventTriggered = false;
            IComponentRefCore capturedCore = null;
            ulong capturedEntityId = 0;
            
            _componentManager.OnComponentCreated.Add((core, entityId) => {
                eventTriggered = true;
                capturedCore = core;
                capturedEntityId = entityId;
            });
            
            var entity = _world.CreateEntity();
            
            // Act
            var componentCore = _componentManager.CreateComponent<PositionComponent>(entity.EntityId);
            
            // Assert
            Assert.IsTrue(eventTriggered);
            Assert.AreSame(componentCore, capturedCore);
            Assert.AreEqual(entity.EntityId, capturedEntityId);
        }
        
        [Test]
        public void ComponentManager_ComponentRemovedEvent_IsTriggered()
        {
            // Arrange
            var eventTriggered = false;
            IComponentRefCore capturedCore = null;
            ulong capturedEntityId = 0;
            
            _componentManager.OnComponentRemoved.Add((core, entityId) => {
                eventTriggered = true;
                capturedCore = core;
                capturedEntityId = entityId;
            });
            
            var entity = _world.CreateEntity();
            var componentCore = _componentManager.CreateComponent<PositionComponent>(entity.EntityId);
            
            // Reset flags after creation event
            eventTriggered = false;
            capturedCore = null;
            capturedEntityId = 0;
            
            // Act
            _componentManager.DestroyComponent(componentCore);
            
            // Assert
            Assert.IsTrue(eventTriggered);
            Assert.AreSame(componentCore, capturedCore);
            Assert.AreEqual(entity.EntityId, capturedEntityId);
        }
        
        [Test]
        public void ComponentManager_ComponentStore_CapacityExpansionWorks()
        {
            // Arrange
            var store = _componentManager.GetComponentStore<PositionComponent>();
            var initialCapacity = store.Capacity;
            
            // Act - Add more components than initial capacity to trigger expansion
            var entityIds = new List<ulong>();
            for (int i = 0; i < initialCapacity + 10; i++)
            {
                var entity = _world.CreateEntity();
                var core = _componentManager.CreateComponent<PositionComponent>(entity.EntityId);
                entityIds.Add(entity.EntityId);
            }
            
            // Assert
            Assert.Greater(store.Capacity, initialCapacity);
            Assert.AreEqual(initialCapacity + 10, store.Allocated);
        }
        
        [Test]
        public void ComponentManager_ComponentStore_ExpandMethod_IncreasesCapacity()
        {
            // Arrange
            var store = _componentManager.GetComponentStore<PositionComponent>();
            var initialCapacity = store.Capacity;
            var expandAmount = 50;
            
            // Act
            var expandedAmount = store.Expand(expandAmount);
            
            // Assert
            Assert.AreEqual(expandAmount, expandedAmount);
            Assert.AreEqual(initialCapacity + expandAmount, store.Capacity);
        }
        
        [Test]
        public void ComponentManager_MultipleComponentTypes_ManagedSeparately()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            
            // Act
            var posCore1 = _componentManager.CreateComponent<PositionComponent>(entity1.EntityId);
            var velCore1 = _componentManager.CreateComponent<VelocityComponent>(entity1.EntityId);
            var posCore2 = _componentManager.CreateComponent<PositionComponent>(entity2.EntityId);
            
            // Assert
            var posStore = _componentManager.GetComponentStore<PositionComponent>();
            var velStore = _componentManager.GetComponentStore<VelocityComponent>();
            
            Assert.AreEqual(2, posStore.Allocated);
            Assert.AreEqual(1, velStore.Allocated);
            
            // Verify components are in correct stores
            Assert.AreEqual(typeof(PositionComponent), posCore1.RefLocator.GetT());
            Assert.AreEqual(typeof(VelocityComponent), velCore1.RefLocator.GetT());
            Assert.AreEqual(typeof(PositionComponent), posCore2.RefLocator.GetT());
        }
        
        [Test]
        public void ComponentManager_ComponentStore_CorrectlyTracksComponents()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var store = _componentManager.GetComponentStore<PositionComponent>();
            
            // Act
            var core1 = _componentManager.CreateComponent<PositionComponent>(entity.EntityId);
            var core2 = _componentManager.CreateComponent<PositionComponent>(_world.CreateEntity().EntityId);
            var core3 = _componentManager.CreateComponent<PositionComponent>(_world.CreateEntity().EntityId);
            
            // Verify all are allocated
            Assert.AreEqual(3, store.Allocated);
            
            // Remove one in the middle
            _componentManager.DestroyComponent(core2);
            
            // Assert
            Assert.AreEqual(3, store.Allocated);
            
            // Remove first
            _componentManager.DestroyComponent(core1);
            _componentManager.CleanupComponents();
            
            // Assert
            Assert.AreEqual(1, store.Allocated);
            
            // Remove last
            _componentManager.DestroyComponent(core3);
            _componentManager.CleanupComponents();
            
            // Assert
            Assert.AreEqual(0, store.Allocated);
        }
        
        [Test]
        public void ComponentManager_GetComponentStore_WithCreateIfNotExistFalse_ReturnsNullIfNotExists()
        {
            // Act
            var store = _componentManager.GetComponentStore<PositionComponent>(createIfNotExist: false);
            
            // Assert
            Assert.IsNull(store);
        }
        
        [Test]
        public void ComponentManager_GetComponentStore_NonGenericWithCreateIfNotExistFalse_ReturnsNullIfNotExists()
        {
            // Act
            var store = _componentManager.GetComponentStore(typeof(PositionComponent), createIfNotExist: false);
            
            // Assert
            Assert.IsNull(store);
        }
        
        [Test]
        public void ComponentManager_ComponentLifecycle_CallbacksAreCalled()
        {
            // Arrange
            var entity = _world.CreateEntity();
            IComponentRefCore capturedCore = null;
            ulong capturedEntityId = 0;
            var destroyEventTriggered = false;
            
            _componentManager.OnComponentRemoved.Add((core, entityId) => {
                destroyEventTriggered = true;
                capturedCore = core;
                capturedEntityId = entityId;
            });
            
            // Act - Create component
            var componentCore = _componentManager.CreateComponent<LifecycleComponent>(entity.EntityId);
            var componentRef = new ComponentRef<LifecycleComponent>(componentCore);
            
            // Assert - Creation callback should be called
            Assert.IsTrue(componentRef.RW.OnCreateCalled);
            Assert.IsFalse(componentRef.RW.OnDestroyCalled);
            
            // Act - Destroy component
            _componentManager.DestroyComponent(componentCore);
            
            // Assert - Destruction callback should be called
            Assert.IsTrue(destroyEventTriggered);
        }
        
        
                [Test]
        public void ComponentManager_ComponentRefCache_AfterDestroyingSameTypeComponents()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var entity3 = _world.CreateEntity();
            
            // Create multiple PositionComponent instances (same type)
            var posCore1 = _componentManager.CreateComponent<PositionComponent>(entity1.EntityId);
            var posCore2 = _componentManager.CreateComponent<PositionComponent>(entity2.EntityId);
            var posCore3 = _componentManager.CreateComponent<PositionComponent>(entity3.EntityId);
            
            // Create component references
            var posRef1 = new ComponentRef<PositionComponent>(posCore1);
            var posRef2 = new ComponentRef<PositionComponent>(posCore2);
            var posRef3 = new ComponentRef<PositionComponent>(posCore3);
            
            // Set initial values for each component
            posRef1.RW.X = 10.0f;
            posRef1.RW.Y = 20.0f;
            
            posRef2.RW.X = 30.0f;
            posRef2.RW.Y = 40.0f;
            
            posRef3.RW.X = 50.0f;
            posRef3.RW.Y = 60.0f;
            
            // Act - Use ref var cached = ref component.RW to reference the first position component
            ref var cachedPosition = ref posRef2.RW;
            
            // Modify the cached value
            cachedPosition.X = 70.0f;
            cachedPosition.Y = 80.0f;
            
            // Assert - Check if the cached position value remains unchanged
            Assert.AreEqual(70.0f, cachedPosition.X);
            Assert.AreEqual(80.0f, cachedPosition.Y);
            
            // Verify the original component reference still has the same value
            Assert.AreEqual(70.0f, posRef2.RW.X);
            Assert.AreEqual(80.0f, posRef2.RW.Y);
            
            // Remove components and trigger rearrangement
            _componentManager.DestroyComponent(posCore1);
            _componentManager.DestroyComponent(posCore3);
            _componentManager.GetComponentStore<PositionComponent>().Rearrange();
            
            // Modify the cached value
            cachedPosition.X = 75.0f;
            cachedPosition.Y = 85.0f;
            
            // Assert - Check if the cached position value remains unchanged
            Assert.AreEqual(75.0f, cachedPosition.X);
            Assert.AreEqual(85.0f, cachedPosition.Y);
            
            // Verify the original component reference still has the same value
            Assert.AreEqual(70.0f, posRef2.RW.X);
            Assert.AreEqual(80.0f, posRef2.RW.Y);
            
            // Verify other PositionComponents are destroyed
            Assert.IsFalse(posRef1.NotNull);
            Assert.IsFalse(posRef3.NotNull);
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
        
        private struct LifecycleComponent : IComponent<LifecycleComponent>
        {
            public bool OnCreateCalled;
            public bool OnDestroyCalled;
            
            public void OnCreate(ulong entityId)
            {
                OnCreateCalled = true;
            }
            
            public void OnDestroy(ulong entityId)
            {
                OnDestroyCalled = true;
            }
        }
    }
}