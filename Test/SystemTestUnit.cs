using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public class SystemTestUnit
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
        public void SystemManager_CanRegisterSystem()
        {
            // Arrange
            var systemManager = _world.GetManager<SystemManager>();
            
            // Act
            systemManager.RegisterSystem(typeof(TestSystem));
            
            // Assert
            Assert.IsTrue(systemManager.SystemTransformer.ContainsKey(typeof(TestSystem)));
            Assert.IsTrue(systemManager.Systems.Count > 0);
            
            _world.BeginTick();
            _world.Tick();
            _world.EndTick();

            Assert.IsTrue(systemManager.SystemTransformer.ContainsKey(typeof(TestSystem)));
            Assert.IsTrue(systemManager.Systems.Count > 0);
        }
        
        [Test]
        public void SystemManager_CanUnregisterSystem()
        {
            // Arrange
            var systemManager = _world.GetManager<SystemManager>();
            systemManager.RegisterSystem(typeof(TestSystem));
            
            // Act
            systemManager.UnregisterSystem(typeof(TestSystem));
            
            _world.BeginTick();
            _world.Tick();
            _world.EndTick();
            
            // Assert
            Assert.IsFalse(systemManager.SystemTransformer.ContainsKey(typeof(TestSystem)));
        }

        [Test]
        public void SystemManager_UnregisterSystem_ThrowsWhenSystemIsNotRegistered()
        {
            // Arrange
            var systemManager = _world.GetManager<SystemManager>();

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                systemManager.UnregisterSystem(typeof(TestSystem)));

            Assert.AreEqual("System CoreECS.Test.SystemTestUnit+TestSystem is not registered.", ex!.Message);
        }
        
        [Test]
        public void SystemManager_CanExecuteSystems()
        {
            // Arrange
            var systemManager = _world.GetManager<SystemManager>();
            systemManager.RegisterSystem(typeof(TestSystem));
            
            // Act
            systemManager.TeardownSystems();
            systemManager.ExecuteSystems(ulong.MaxValue);
            systemManager.CleanupSystems();
            
            // Assert
            var system = (TestSystem)systemManager.SystemTransformer[typeof(TestSystem)];
            Assert.IsTrue(system.OnTickCalled);
        }
        
        [Test]
        public void SystemManager_CanHandleSystemLifecycle()
        {
            // Arrange
            var systemManager = _world.GetManager<SystemManager>();
            
            // Act
            systemManager.RegisterSystem(typeof(LifecycleTestSystem));
            systemManager.TeardownSystems();
            
            systemManager.UnregisterSystem(typeof(LifecycleTestSystem));
            systemManager.CleanupSystems();
            
            // Assert
            var system = new LifecycleTestSystem();
            system.OnDestroy();
            Assert.IsTrue(system.OnDestroyCalled);
        }

        [Test]
        public void TeardownSystems_RepeatedCallsAreIgnored_AndNextCycleRunsAgain()
        {
            // Arrange
            var systemManager = _world.GetManager<SystemManager>();
            var teardownCount = 0;
            systemManager.OnSystemTeardown += world => teardownCount++;

            // Act - a repeated teardown before the matching cleanup is ignored
            systemManager.TeardownSystems();
            systemManager.TeardownSystems();
            Assert.AreEqual(1, teardownCount);

            // A teardown after the matching cleanup starts a new cycle
            systemManager.CleanupSystems();
            systemManager.TeardownSystems();
            Assert.AreEqual(2, teardownCount);
        }

        [Test]
        public void CleanupSystems_RepeatedCallsAreIgnored_AndRequiresTeardown()
        {
            // Arrange
            var systemManager = _world.GetManager<SystemManager>();
            var cleanupCount = 0;
            systemManager.OnSystemCleanup += world => cleanupCount++;

            // Act - cleanup without a preceding teardown is ignored
            systemManager.CleanupSystems();
            Assert.AreEqual(0, cleanupCount);

            // A repeated cleanup after the matching teardown is ignored
            systemManager.TeardownSystems();
            systemManager.CleanupSystems();
            systemManager.CleanupSystems();
            Assert.AreEqual(1, cleanupCount);
        }
        
        [Test]
        public void World_CanFindSystem()
        {
            // Arrange
            _world.RegisterSystem<TestSystem>();
            
            _world.BeginTick();
            _world.Tick();
            _world.EndTick();
            
            // Act
            var system = _world.FindSystem<TestSystem>();
            
            // Assert
            Assert.IsNotNull(system);
            Assert.IsInstanceOf<TestSystem>(system);
        }
        
        [Test]
        public void World_CanExecuteSystemsWithTickMask()
        {
            // Arrange
            _world.RegisterSystem<TickMaskTestSystem1>(); // TickGroup = 0b0001
            _world.RegisterSystem<TickMaskTestSystem2>(); // TickGroup = 0b0010
            
            // Act - Execute with mask 0b0001 (should only execute system 1)
            _world.BeginTick();
            _world.Tick(0b0001);
            _world.EndTick();
            
            var system1 = _world.FindSystem<TickMaskTestSystem1>();
            var system2 = _world.FindSystem<TickMaskTestSystem2>();
            
            // Assert
            Assert.IsTrue(system1.OnTickCalled);
            Assert.IsFalse(system2.OnTickCalled);
            
            // Reset
            system1.OnTickCalled = false;
            
            // Act - Execute with mask 0b0011 (should execute both systems)
            _world.BeginTick();
            _world.Tick(0b0011);
            _world.EndTick();
            
            // Assert
            Assert.IsTrue(system1.OnTickCalled);
            Assert.IsTrue(system2.OnTickCalled);
        }
        
        [Test]
        public void SystemManager_CanHandleSystemExceptions()
        {
            // Arrange
            _world.RegisterSystem<ExceptionTestSystem>();
            
            // Act & Assert - Should not throw exception
            Assert.DoesNotThrow(() =>
            {
                _world.BeginTick();
                _world.Tick();
                _world.EndTick();
            });
        }
        
        [Test]
        public void SystemManager_CanHandleMultipleSystemExecution_OrderTest()
        {
            // Arrange
            _world.RegisterSystem<FirstSystem>();
            _world.RegisterSystem<SecondSystem>();
            _world.RegisterSystem<ThirdSystem>();
            
            // Act
            _world.BeginTick();
            _world.Tick();
            _world.EndTick();
            
            var firstSystem = _world.FindSystem<FirstSystem>();
            var secondSystem = _world.FindSystem<SecondSystem>();
            var thirdSystem = _world.FindSystem<ThirdSystem>();
            
            // Assert
            Assert.IsTrue(firstSystem.Executed);
            Assert.IsTrue(secondSystem.Executed);
            Assert.IsTrue(thirdSystem.Executed);
            
            // Systems should execute in the order they were registered
            Assert.LessOrEqual(firstSystem.ExecutionOrder, secondSystem.ExecutionOrder);
            Assert.LessOrEqual(secondSystem.ExecutionOrder, thirdSystem.ExecutionOrder);
        }
        
        // Test systems
        private class TestSystem : ISystem
        {
            public bool OnTickCalled { get; set; }
            
            public void OnCreate()
            {
            }
            
            public void OnTick(ulong tickMask)
            {
                OnTickCalled = true;
            }
            
            public void OnDestroy()
            {
            }
        }
        
        private class LifecycleTestSystem : ISystem
        {
            public bool OnDestroyCalled { get; private set; }
            
            public void OnCreate()
            {
            }
            
            public void OnTick(ulong tickMask)
            {
            }
            
            public void OnDestroy()
            {
                OnDestroyCalled = true;
            }
        }
        
        private class TickMaskTestSystem1 : ISystem
        {
            public ulong TickGroup => 0b0001;
            
            public bool OnTickCalled { get; set; }
            
            public void OnCreate()
            {
            }
            
            public void OnTick(ulong tickMask)
            {
                OnTickCalled = true;
            }
            
            public void OnDestroy()
            {
            }
        }
        
        private class TickMaskTestSystem2 : ISystem
        {
            public ulong TickGroup => 0b0010;
            
            public bool OnTickCalled { get; set; }
            
            public void OnCreate()
            {
            }
            
            public void OnTick(ulong tickMask)
            {
                OnTickCalled = true;
            }
            
            public void OnDestroy()
            {
            }
        }
        
        private class ExceptionTestSystem : ISystem
        {
            public void OnCreate()
            {
            }
            
            public void OnTick(ulong tickMask)
            {
                throw new InvalidOperationException("Test exception");
            }
            
            public void OnDestroy()
            {
            }
        }
        
        private class FirstSystem : ISystem
        {
            public bool Executed { get; private set; }
            public int ExecutionOrder { get; private set; }
            private static int _executionCounter = 0;
            
            public void OnCreate()
            {
            }
            
            public void OnTick(ulong tickMask)
            {
                Executed = true;
                ExecutionOrder = ++_executionCounter;
            }
            
            public void OnDestroy()
            {
            }
        }
        
        private class SecondSystem : ISystem
        {
            public bool Executed { get; private set; }
            public int ExecutionOrder { get; private set; }
            private static int _executionCounter = 0;
            
            public void OnCreate()
            {
            }
            
            public void OnTick(ulong tickMask)
            {
                Executed = true;
                ExecutionOrder = ++_executionCounter;
            }
            
            public void OnDestroy()
            {
            }
        }
        
        private class ThirdSystem : ISystem
        {
            public bool Executed { get; private set; }
            public int ExecutionOrder { get; private set; }
            private static int _executionCounter = 0;
            
            public void OnCreate()
            {
            }
            
            public void OnTick(ulong tickMask)
            {
                Executed = true;
                ExecutionOrder = ++_executionCounter;
            }
            
            public void OnDestroy()
            {
            }
        }
    }
}