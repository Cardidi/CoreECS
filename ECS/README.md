# CoreECS - State First ECS Toolkit

CoreECS is a lightweight, easy-to-use Entity-Component-System (ECS) implementation designed for C#-based game applications. By combining ComponentStore and EntityGraph, it strikes an effective balance between flexibility and performance.

Unlike a full application framework, CoreECS is built for seamless integration. It can coexist alongside other ECS solutions, such as UnityECS, allowing you to incorporate it into existing projects with minimal friction.

The toolkit's design was refined through a turn-based card game which makes CoreECS inherently focused on efficient and robust state management for state-driven scenarios.

## Why you should to use toolkit instead of framework?

Throughout my career, I have worked on card-based games as well as RPGs; on commercial multiplayer titles as well as low-budget indie projects. In these experiences, I have observed a fascinating phenomenon: we often see common design approaches appearing in games of different scales and genres—such as the design philosophy embodied by CoreECS. If we try to force complex problems into a single framework, we may lose the flexibility to handle issues creatively—your thinking often ends up revolving around engineering concerns, like whether your framework supports a certain feature, how to implement it, and how to balance development time with efficiency. Even game engines inherently carry a design philosophy that you must adapt to, and this constraint can become a major time-consuming challenge in software engineering.

That is why I believe a toolkit—a loosely coupled organizational approach—can more easily help developers bypass cumbersome engineering structures, select the right tools as needed, reduce development costs, and build software designs suited to the nature of their projects. This was my original motivation for developing CoreECS, and it also addressed a specific technical challenge I faced. My project initially planned to use Unity ECS for world updates, but Unity ECS proved quite clumsy in handling state changes—it sacrifices almost all flexibility for the sake of performance. To achieve my development goals, I had to write additional features to compensate. Moreover, due to the strict constraints Unity ECS imposes on C#, I struggled to work smoothly in my preferred development style. I needed a solution that balanced performance and flexibility.

Therefore, I designed CoreECS with a "state-first" philosophy and embedded it into my game to work alongside Unity ECS, achieving exactly that balance. I hope this lightweight toolkit can also help you build a development environment that fits your project—whether as a simple ECS manager or as a guardian for frontend-backend consistency, if it fits your needs, then it's the right choice!

## Key Concepts

Those are really common concepts in ECS, and you can find them in most ECS implementations.

- **Entity**: A unique identifier that groups components together
- **Component**: A data structure that holds properties/data (no logic)
- **System**: Contains the logic that operates on entities with specific component combinations
- **World**: Container that manages entities, components, and systems
- **Matcher**: Defines criteria for selecting entities based on their components
- **Collector**: Tracks entities that match specific criteria and efficiently updates when entities change
- **Injector**: Resolves dependencies and injects them into systems, components, and collectors
- **Tick**: A single iteration of the ECS framework, where systems are processed in a defined order.
- **Mask**: A bitwise flag that is used to filter entities.

## Quick Start Guide

### 1. Creating a World
A `World` represents the container for all entities, components, and systems. It manages the lifecycle of the ECS framework.

```csharp
using CoreECS;

var world = new World();
world.Startup(); // Initialize the world
```

Before calling `Startup()`, you should **NOT** do any following operations:

- Create entities
- Add components to entities
- Register systems

But you can do those operations before World call `Startup()`:

- Configure Injector by `World.Injector`
- Inherit `World` class to add custom logic like additional ECS managers or do something while world is build, start, tick and shutdown.

Be aware that the world is not thread-safe. You should only access the world from the main thread.

When this world is no longer used, ensure that you call `World.Shutdown()` to terminate this world and release all resources.

### 2. Defining Components
Components are simple data structures that implement the `IComponent<T>` interface. They hold data but don't contain logic.

```csharp
public struct PositionComponent : IComponent<PositionComponent>
{
    public float X;
    public float Y;
}

public struct VelocityComponent : IComponent<VelocityComponent>
{
    public float X;
    public float Y;
}

public struct HealthComponent : IComponent<HealthComponent>
{
    public float Value;
}
```

Components can optionally implement lifecycle methods:

```csharp
public struct LifecycleComponent : IComponent<LifecycleComponent>
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
```

### 3. Creating Entities

Entities serve as unique identifiers that bundle components together. While the underlying representation of an entity is a `ulong` value, it is recommended to use the `Entity` struct for better functionality and type safety. Use `World.GetEntity()` to convert a raw `ulong` identifier into a fully-featured `Entity` struct.

```csharp
// Create an entity
var entity = world.CreateEntity();

// Get a fully functional entity via World.GetEntity(ulong)
var anotherEntity = world.GetEntity(entityId);
```

You can also add a mask to an entity to create types of entity.

```csharp
enum EntityType {
    Actor = 1 << 1,
    Terrian = 1 << 2
}

// Create an entity with mask
var entityWithMask = world.CreateEntity((ulong) EntityType.Actor);
```

### 4. Adding Components to Entities
Components can be added to entities to give them properties and data.

```csharp
// Add components to an entity
var velocityRef = entity.CreateComponent<VelocityComponent>();
velocityRef.RW.X = 1;
velocityRef.RW.Y = 1;

// Alternative way to set component data
entity.CreateComponent<HealthComponent>().RW.Value = 100;

// Create component with initial value (recommended for setting initial data)
var positionRef = entity.CreateComponent(new PositionComponent { X = 10, Y = 20 });

// Not recommended: Direct assignment ignores component's `OnCreate` callback
var positionRef2 = entity.CreateComponent<PositionComponent>();
positionRef2.RW = new PositionComponent { X = 10, Y = 20 };
```

### 5. Accessing Components
Retrieve components from entities to read or modify their data. If you only need to read the component data, use `RO` property. If you need to modify the component data, use `RW` property.

```csharp
// Get a component from an entity
var positionRef = entity.GetComponent<PositionComponent>();
Console.WriteLine($"Position: ({positionRef.RO.X}, {positionRef.RO.Y})");

// Check if entity has a specific component
bool hasHealth = entity.HasComponent<HealthComponent>();
Console.WriteLine($"Has Health: {hasHealth}");

// Get all components of an entity
var allComponents = entity.GetComponents();
Console.WriteLine($"Total components: {allComponents.Length}");
```

#### Helper Extension Methods
Entity provides convenient extension methods for common component operations:

```csharp
// TryGetComponent - Safely get a component if it exists
if (entity.TryGetComponent<PositionComponent>(out var positionRef))
{
    Console.WriteLine($"Position: ({positionRef.RO.X}, {positionRef.RO.Y})");
}

// GetOrCreateComponent - Get existing component or create new one
// Returns true if component existed, false if it was created
bool existed = entity.GetOrCreateComponent<VelocityComponent>(out var velocityRef);
if (!existed)
{
    velocityRef.RW = new VelocityComponent { X = 1, Y = 1 };
}

// GetOrCreateComponent with initial value
entity.GetOrCreateComponent(out var healthRef, new HealthComponent { Value = 100 });
```

### 6. Removing Components
Components can be removed from entities when no longer needed.

```csharp
// Remove a component by reference
entity.DestroyComponent(positionRef);

// Remove a component by type
entity.DestroyComponent<HealthComponent>();
```

### 7. Defining Systems
Systems contain the logic that operates on entities with specific component combinations. If you want a system to access world, managers or anything can be get from DI container, just put them on constructor.

If you need to grouping system and wish those group can being ticked one by one, the best way is to add mask on system to filter which systems it should process. You can tick those system when you call `World.Tick(ulong mask)`.

Create a collector to find relevant entities is the default way to access entities in a system. As you can see, use `World.CreateCollector()` to request collector when this system created and we can get those entities via `IEntityCollector.Collected`. When we trying to iterating over entity via Collector, call `Change()` before iterating entities to update collector.

```csharp
public class MovementSystem : ISystem
{
    private World m_world;
    private IEntityCollector m_movingEntities;
    
    public ulong TickGroup => ulong.MaxValue;
    
    public MovementSystem(World world)
    {
        m_world = world;
    }
    
    public void OnCreate()
    {
        // Initialize system - create collectors to find relevant entities
        m_movingEntities = m_world.CreateCollector(
            EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>()
        );
    }
    
    public void OnTick(ulong tickMask)
    {
        // Process all entities that match the collector's criteria
        m_movingEntities.Change();
        for (var i = 0; i < m_movingEntities.Collected.Count; i++)
        {
            var entity = m_world.GetEntity(m_movingEntities.Collected[i]);
            
            var position = entity.GetComponent<PositionComponent>();
            var velocity = entity.GetComponent<VelocityComponent>();
            
            // Update position based on velocity
            position.RW.X += velocity.RW.X;
            position.RW.Y += velocity.RW.Y;
        }
    }
    
    public void OnDestroy()
    {
        // Clean up resources
        m_movingEntities?.Dispose();
    }
}
```

For further details, please refer to EntityCollector. 

### 8. Managing Systems
Register and manage systems within the world. The execution order of system is matched with the order you register system (First In First Tick). You should not changing system registration between `World.BeginTick()` and `World.EndTick()`. If you do that so, system will be added, but world modification will defer until next `World.BeginTick()`.

```csharp
// Create a world instance
var world = new World();
world.Startup();

// Register a system with the world
world.RegisterSystem<MovementSystem>();

// Find a system by type
var movementSystem = world.FindSystem<MovementSystem>();

// Tick the world in a loop
while (true) 
{
    world.BeginTick();
    world.Tick();
    world.EndTick();
}
```

### 9. Using Entity Matchers
Matchers allow you to filter entities based on their component composition.

```csharp
// Match entities that have ALL of the specified components
var positionOnlyMatcher = EntityMatcher.With.OfAll<PositionComponent>();

// Match entities that have AT LEAST ONE of the specified components
var positionOrVelocityMatcher = EntityMatcher.With
    .OfAny<PositionComponent>()
    .OfAny<VelocityComponent>();

// Match entities that have specific components BUT NOT others
var positionWithoutHealthMatcher = EntityMatcher.With
    .OfAll<PositionComponent>()
    .OfNone<HealthComponent>();

// Complex matcher - entities with Position AND Velocity but WITHOUT Health
var complexMatcher = EntityMatcher.With
    .OfAll<PositionComponent>()
    .OfAll<VelocityComponent>()
    .OfNone<HealthComponent>();

// Match entities with specific mask
var maskingMatcher = EntityMatcher.WithMask((ulong) EntityType.Actor);
```

If you create a matcher without any modification like entity mask or component rule, it will defaults to match any entity.

### 10. Entity Collector - Advanced Filtering and Change Tracking

EntityCollector is a powerful feature that tracks entities matching specific criteria and efficiently updates when entities change. It's essential for creating state-first game logic.

#### Basic Collector Usage

```csharp
// Create a collector that tracks all entities with PositionComponent
var positionCollector = world.CreateCollector(
    EntityMatcher.With.OfAll<PositionComponent>()
);

// Process collected entities
positionCollector.Change(); // Call Change() to apply any pending changes
for (int i = 0; i < positionCollector.Collected.Count; i++)
{
    var entity = world.GetEntity(positionCollector.Collected[i]);
    // Process the entity
}
```

#### Collector Flags

Collectors support different behaviors through flags. The default flag is `EntityCollectorFlag.Lazy`:

```csharp
// None - entities are immediately added/removed from Collected
var immediateCollector = world.CreateCollector(
    EntityMatcher.With.OfAll<PositionComponent>(),
    EntityCollectorFlag.None
);

// LazyAdd - newly matching entities won't appear in Collected until Change() is called
var lazyAddCollector = world.CreateCollector(
    EntityMatcher.With.OfAll<PositionComponent>(),
    EntityCollectorFlag.LazyAdd
);

// LazyRemove - entities won't be removed from Collected until Change() is called
var lazyRemoveCollector = world.CreateCollector(
    EntityMatcher.With.OfAll<PositionComponent>(),
    EntityCollectorFlag.LazyRemove
);

// Lazy (default) - combines both LazyAdd and LazyRemove behaviors
// This is the default when no flag is specified
var lazyCollector = world.CreateCollector(
    EntityMatcher.With.OfAll<PositionComponent>()
    // EntityCollectorFlag.Lazy is used by default
);
```

#### Change Tracking

Collectors track which entities have changed since the last `Change()` call:

```csharp
var collector = world.CreateCollector(
    EntityMatcher.With.OfAll<PositionComponent>()
);

// Add some entities with components
var entity1 = world.CreateEntity();
entity1.CreateComponent<PositionComponent>();

// Call Change() to process pending changes
collector.Change();

// Access change tracking lists
var matchingEntities = collector.Matching;  // New entities that started matching
var clashingEntities = collector.Clashing;  // Entities that stopped matching

foreach (var entityId in matchingEntities)
{
    Console.WriteLine($"New matching entity: {entityId}");
}

foreach (var entityId in clashingEntities)
{
    Console.WriteLine($"Stopped matching entity: {entityId}");
}
```

#### Best Practices for Using Collectors

1. **Always call `Change()` before processing collected entities** to ensure the collection is up-to-date with recent changes.

2. **Do NOT use foreach loops** when iterating over `Collected` - use indexed for loops instead to prevent issues with potential collection modifications during iteration:

```csharp
// CORRECT - Use indexed for loop
for (int i = 0; i < collector.Collected.Count; i++)
{
    var entity = world.GetEntity(collector.Collected[i]);
    // Process entity
}

// INCORRECT - Avoid foreach loops
// foreach (var entityId in collector.Collected) // This can cause issues
// {
//     var entity = world.GetEntity(entityId);
//     // Process entity
// }
```

3. **Proper cleanup** - Always dispose collectors when no longer needed:

```csharp
// In system's OnDestroy method
public void OnDestroy()
{
    m_collector?.Dispose();
}
```

4. **Choose appropriate flags** based on your use case:
   - Use `Lazy` flag when you want to process all changes at once in a predictable manner
   - Use `None` flag when you need immediate updates to the collection


### 11. Complete Example
Here's a complete example demonstrating the basic usage:

```csharp
using System;
using CoreECS;
using CoreECS.Defines;

// Define components
public struct PositionComponent : IComponent<PositionComponent>
{
    public float X;
    public float Y;
}

public struct VelocityComponent : IComponent<VelocityComponent>
{
    public float X;
    public float Y;
}

// Define system
public class MovementSystem : ISystem
{
    private World m_world;
    private IEntityCollector m_movingEntities;
    
    public MovementSystem(World world)
    {
        m_world = world;
    }
    
    public void OnCreate()
    {
        m_movingEntities = m_world.CreateCollector(
            EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>()
        );
    }
    
    public void OnTick(ulong tickMask)
    {
        m_movingEntities.Change(); // Apply pending changes
        for (var i = 0; i < m_movingEntities.Collected.Count; i++)
        {
            var entity = m_world.GetEntity(m_movingEntities.Collected[i]);
            
            var position = entity.GetComponent<PositionComponent>();
            var velocity = entity.GetComponent<VelocityComponent>();
            
            position.RW.X += velocity.RW.X * 0.016f; // Assuming ~60 FPS
            position.RW.Y += velocity.RW.Y * 0.016f;
        }
    }
    
    public void OnDestroy()
    {
        m_movingEntities?.Dispose();
    }
}

// Usage
class Program
{
    static void Main(string[] args)
    {
        // Create and start world
        var world = new World();
        world.Startup();
        
        // Register system
        world.RegisterSystem<MovementSystem>();
        
        // Create an entity with position and velocity
        var entity = world.CreateEntity();
        entity.CreateComponent<PositionComponent>().RW = new PositionComponent { X = 0, Y = 0 };
        entity.CreateComponent<VelocityComponent>().RW = new VelocityComponent { X = 10, Y = 5 };
        
        // Run simulation loop
        for (int i = 0; i < 100; i++)
        {
            world.BeginTick();
            world.Tick();
            world.EndTick();
            
            var position = entity.GetComponent<PositionComponent>();
            Console.WriteLine($"Frame {i}: Position = ({position.RW.X:F2}, {position.RW.Y:F2})");
        }
        
        // Cleanup
        world.Shutdown();
    }
}
```
