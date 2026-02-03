using System;
using System.Collections.Generic;
using System.Diagnostics;
using CoreECS;
using CoreECS.Defines;
using NUnit.Framework;
using TinyECS;

namespace TinyECS.Test
{
    [TestFixture]
    public class StressTestUnit
    {
        [Test]
        public void StressTest_LargeNumberOfEntities()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            const int entityCount = 10000; // Increased for stress test
            var entities = new List<Entity>();
            
            // Act
            
            var stopwatch = Stopwatch.StartNew();
            
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
            Console.WriteLine($"Created {entityCount} entities in {stopwatch.ElapsedMilliseconds} ms");
            Assert.Less(stopwatch.ElapsedMilliseconds, 5000); // Should create 10k entities in less than 5 seconds
            
            // Cleanup
            stopwatch.Restart();
            foreach (var entity in entities)
            {
                world.DestroyEntity(entity);
            }
            stopwatch.Stop();
            Console.WriteLine($"Destroyed {entityCount} entities in {stopwatch.ElapsedMilliseconds} ms");
            
            world.Shutdown();
        }
        
        [Test]
        public void StressTest_HighFrequencyTicks()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            const int tickCount = 1000; // More ticks for stress test
            const int entitiesPerTick = 50;
            
            world.RegisterSystem<MovementSystem>();
            var entities = new List<Entity>();
            
            var stopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < tickCount; i++)
            {
                // Create entities periodically
                if (i % 100 == 0)
                {
                    for (int j = 0; j < entitiesPerTick; j++)
                    {
                        var entity = world.CreateEntity();
                        entity.CreateComponent<PositionComponent>();
                        entity.CreateComponent<VelocityComponent>();
                        entities.Add(entity);
                    }
                }
                
                // Tick the world
                world.BeginTick();
                world.Tick();
                world.EndTick();
            }
            
            stopwatch.Stop();
            
            // Assert
            Assert.AreEqual(tickCount, world.TickCount);
            Assert.IsTrue(world.FindSystem<MovementSystem>().ProcessedEntities);
            Console.WriteLine($"Completed {tickCount} ticks with {entities.Count} total entities in {stopwatch.ElapsedMilliseconds} ms");
            
            // Cleanup
            foreach (var entity in entities)
            {
                world.DestroyEntity(entity);
            }
            world.Shutdown();
        }
        
        [Test]
        public void StressTest_ComponentOperationsUnderLoad()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            const int entityCount = 5000;
            var entities = new List<Entity>();
            
            var stopwatch = Stopwatch.StartNew();
            
            // Create entities with components
            for (int i = 0; i < entityCount; i++)
            {
                var entity = world.CreateEntity();
                entity.CreateComponent<PositionComponent>();
                entities.Add(entity);
            }
            
            // Act - Perform many component operations
            for (int i = 0; i < 1000; i++)
            {
                var randomEntity = entities[i % entities.Count];
                
                // Add and remove components
                if (randomEntity.HasComponent<VelocityComponent>())
                {
                    var velRef = randomEntity.GetComponent<VelocityComponent>();
                    randomEntity.DestroyComponent(velRef);
                }
                else
                {
                    randomEntity.CreateComponent<VelocityComponent>();
                }
                
                // Update component data
                var posRef = randomEntity.GetComponent<PositionComponent>();
                posRef.RW.X = i % 100;
                posRef.RW.Y = i % 100;
            }
            
            stopwatch.Stop();
            
            // Assert
            Console.WriteLine($"Performed 1000 component operations in {stopwatch.ElapsedMilliseconds} ms");
            Assert.Less(stopwatch.ElapsedMilliseconds, 2000); // Should complete in under 2 seconds
            
            // Cleanup
            foreach (var entity in entities)
            {
                world.DestroyEntity(entity);
            }
            world.Shutdown();
        }
        
        [Test]
        public void StressTest_ManyCollectors()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            // Create many different collectors with various matchers
            var collectors = new List<IEntityCollector>();
            
            var matcher1 = EntityMatcher.With.OfAll<PositionComponent>();
            var matcher2 = EntityMatcher.With.OfAll<VelocityComponent>();
            var matcher3 = EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>();
            var matcher4 = EntityMatcher.With.OfAll<HealthComponent>();
            var matcher5 = EntityMatcher.With.OfAll<PositionComponent>().OfAny<VelocityComponent>().OfAny<HealthComponent>();
            
            collectors.Add(world.CreateCollector(matcher1));
            collectors.Add(world.CreateCollector(matcher2));
            collectors.Add(world.CreateCollector(matcher3));
            collectors.Add(world.CreateCollector(matcher4));
            collectors.Add(world.CreateCollector(matcher5));
            
            // Create entities
            const int entityCount = 1000;
            var entities = new List<Entity>();
            
            var stopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < entityCount; i++)
            {
                var entity = world.CreateEntity();
                
                // Randomly assign components to create varied matching scenarios
                entity.CreateComponent<PositionComponent>();
                
                if (i % 2 == 0)
                    entity.CreateComponent<VelocityComponent>();
                
                if (i % 3 == 0)
                    entity.CreateComponent<HealthComponent>();
                
                entities.Add(entity);
            }
            
            
            // Run multiple ticks while using collectors
            for (int i = 0; i < 100; i++)
            {
                world.BeginTick();
                world.Tick();
                
                // Update all collectors
                foreach (var collector in collectors)
                {
                    collector.Change();
                }
                
                world.EndTick();
            }
            
            stopwatch.Stop();
            
            // Assert
            Assert.AreEqual(100, world.TickCount);
            Console.WriteLine($"Ran {entityCount} entities with 5 collectors through 100 ticks in {stopwatch.ElapsedMilliseconds} ms");
            for (var i = 0; i < collectors.Count; i++)
            {
                Console.WriteLine($"Collector {i} matched {collectors[i].Collected.Count} entities");
            }
            
            // Cleanup
            foreach (var collector in collectors)
            {
                collector.Dispose();
            }
            
            foreach (var entity in entities)
            {
                world.DestroyEntity(entity);
            }
            
            world.Shutdown();
        }
        
        [Test]
        public void StressTest_EntityCreationAndDestruction()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            const int iterations = 1000;
            const int batchSize = 50;
            
            var stopwatch = Stopwatch.StartNew();
            
            // Act - Repeatedly create and destroy batches of entities
            for (int i = 0; i < iterations; i++)
            {
                var batch = new List<Entity>();
                
                // Create a batch of entities
                for (int j = 0; j < batchSize; j++)
                {
                    var entity = world.CreateEntity();
                    entity.CreateComponent<PositionComponent>();
                    entity.CreateComponent<VelocityComponent>();
                    batch.Add(entity);
                }
                
                // Tick to process the new entities
                world.BeginTick();
                world.Tick();
                world.EndTick();
                
                // Destroy the batch
                foreach (var entity in batch)
                {
                    world.DestroyEntity(entity);
                }
            }
            
            stopwatch.Stop();
            
            // Assert
            Console.WriteLine($"Created and destroyed {iterations * batchSize} entities in {stopwatch.ElapsedMilliseconds} ms");
            Assert.Less(stopwatch.ElapsedMilliseconds, 10000); // Should complete in under 10 seconds
            
            world.Shutdown();
        }
        
        [Test]
        public void StressTest_ExtremeEntityComponentCombinations()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            const int entityCount = 5000;
            var entities = new List<Entity>();
            
            var stopwatch = Stopwatch.StartNew();
            
            // Create entities with complex component combinations
            for (int i = 0; i < entityCount; i++)
            {
                var entity = world.CreateEntity();
                
                // Randomly assign multiple components to create complex scenarios
                if (i % 2 == 0)
                    entity.CreateComponent<PositionComponent>();
                
                if (i % 3 == 0)
                    entity.CreateComponent<VelocityComponent>();
                
                if (i % 5 == 0)
                    entity.CreateComponent<HealthComponent>();
                
                // Additional test components to increase complexity
                if (i % 7 == 0)
                    entity.CreateComponent<DamageComponent>();
                
                if (i % 11 == 0)
                    entity.CreateComponent<DefenseComponent>();
                    
                if (i % 13 == 0)
                    entity.CreateComponent<SpeedComponent>();
                
                entities.Add(entity);
            }
            
            stopwatch.Stop();
            Console.WriteLine($"Created {entityCount} entities with complex component combinations in {stopwatch.ElapsedMilliseconds} ms");
            
            // Test performance of retrieving components under load
            var retrieveStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++)
            {
                var entity = entities[i % entities.Count];
                if (entity.HasComponent<PositionComponent>())
                {
                    var posRef = entity.GetComponent<PositionComponent>();
                    posRef.RW.X = i % 1000;
                }
            }
            retrieveStopwatch.Stop();
            Console.WriteLine($"Performed 10000 component retrievals in {retrieveStopwatch.ElapsedMilliseconds} ms");
            
            // Assert
            Assert.Less(retrieveStopwatch.ElapsedMilliseconds, 5000); // Should complete in under 5 seconds
            
            // Cleanup
            foreach (var entity in entities)
            {
                world.DestroyEntity(entity);
            }
            world.Shutdown();
        }
        
        [Test]
        public void StressTest_MassiveCollectorSystemInteraction()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            // Register multiple systems that interact with collectors
            world.RegisterSystem<ComplexMovementSystem>();
            world.RegisterSystem<CollisionDetectionSystem>();
            world.RegisterSystem<HealthRegenSystem>();
            
            // Create many collectors with complex matchers
            var collectors = new List<IEntityCollector>();
            
            collectors.Add(world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>()));
            collectors.Add(world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>()));
            collectors.Add(world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>().OfAll<HealthComponent>()));
            collectors.Add(world.CreateCollector(EntityMatcher.With.OfAll<VelocityComponent>().OfAll<HealthComponent>()));
            collectors.Add(world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>().OfAll<HealthComponent>()));
            collectors.Add(world.CreateCollector(EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>()));
            collectors.Add(world.CreateCollector(EntityMatcher.With.OfAll<HealthComponent>().OfNone<DamageComponent>()));
            collectors.Add(world.CreateCollector(EntityMatcher.With.OfAll<SpeedComponent>().OfAll<VelocityComponent>()));
            collectors.Add(world.CreateCollector(EntityMatcher.With.OfAll<DefenseComponent>().OfAll<HealthComponent>()));
            collectors.Add(world.CreateCollector(EntityMatcher.With.OfAll<DamageComponent>().OfAll<HealthComponent>()));
            
            // Create a large number of entities with varied components
            const int entityCount = 10000;
            const int tickCount = 200;
            var entities = new List<Entity>();
            var tickTimeDuration = new float[tickCount];
            
            var stopwatch = Stopwatch.StartNew();
            
            var initStopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < entityCount; i++)
            {
                var entity = world.CreateEntity();
                
                // Complex component assignment patterns
                entity.CreateComponent<PositionComponent>();
                
                if (i % 2 == 0)
                    entity.CreateComponent<VelocityComponent>();
                
                if (i % 3 == 0)
                    entity.CreateComponent<HealthComponent>();
                
                if (i % 4 == 0)
                    entity.CreateComponent<DamageComponent>();
                
                if (i % 5 == 0)
                    entity.CreateComponent<DefenseComponent>();
                    
                if (i % 6 == 0)
                    entity.CreateComponent<SpeedComponent>();
                
                entities.Add(entity);
            }
            
            initStopwatch.Stop();
            
            // Run intensive tick cycle with collector updates
            for (int i = 0; i < tickCount; i++)
            {
                var tickStopwatch = Stopwatch.StartNew();
                
                world.BeginTick();
                world.Tick();
                
                // Update all collectors
                foreach (var collector in collectors)
                {
                    collector.Change();
                }
                
                // Simulate system interactions with collectors
                var movementSystem = world.FindSystem<ComplexMovementSystem>();
                movementSystem?.SimulateProcessing();
                
                world.EndTick();
                
                tickStopwatch.Stop();
                tickTimeDuration[i] = tickStopwatch.ElapsedMilliseconds;
            }
            
            stopwatch.Stop();
            
            // Assert
            Assert.AreEqual(200, world.TickCount);
            Console.WriteLine($"Ran {entityCount} entities with 10 collectors through 200 ticks in {stopwatch.ElapsedMilliseconds} ms");
            
            // Print tick time duration statistics
            Console.WriteLine($"Initialized {entityCount} entities in {initStopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"Tick time duration statistics:");
            Console.WriteLine($"  Min: {tickTimeDuration.Min()} ms");
            Console.WriteLine($"  Max: {tickTimeDuration.Max()} ms");
            Console.WriteLine($"  Avg: {tickTimeDuration.Average()} ms");
            Console.WriteLine($"  1st: {tickTimeDuration[0]} ms");
            Console.WriteLine($"  2nd: {tickTimeDuration[1]} ms");
            Console.WriteLine($"  3rd: {tickTimeDuration[2]} ms");
            Console.WriteLine($"  Last: {tickTimeDuration[tickTimeDuration.Length - 1]} ms");
            
            
            Array.Sort(tickTimeDuration);
            Console.WriteLine($"  P50: {tickTimeDuration[tickTimeDuration.Length / 2]} ms");
            Console.WriteLine($"  P90: {tickTimeDuration[tickTimeDuration.Length * 9 / 10]} ms");
            Console.WriteLine($"  P01: {tickTimeDuration[tickTimeDuration.Length * 1 / 100]} ms");
            Console.WriteLine($"");
            
            // Print collector statistics
            for (var i = 0; i < collectors.Count; i++)
            {
                Console.WriteLine($"Collector {i} matched {collectors[i].Collected.Count} entities");
            }
            
            // Cleanup
            foreach (var collector in collectors)
            {
                collector.Dispose();
            }
            
            foreach (var entity in entities)
            {
                world.DestroyEntity(entity);
            }
            
            world.Shutdown();
        }
        
        [Test]
        public void StressTest_ComponentPoolFragmentation()
        {
            // Arrange
            var world = new World();
            world.Startup();
            
            const int cycles = 500;
            const int batchCount = 100;
            var entities = new List<Entity>();
            
            var stopwatch = Stopwatch.StartNew();
            
            // Create and destroy entities in cycles to test memory fragmentation
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                // Create a batch of entities
                for (int i = 0; i < batchCount; i++)
                {
                    var entity = world.CreateEntity();
                    entity.CreateComponent<PositionComponent>();
                    entity.CreateComponent<VelocityComponent>();
                    entity.CreateComponent<HealthComponent>();
                    
                    // Randomly add more components
                    if (cycle % 3 == 0)
                        entity.CreateComponent<DamageComponent>();
                        
                    if (cycle % 4 == 0)
                        entity.CreateComponent<DefenseComponent>();
                        
                    entities.Add(entity);
                }
                
                // Perform operations during each cycle
                foreach (var entity in entities)
                {
                    if (entity.HasComponent<HealthComponent>())
                    {
                        var healthRef = entity.GetComponent<HealthComponent>();
                        healthRef.RW.Value -= 1;
                    }
                }
                
                // Periodically destroy half the entities to create fragmentation
                if (cycle % 5 == 0 && entities.Count > 0)
                {
                    int half = entities.Count / 2;
                    for (int i = 0; i < half; i++)
                    {
                        world.DestroyEntity(entities[i]);
                    }
                    
                    entities.RemoveRange(0, half);
                }
                
                // Tick the world
                world.BeginTick();
                world.Tick();
                world.EndTick();
            }
            
            stopwatch.Stop();
            
            // Final cleanup
            foreach (var entity in entities)
            {
                world.DestroyEntity(entity);
            }
            
            // Assert
            Console.WriteLine($"Completed {cycles} cycles of entity creation/destruction with fragmentation testing in {stopwatch.ElapsedMilliseconds} ms");
            Assert.Less(stopwatch.ElapsedMilliseconds, 15000); // Should complete in under 15 seconds
            
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
        
        private struct DamageComponent : IComponent<DamageComponent>
        {
            public float Value;
            public float Range;
        }
        
        private struct DefenseComponent : IComponent<DefenseComponent>
        {
            public float Value;
            public float Shield;
        }
        
        private struct SpeedComponent : IComponent<SpeedComponent>
        {
            public float Multiplier;
            public float MaxSpeed;
        }
        
        // Test systems
        private class MovementSystem : ISystem
        {
            public bool ProcessedEntities { get; private set; }
         
            private World m_world;
            
            private IEntityCollector m_movementCollector;
            
            public void OnCreate()
            {
                m_movementCollector = m_world?.CreateCollector(
                    EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>()
                );
            }
            
            public void OnTick(ulong tickMask)
            {
                if (m_movementCollector != null)
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
            }
            
            public void OnDestroy()
            {
                m_movementCollector?.Dispose();
            }

            public MovementSystem(World world)
            {
                m_world = world;
            }
        }
        
        private class ComplexMovementSystem : ISystem
        {
            private World m_world;
            private IEntityCollector m_collector;
            private int _processCount = 0;
            
            public ComplexMovementSystem(World world)
            {
                m_world = world;
            }
            
            public void OnCreate()
            {
                m_collector = m_world?.CreateCollector(
                    EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>().OfAll<SpeedComponent>()
                );
            }
            
            public void OnTick(ulong tickMask)
            {
                if (m_collector != null)
                {
                    m_collector.Change();
                    for (int i = 0; i < m_collector.Collected.Count; i++)
                    {
                        var entity = m_world.GetEntity(m_collector.Collected[i]);
                        var position = entity.GetComponent<PositionComponent>();
                        var velocity = entity.GetComponent<VelocityComponent>();
                        var speed = entity.GetComponent<SpeedComponent>();
                        
                        position.RW.X += velocity.RO.X * speed.RO.Multiplier;
                        position.RW.Y += velocity.RO.Y * speed.RO.Multiplier;
                        
                        _processCount++;
                    }
                }
            }
            
            public void OnDestroy()
            {
                m_collector?.Dispose();
            }
            
            public void SimulateProcessing()
            {
                // Additional processing simulation
                for (int i = 0; i < 100; i++)
                {
                    var dummy = i * 2 + 1;
                }
            }
        }
        
        private class CollisionDetectionSystem : ISystem
        {
            private World m_world;
            private IEntityCollector m_collector;
            
            public CollisionDetectionSystem(World world)
            {
                m_world = world;
            }
            
            public void OnCreate()
            {
                m_collector = m_world?.CreateCollector(
                    EntityMatcher.With.OfAll<PositionComponent>().OfAll<DamageComponent>()
                );
            }
            
            public void OnTick(ulong tickMask)
            {
                if (m_collector != null)
                {
                    m_collector.Change();
                    for (int i = 0; i < m_collector.Collected.Count; i++)
                    {
                        // Simulate collision detection logic
                        var entity = m_world.GetEntity(m_collector.Collected[i]);
                        var damage = entity.GetComponent<DamageComponent>();
                        damage.RW.Value += 0.1f;
                    }
                }
            }
            
            public void OnDestroy()
            {
                m_collector?.Dispose();
            }
        }
        
        private class HealthRegenSystem : ISystem
        {
            private World m_world;
            private IEntityCollector m_collector;
            
            public HealthRegenSystem(World world)
            {
                m_world = world;
            }
            
            public void OnCreate()
            {
                m_collector = m_world?.CreateCollector(
                    EntityMatcher.With.OfAll<HealthComponent>()
                );
            }
            
            public void OnTick(ulong tickMask)
            {
                if (m_collector != null)
                {
                    m_collector.Change();
                    for (int i = 0; i < m_collector.Collected.Count; i++)
                    {
                        var entity = m_world.GetEntity(m_collector.Collected[i]);
                        var health = entity.GetComponent<HealthComponent>();
                        health.RW.Value += 0.05f;
                        
                        if (health.RW.Value > 100.0f)
                            health.RW.Value = 100.0f;
                    }
                }
            }
            
            public void OnDestroy()
            {
                m_collector?.Dispose();
            }
        }
    }
}