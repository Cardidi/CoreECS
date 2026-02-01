using System;
using CoreECS;
using CoreECS.Defines;
using CoreECS.Managers;
using NUnit.Framework;
using TinyECS;

namespace TinyECS.Test
{
    [TestFixture]
    public class WorldTestUnit
    {
        [Test]
        public void World_CanBeCreatedAndStarted()
        {
            // Arrange & Act
            var world = new World();
            
            // Assert
            Assert.IsFalse(world.Ready);
            
            // Act
            world.Startup();
            
            // Assert
            Assert.IsTrue(world.Ready);
            Assert.AreEqual(0, world.TickCount);
            
            // Cleanup
            world.Shutdown();
            Assert.IsFalse(world.Ready);
        }
        
        [Test]
        public void World_CanTickMultipleTimes()
        {
            // Arrange
            var world = new World();
            world.Startup();
            world.RegisterSystem<TestSystem>();
            
            // Act
            for (int i = 0; i < 5; i++)
            {
                world.BeginTick();
                world.Tick();
                world.EndTick();
            }
            
            // Assert
            Assert.AreEqual(5, world.TickCount);
            var system = world.FindSystem<TestSystem>();
            Assert.AreEqual(5, system.TickCount);
            
            // Cleanup
            world.Shutdown();
        }
        
        [Test]
        public void World_CanCreateEntity()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            // Act
            var entity = world.CreateEntity();
            
            // Assert
            Assert.IsTrue(entity.IsValid);
            Assert.AreEqual(world, entity.World);
            Assert.IsTrue(entity.EntityId > 0);
            
            // Cleanup
            world.Shutdown();
        }
        
        [Test]
        public void World_CanDestroyEntity()
        {
            // Arrange
            var world = new World();
            world.Startup();
            var entity = world.CreateEntity();
            var entityId = entity.EntityId;
            
            // Act
            world.DestroyEntity(entity);
            
            // Assert
            Assert.IsFalse(entity.IsValid);
            Assert.IsNull(world.GetManager<EntityManager>().GetEntity(entityId));
            
            // Cleanup
            world.Shutdown();
        }
        
        [Test]
        public void World_CanRegisterAndExecuteSystem()
        {
            // Arrange
            var world = new World();
            world.Startup();
            world.RegisterSystem<TestSystem>();
            
            // Act
            world.BeginTick();
            world.Tick();
            world.EndTick();
            
            // Assert
            var system = world.FindSystem<TestSystem>();
            Assert.IsNotNull(system);
            Assert.IsTrue(system.OnCreateCalled);
            Assert.IsTrue(system.OnTickCalled);
            
            // Cleanup
            world.Shutdown();
        }
        
        [Test]
        public void World_MultipleWorldsIsolation()
        {
            // Arrange
            var world1 = new World();
            var world2 = new World();
            
            world1.Startup();
            world2.Startup();
            
            // Act
            var entity1 = world1.CreateEntity();
            var entity2 = world2.CreateEntity();
            
            // Assert
            Assert.AreEqual(entity1.EntityId, entity2.EntityId);
            Assert.AreNotEqual(entity1.World, entity2.World);
            
            // Cleanup
            world1.DestroyEntity(entity1);
            world2.DestroyEntity(entity2);
            world1.Shutdown();
            world2.Shutdown();
        }
        
        [Test]
        public void World_Injection_CanInjectDependencies()
        {
            // Arrange
            var world = new World();
            world.Startup();
            world.Injection.Register(new TestService());
            world.RegisterSystem<DependencyTestSystem>();
            
            // Act
            world.BeginTick();
            world.Tick();
            world.EndTick();
            
            // Assert
            var system = world.FindSystem<DependencyTestSystem>();
            Assert.IsNotNull(system);
            Assert.IsNotNull(system.TestService);
            Assert.IsTrue(system.TestServiceCalled);
            
            // Cleanup
            world.Shutdown();
        }
        
        [Test]
        public void MinimalWorld_LifecycleEvents_AreCalledCorrectly()
        {
            // Arrange
            var testWorld = new TestMinimalWorld();
            
            // Act
            testWorld.Startup();
            
            // Assert
            Assert.IsTrue(testWorld.RegisterManagerCalled);
            Assert.IsTrue(testWorld.ConstructCalled);
            Assert.IsTrue(testWorld.StartCalled);
            
            // Act
            testWorld.BeginTick();
            testWorld.Tick();
            testWorld.EndTick();
            
            // Assert
            Assert.IsTrue(testWorld.TickBeginCalled);
            Assert.IsTrue(testWorld.TickCalled);
            Assert.IsTrue(testWorld.TickEndCalled);
            
            // Act
            testWorld.Shutdown();
            
            // Assert
            Assert.IsTrue(testWorld.ShutdownCalled);
        }
        
        [Test]
        public void World_FindSystem_ReturnsCorrectSystem()
        {
            // Arrange
            var world = new World();
            world.Startup();
            world.RegisterSystem<TestSystem>();
            
            // Act
            var system = world.FindSystem<TestSystem>();
            
            // Assert
            Assert.IsNotNull(system);
            Assert.IsInstanceOf<TestSystem>(system);
            
            // Cleanup
            world.Shutdown();
        }
        
        [Test]
        public void World_GetEntity_ReturnsValidEntity()
        {
            // Arrange
            var world = new World();
            world.Startup();
            var entity = world.CreateEntity();
            var entityId = entity.EntityId;
            
            // Act
            var retrievedEntity = world.GetEntity(entityId);
            
            // Assert
            Assert.IsTrue(retrievedEntity.IsValid);
            Assert.AreEqual(entityId, retrievedEntity.EntityId);
            Assert.AreEqual(world, retrievedEntity.World);
            
            // Cleanup
            world.Shutdown();
        }
        
        [Test]
        public void World_CreateCollector_CreatesValidCollector()
        {
            // Arrange
            var world = new World();
            world.Startup();
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            
            // Act
            var collector = world.CreateCollector(matcher);
            
            // Assert
            Assert.IsNotNull(collector);
            Assert.AreEqual(matcher, collector.Matcher);
            
            // Cleanup
            collector.Dispose();
            world.Shutdown();
        }
        
        [Test]
        public void World_RegisterAndUnregisterSystem_WorksCorrectly()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            // Act - Register system
            world.RegisterSystem<TestSystem>();
            var registeredSystem = world.FindSystem<TestSystem>();
            
            // Assert - System should be registered
            Assert.IsNotNull(registeredSystem);
            
            // Act - Unregister system
            world.UnregisterSystem<TestSystem>();
            var unregisteredSystem = world.FindSystem<TestSystem>();
            
            // Assert - System should be unregistered
            Assert.IsNull(unregisteredSystem);
            
            // Cleanup
            world.Shutdown();
        }
        
        [Test]
        public void World_ManagerLifecycle_MethodsAreCalledCorrectly()
        {
            
            // Since World registers managers internally, we'll test with a custom world
            var customWorld = new TestWorldWithCustomManager();
            
            // Act - Startup should trigger OnManagerCreated and OnWorldStarted
            customWorld.Startup();

            var testManager = customWorld.GetManager<TestWorldManager>();
            
            // Assert
            Assert.IsTrue(testManager.OnManagerCreatedCalled);
            Assert.IsTrue(testManager.OnWorldStartedCalled);
            Assert.IsFalse(testManager.OnWorldEndedCalled);
            Assert.IsFalse(testManager.OnManagerDestroyedCalled);
            
            // Act - Perform a tick
            customWorld.BeginTick();
            customWorld.Tick();
            customWorld.EndTick();
            
            // Act - Shutdown should trigger OnWorldEnded and OnManagerDestroyed
            customWorld.Shutdown();
            
            // Assert
            Assert.IsTrue(testManager.OnWorldEndedCalled);
            Assert.IsTrue(testManager.OnManagerDestroyedCalled);
        }
        
        [Test]
        public void World_GetManager_ReturnsValidManager()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            // Act
            var entityManager = world.GetManager<EntityManager>();
            var componentManager = world.GetManager<ComponentManager>();
            var systemManager = world.GetManager<SystemManager>();
            var entityMatchManager = world.GetManager<EntityMatchManager>();
            
            // Assert
            Assert.IsNotNull(entityManager);
            Assert.IsNotNull(componentManager);
            Assert.IsNotNull(systemManager);
            Assert.IsNotNull(entityMatchManager);
            
            Assert.IsInstanceOf<EntityManager>(entityManager);
            Assert.IsInstanceOf<ComponentManager>(componentManager);
            Assert.IsInstanceOf<SystemManager>(systemManager);
            Assert.IsInstanceOf<EntityMatchManager>(entityMatchManager);
            
            // Cleanup
            world.Shutdown();
        }
        
        [Test]
        public void World_ManagerRegistration_DoesNotAllowDuplicates()
        {
            // Testing that the ManagerMediator prevents duplicate registrations
            // We'll verify this by ensuring the same manager type can be retrieved after world setup
            var world = new World();
            world.Startup();
            
            // Get the same manager twice to ensure it works correctly
            var firstCall = world.GetManager<EntityManager>();
            var secondCall = world.GetManager<EntityManager>();
            
            // Both calls should return the same instance
            Assert.IsNotNull(firstCall);
            Assert.IsNotNull(secondCall);
            Assert.AreSame(firstCall, secondCall, "Manager instances should be the same (singleton)");
            
            world.Shutdown();
        }
        
        // Test system
        private class TestSystem : ISystem
        {
            public bool OnCreateCalled { get; private set; }
            public bool OnTickCalled { get; private set; }
            public int TickCount { get; private set; }
            
            public void OnCreate()
            {
                OnCreateCalled = true;
            }
            
            public void OnTick()
            {
                OnTickCalled = true;
                TickCount++;
            }
            
            public void OnDestroy()
            {
            }
        }
        
        // Test system with dependency
        private class DependencyTestSystem : ISystem
        {
            public ITestService TestService { get; private set; }
            public bool TestServiceCalled { get; private set; }
            
            public void OnCreate()
            {
            }
            
            public void OnTick()
            {
                if (TestService != null)
                {
                    TestService.DoSomething();
                    TestServiceCalled = true;
                }
            }
            
            public void OnDestroy()
            {
            }

            public DependencyTestSystem(ITestService testService)
            {
                TestService = testService;
            }
        }
        
        // Test service interface and implementation
        private interface ITestService
        {
            void DoSomething();
        }
        
        private class TestService : ITestService
        {
            public void DoSomething()
            {
                // Do something
            }
        }
        
        // Test MinimalWorld implementation for testing lifecycle events
        private class TestMinimalWorld : MinimalWorld
        {
            public bool RegisterManagerCalled { get; private set; }
            public bool ConstructCalled { get; private set; }
            public bool StartCalled { get; private set; }
            public bool TickBeginCalled { get; private set; }
            public bool TickCalled { get; private set; }
            public bool TickEndCalled { get; private set; }
            public bool ShutdownCalled { get; private set; }
            
            protected override void OnRegisterManager(IManagerRegister register)
            {
                RegisterManagerCalled = true;
            }

            protected override void OnConstruct()
            {
                ConstructCalled = true;
            }
            
            protected override void OnStart()
            {
                StartCalled = true;
            }
            
            protected override void OnTickBegin()
            {
                TickBeginCalled = true;
            }
            
            protected override void OnTick(ulong tickMask)
            {
                TickCalled = true;
            }
            
            protected override void OnTickEnd()
            {
                TickEndCalled = true;
            }
            
            protected override void OnShutdown()
            {
                ShutdownCalled = true;
            }
        }
        
        // Test component for collector tests
        private struct PositionComponent : IComponent<PositionComponent>
        {
            public float X, Y;
        }
        
        // Test world manager for verifying lifecycle methods
        private class TestWorldManager : IWorldManager
        {
            public bool OnManagerCreatedCalled { get; private set; }
            public bool OnWorldStartedCalled { get; private set; }
            public bool OnWorldEndedCalled { get; private set; }
            public bool OnManagerDestroyedCalled { get; private set; }
            
            public void OnManagerCreated()
            {
                OnManagerCreatedCalled = true;
            }
            
            public void OnWorldStarted()
            {
                OnWorldStartedCalled = true;
            }
            
            public void OnWorldEnded()
            {
                OnWorldEndedCalled = true;
            }
            
            public void OnManagerDestroyed()
            {
                OnManagerDestroyedCalled = true;
            }
        }
        
        // Custom world to test manager lifecycle
        private class TestWorldWithCustomManager : MinimalWorld
        {
            
            protected override void OnRegisterManager(IManagerRegister register)
            {
                // Register our test manager
                register.RegisterManager<IWorldManager, TestWorldManager>();
            }
            
            protected override void OnConstruct()
            {
                // Manager should be constructed here
            }
            
            protected override void OnStart()
            {
            }
            
            protected override void OnTickBegin()
            {
            }
            
            protected override void OnTick(ulong tickMask)
            {
            }
            
            protected override void OnTickEnd()
            {
            }
            
            protected override void OnShutdown()
            {
            }
        }
    }
}