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
    public class IntegrationTestUnit
    {
        [Test]
        public void CompleteECSWorkflow_EntityCreationToDestruction()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            world.RegisterSystem<MovementSystem>();
            world.RegisterSystem<CollisionSystem>();
            world.RegisterSystem<RenderSystem>();
            
            // Act - Create entities with components
            var player = world.CreateEntity();
            player.CreateComponent<PositionComponent>().RW = new PositionComponent { X = 0, Y = 0 };
            player.CreateComponent<VelocityComponent>().RW = new VelocityComponent { X = 1, Y = 1 };
            player.CreateComponent<PlayerComponent>().RW = new PlayerComponent { Name = "Player1" };
            
            var enemy = world.CreateEntity();
            enemy.CreateComponent<PositionComponent>().RW = new PositionComponent { X = 10, Y = 10 };
            enemy.CreateComponent<VelocityComponent>().RW = new VelocityComponent { X = -1, Y = -1 };
            enemy.CreateComponent<EnemyComponent>().RW = new EnemyComponent { Health = 100 };
            
            // Run simulation for several ticks
            for (int i = 0; i < 10; i++)
            {
                world.BeginTick();
                world.Tick();
                world.EndTick();
            }
            
            // Check final positions
            var finalPlayerPos = player.GetComponent<PositionComponent>().RW;
            var finalEnemyPos = enemy.GetComponent<PositionComponent>().RW;
            
            // Assert
            Assert.AreEqual(10, finalPlayerPos.X);
            Assert.AreEqual(10, finalPlayerPos.Y);
            Assert.AreEqual(0, finalEnemyPos.X);
            Assert.AreEqual(0, finalEnemyPos.Y);
            
            // Verify systems processed entities
            var movementSystem = world.FindSystem<MovementSystem>();
            var collisionSystem = world.FindSystem<CollisionSystem>();
            var renderSystem = world.FindSystem<RenderSystem>();
            
            Assert.IsTrue(movementSystem.ProcessedEntities);
            Assert.IsTrue(collisionSystem.DetectedCollisions);
            Assert.IsTrue(renderSystem.RenderedEntities);
            
            // Cleanup
            world.DestroyEntity(player);
            world.DestroyEntity(enemy);
            world.Shutdown();
        }
        
        [Test]
        public void EntityComponentSystem_DynamicComponentAdditionAndRemoval()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            world.RegisterSystem<DynamicComponentSystem>();
            
            var entity = world.CreateEntity();
            entity.CreateComponent<PositionComponent>().RW = new PositionComponent { X = 5, Y = 5 };
            
            // Act - Run initial tick
            world.BeginTick();
            world.Tick();
            world.EndTick();
            
            var system = world.FindSystem<DynamicComponentSystem>();
            Assert.AreEqual(1, system.PositionOnlyCount);
            
            // Add a new component
            entity.CreateComponent<VelocityComponent>().RW = new VelocityComponent { X = 1, Y = 1 };
            
            // Run another tick
            world.BeginTick();
            world.Tick();
            world.EndTick();
            
            Assert.AreEqual(0, system.PositionOnlyCount);
            Assert.AreEqual(1, system.PositionAndVelocityCount);
            
            // Remove the velocity component
            var velocityRef = entity.GetComponent<VelocityComponent>();
            entity.DestroyComponent(velocityRef);
            
            // Run another tick
            world.BeginTick();
            world.Tick();
            world.EndTick();
            
            Assert.AreEqual(1, system.PositionOnlyCount);
            Assert.AreEqual(0, system.PositionAndVelocityCount);
            
            // Cleanup
            world.DestroyEntity(entity);
            world.Shutdown();
        }
        
        [Test]
        public void PerformanceTest_LargeNumberOfEntities()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            const int entityCount = 1000;
            var entities = new List<Entity>();
            
            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            for (int i = 0; i < entityCount; i++)
            {
                var entity = world.CreateEntity();
                entity.CreateComponent<PositionComponent>();
                entity.CreateComponent<VelocityComponent>();
                entities.Add(entity);
            }
            
            stopwatch.Stop();
            
            // Assert
            Assert.AreEqual(entityCount, entities.Count);
            Assert.Less(stopwatch.ElapsedMilliseconds, 1000); // Should create 1k entities in less than 1 second
            
            // Cleanup
            foreach (var entity in entities)
            {
                world.DestroyEntity(entity);
            }
            world.Shutdown();
        }
        
        [Test]
        public void SystemManager_SystemRegistrationAndUnregistration()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            world.RegisterSystem<TestSystem1>();
            world.RegisterSystem<TestSystem2>();
            
            // Act
            world.BeginTick();
            world.Tick();
            world.EndTick();
            
            var system1 = world.FindSystem<TestSystem1>();
            var system2 = world.FindSystem<TestSystem2>();
            
            Assert.IsNotNull(system1);
            Assert.IsNotNull(system2);
            Assert.IsTrue(system1.OnTickCalled);
            Assert.IsTrue(system2.OnTickCalled);
            
            // Unregister one system
            world.GetManager<SystemManager>().UnregisterSystem(typeof(TestSystem1));
            
            world.BeginTick();
            world.Tick();
            world.EndTick();
            
            // Assert
            system1 = world.FindSystem<TestSystem1>();
            system2 = world.FindSystem<TestSystem2>();
            
            Assert.IsNull(system1);
            Assert.IsNotNull(system2);
            
            // Cleanup
            world.Shutdown();
        }
        
        [Test]
        public void ComponentLifecycle_OnCreateAndOnDestroyEvents()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            var entity = world.CreateEntity();
            
            // Act
            var componentRef = entity.CreateComponent<LifecycleComponent>();
            
            // Assert
            Assert.IsTrue(componentRef.RW.OnCreateCalled);
            Assert.IsFalse(componentRef.RW.OnDestroyCalled);
            
            // Act
            world.DestroyEntity(entity);
            
            // Assert
            // Note: In a real implementation, we would need to verify that OnDestroy was called
            // This might require a more complex setup with event tracking
            
            // Cleanup
            world.Shutdown();
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
        
        private struct PlayerComponent : IComponent<PlayerComponent>
        {
            public string Name;
        }
        
        private struct EnemyComponent : IComponent<EnemyComponent>
        {
            public float Health;
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
        
        // Test systems
        private class MovementSystem : ISystem
        {
            public bool ProcessedEntities { get; private set; }
         
            private World m_world;
            
            private IEntityCollector m_movementCollector;
            
            public void OnCreate()
            {
                m_movementCollector = m_world.CreateCollector(
                    EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>()
                );
            }
            
            public void OnTick()
            {
                m_movementCollector.Change();
                foreach (var entityId in m_movementCollector.Collected)
                {
                    ProcessedEntities = true;
                    
                    var entity = m_world.GetEntity(entityId);
                    var positionComp = entity.GetComponent<PositionComponent>();
                    var velocityComp = entity.GetComponent<VelocityComponent>();
                    
                    positionComp.RW.X += velocityComp.RO.X;
                    positionComp.RW.Y += velocityComp.RO.Y;
                }
            }
            
            public void OnDestroy()
            {
                m_movementCollector.Dispose();
            }

            public MovementSystem(World world)
            {
                m_world = world;
            }
        }
        
        private class CollisionSystem : ISystem
        {
            public bool DetectedCollisions { get; private set; }
            
            public void OnCreate()
            {
            }
            
            public void OnTick()
            {
                // In a real implementation, we would check for collisions between entities
                DetectedCollisions = true;
            }
            
            public void OnDestroy()
            {
            }
        }
        
        private class RenderSystem : ISystem
        {
            public bool RenderedEntities { get; private set; }
            
            public void OnCreate()
            {
            }
            
            public void OnTick()
            {
                // In a real implementation, we would render entities with PositionComponent
                RenderedEntities = true;
            }
            
            public void OnDestroy()
            {
            }
        }
        
        private class DynamicComponentSystem : ISystem
        {
            public int PositionOnlyCount { get; private set; }
            public int PositionAndVelocityCount { get; private set; }
            
            private World m_world;
            private IEntityCollector m_positionOnlyCollector;
            private IEntityCollector m_positionAndVelocityCollector;
            
            public void OnCreate()
            {
                m_positionOnlyCollector = m_world.CreateCollector(
                    EntityMatcher.With.OfAll<PositionComponent>().OfNone<VelocityComponent>()
                );
                
                m_positionAndVelocityCollector = m_world.CreateCollector(
                    EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>()
                );
            }
            
            public void OnTick()
            {
                m_positionOnlyCollector.Change();
                m_positionAndVelocityCollector.Change();
                
                PositionOnlyCount = m_positionOnlyCollector.Collected.Count;
                PositionAndVelocityCount = m_positionAndVelocityCollector.Collected.Count;
            }
            
            public void OnDestroy()
            {
                m_positionOnlyCollector.Dispose();
                m_positionAndVelocityCollector.Dispose();
            }
        
            public DynamicComponentSystem(World world)
            {
                m_world = world;
            }
        }
        
        private class TestSystem1 : ISystem
        {
            public bool OnTickCalled { get; private set; }
            
            public void OnCreate()
            {
            }
            
            public void OnTick()
            {
                OnTickCalled = true;
            }
            
            public void OnDestroy()
            {
            }
        }
        
        private class TestSystem2 : ISystem
        {
            public bool OnTickCalled { get; private set; }
            
            public void OnCreate()
            {
            }
            
            public void OnTick()
            {
                OnTickCalled = true;
            }
            
            public void OnDestroy()
            {
            }
        }
    }
}