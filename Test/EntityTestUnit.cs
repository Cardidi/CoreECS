using System;
using System.Collections.Generic;
using CoreECS;
using CoreECS.Defines;
using NUnit.Framework;
using TinyECS;

namespace TinyECS.Test
{
    [TestFixture]
    public class EntityTestUnit
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
        public void Entity_IsValidAfterCreation()
        {
            // Act
            var entity = _world.CreateEntity();
            
            // Assert
            Assert.IsTrue(entity.IsValid);
            Assert.AreEqual(_world, entity.World);
            Assert.IsTrue(entity.EntityId > 0);
        }
        
        [Test]
        public void Entity_IsInvalidAfterDestruction()
        {
            // Arrange
            var entity = _world.CreateEntity();
            
            // Act
            _world.DestroyEntity(entity);
            
            // Assert
            Assert.IsFalse(entity.IsValid);
        }
        
        [Test]
        public void Entity_CanAccessMask()
        {
            // Arrange
            var entity = _world.CreateEntity(0b1010);
            
            // Act
            var mask = entity.Mask;
            
            // Assert
            Assert.AreEqual(0b1010, mask);
        }
        
        [Test]
        public void Entity_InvalidAccessThrowsException()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var entityId = entity.EntityId;
            
            // Destroy the entity
            _world.DestroyEntity(entity);
            
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => entity.CreateComponent<PositionComponent>());
            Assert.Throws<InvalidOperationException>(() => entity.GetComponent<PositionComponent>());
            Assert.Throws<InvalidOperationException>(() => entity.HasComponent<PositionComponent>());
            Assert.Throws<InvalidOperationException>(() => entity.GetComponents());
            Assert.Throws<InvalidOperationException>(() => { _ = entity.Mask; });
        }
        
        [Test]
        public void Entity_CanCreateComponent()
        {
            // Arrange
            var entity = _world.CreateEntity();
            
            // Act
            var positionRef = entity.CreateComponent<PositionComponent>();
            positionRef.RW = new PositionComponent { X = 10, Y = 20 };
            
            // Assert
            Assert.IsNotNull(positionRef);
            Assert.AreEqual(10, positionRef.RW.X);
            Assert.AreEqual(20, positionRef.RW.Y);
        }
        
        [Test]
        public void Entity_CanGetComponent()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            positionRef.RW = new PositionComponent { X = 10, Y = 20 };
            
            // Act
            var retrievedRef = entity.GetComponent<PositionComponent>();
            
            // Assert
            Assert.IsNotNull(retrievedRef);
            Assert.AreEqual(10, retrievedRef.RW.X);
            Assert.AreEqual(20, retrievedRef.RW.Y);
        }
        
        [Test]
        public void Entity_CanDestroyComponent()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            
            // Act
            entity.DestroyComponent(positionRef);
            
            // Assert
            Assert.IsFalse(entity.HasComponent<PositionComponent>());
        }
        
        [Test]
        public void Entity_CanGetAllComponents()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            entity.CreateComponent<HealthComponent>();
            
            // Act
            var allComponents = entity.GetComponents();
            
            // Assert
            Assert.AreEqual(3, allComponents.Length);
            
            var componentTypes = new List<Type>();
            foreach (var comp in allComponents)
            {
                componentTypes.Add(comp.RuntimeType);
            }
            
            Assert.Contains(typeof(PositionComponent), componentTypes);
            Assert.Contains(typeof(VelocityComponent), componentTypes);
            Assert.Contains(typeof(HealthComponent), componentTypes);
        }
        
        [Test]
        public void Entity_CanHaveMultipleComponents()
        {
            // Arrange
            var entity = _world.CreateEntity();
            
            // Act
            var positionRef = entity.CreateComponent<PositionComponent>();
            var velocityRef = entity.CreateComponent<VelocityComponent>();
            
            positionRef.RW = new PositionComponent { X = 10, Y = 20 };
            velocityRef.RW = new VelocityComponent { X = 1, Y = 2 };
            
            // Assert
            Assert.IsTrue(entity.HasComponent<PositionComponent>());
            Assert.IsTrue(entity.HasComponent<VelocityComponent>());
            
            var pos = entity.GetComponent<PositionComponent>();
            var vel = entity.GetComponent<VelocityComponent>();
            
            Assert.AreEqual(10, pos.RW.X);
            Assert.AreEqual(20, pos.RW.Y);
            Assert.AreEqual(1, vel.RW.X);
            Assert.AreEqual(2, vel.RW.Y);
        }
        
        [Test]
        public void Entity_CanCreateComponentByType()
        {
            // Arrange
            var entity = _world.CreateEntity();
            
            // Act
            var componentRef = entity.CreateComponent<PositionComponent>();
            
            // Assert
            Assert.IsNotNull(componentRef);
            Assert.AreEqual(typeof(PositionComponent), componentRef.RW.GetType());
        }
        
        [Test]
        public void Entity_CanDestroyComponentByRef()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();
            
            // Act
            entity.DestroyComponent(componentRef);
            
            // Assert
            Assert.IsFalse(entity.HasComponent<PositionComponent>());
        }
        
        [Test]
        public void Entity_DestroyComponentByType()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();

            // Assert that component exists
            Assert.IsTrue(entity.HasComponent<PositionComponent>());

            // Act
            entity.DestroyComponent<PositionComponent>();

            // Assert that component no longer exists
            Assert.IsFalse(entity.HasComponent<PositionComponent>());
        }
        
        [Test]
        public void Entity_DestroyMultipleComponentsOfType()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            
            // Assert components exist
            Assert.IsTrue(entity.HasComponent<PositionComponent>());
            Assert.IsTrue(entity.HasComponent<VelocityComponent>());
            
            // Act
            entity.DestroyComponent<PositionComponent>();
            
            // Assert only PositionComponent was removed
            Assert.IsFalse(entity.HasComponent<PositionComponent>());
            Assert.IsTrue(entity.HasComponent<VelocityComponent>());
        }
        
        [Test]
        public void Entity_DestroyComponentByUntypedRef()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var typedRef = entity.CreateComponent<PositionComponent>();
            var untypedRef = typedRef.Untyped(); // Convert to untyped reference
            
            // Assert component exists
            Assert.IsTrue(entity.HasComponent<PositionComponent>());
            
            // Act
            entity.DestroyComponent(untypedRef);
            
            // Assert component no longer exists
            Assert.IsFalse(entity.HasComponent<PositionComponent>());
        }
        
        [Test]
        public void Entity_DestroyForeignComponent_ThrowsException()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var componentRef = entity1.CreateComponent<PositionComponent>();
            
            // Act & Assert - Should throw exception because component belongs to different entity
            Assert.Throws<InvalidOperationException>(() => entity2.DestroyComponent(componentRef));
        }
        
        [Test]
        public void Entity_GetComponentAfterDestruction_ThrowsException()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            positionRef.RW = new PositionComponent { X = 10, Y = 20 };
            
            // Verify component exists and is accessible
            var retrievedRef = entity.GetComponent<PositionComponent>();
            Assert.AreEqual(10, retrievedRef.RW.X);
            
            // Act - Destroy the component
            entity.DestroyComponent(positionRef);
            
            // Assert - Getting the component should now fail
            Assert.IsFalse(entity.GetComponent<PositionComponent>().NotNull);
        }
        
        [Test]
        public void Entity_CreateAndDestroySameComponentMultipleTimes()
        {
            // Arrange
            var entity = _world.CreateEntity();
            
            // Act & Assert - First cycle
            var comp1 = entity.CreateComponent<PositionComponent>();
            comp1.RW = new PositionComponent { X = 10, Y = 20 };
            Assert.IsTrue(entity.HasComponent<PositionComponent>());
            Assert.AreEqual(10, entity.GetComponent<PositionComponent>().RW.X);
            
            entity.DestroyComponent(comp1);
            Assert.IsFalse(entity.HasComponent<PositionComponent>());
            
            // Second cycle
            var comp2 = entity.CreateComponent<PositionComponent>();
            comp2.RW = new PositionComponent { X = 30, Y = 40 };
            Assert.IsTrue(entity.HasComponent<PositionComponent>());
            Assert.AreEqual(30, entity.GetComponent<PositionComponent>().RW.X);
            
            entity.DestroyComponent(comp2);
            Assert.IsFalse(entity.HasComponent<PositionComponent>());
        }

        #region Additional Entity Tests

        [Test]
        public void Entity_GetComponents_GenericArray_ReturnsCorrectTypes()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var posRef1 = entity.CreateComponent<PositionComponent>();
            var posRef2 = entity.CreateComponent<PositionComponent>();
            
            // Act
            var positionComponents = entity.GetComponents<PositionComponent>();
            
            // Assert
            Assert.AreEqual(2, positionComponents.Length);
            Assert.IsTrue(positionComponents[0].NotNull);
            Assert.IsTrue(positionComponents[1].NotNull);
        }

        [Test]
        public void Entity_GetComponents_Collection_FillsCorrectly()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            
            var results = new List<ComponentRef<PositionComponent>>();
            
            // Act
            var count = entity.GetComponents<PositionComponent>(results);
            
            // Assert
            Assert.AreEqual(1, count);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(typeof(PositionComponent), results[0].Core.RefLocator.GetT());
        }

        [Test]
        public void Entity_DestroyComponentTwice_ThrowsException()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();
            
            // Act - Destroy the component once
            entity.DestroyComponent(componentRef);
            
            // Assert - Trying to destroy the same component again should fail
            Assert.Throws<ArgumentNullException>(() => entity.DestroyComponent(componentRef));
        }

        [Test]
        public void Entity_DestroyNonExistentComponent_ThrowsException()
        {
            // Arrange
            var entity = _world.CreateEntity();
            
            // Assert - Trying to destroy a component that doesn't exist should fail
            Assert.Throws<InvalidOperationException>(() => entity.DestroyComponent<PositionComponent>());
        }

        [Test]
        public void Entity_GetNonExistentComponent_ReturnsInvalidRef()
        {
            // Arrange
            var entity = _world.CreateEntity();
            
            // Act
            var componentRef = entity.GetComponent<PositionComponent>();
            
            // Assert
            Assert.IsFalse(componentRef.NotNull);
        }

        [Test]
        public void Entity_HasComponent_NonExistent_ReturnsFalse()
        {
            // Arrange
            var entity = _world.CreateEntity();
            
            // Act
            var hasComponent = entity.HasComponent<PositionComponent>();
            
            // Assert
            Assert.IsFalse(hasComponent);
        }

        [Test]
        public void Entity_DestroyComponentWithWrongType_ThrowsException()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            
            // Act & Assert - Try to destroy a position component as a velocity component
            Assert.Throws<InvalidOperationException>(() => entity.DestroyComponent<VelocityComponent>());
        }

        [Test]
        public void Entity_DestroyEntityThenAccess_ThrowsException()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();
            
            // Act - Destroy the entity
            _world.DestroyEntity(entity);
            
            // Assert - Accessing the entity should show it's invalid
            Assert.IsFalse(entity.IsValid);
            Assert.Throws<InvalidOperationException>(() => _ = entity.HasComponent<PositionComponent>());
        }

        [Test]
        public void ComponentRef_EqualsOperator_SameReference_ReturnsTrue()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef1 = entity.CreateComponent<PositionComponent>();
            var componentRef2 = componentRef1; // Same reference
            
            // Act
            var result = componentRef1.Equals(componentRef2);
            
            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public void ComponentRef_EqualsOperator_DifferentReferencesSameComponent_ReturnsTrue()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef1 = entity.CreateComponent<PositionComponent>();
            var componentRef2 = entity.GetComponent<PositionComponent>(); // Get same component again
            
            // Act
            var result = componentRef1.Equals(componentRef2);
            
            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public void ComponentRef_EqualsOperator_DifferentComponents_ReturnsFalse()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            var velocityRef = entity.CreateComponent<VelocityComponent>();
            
            // Act
            var result = positionRef.Equals(velocityRef);
            
            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void ComponentRef_EqualsOperator_NullComparison_ReturnsFalse()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();
            
            // Act
            var result = componentRef.Equals(null);
            
            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void ComponentRef_EqualityOperator_SameComponent_ReturnsTrue()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef1 = entity.CreateComponent<PositionComponent>();
            var componentRef2 = entity.GetComponent<PositionComponent>(); // Same component
            
            // Act
            var result = componentRef1 == componentRef2;
            
            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public void ComponentRef_InequalityOperator_DifferentComponents_ReturnsTrue()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            var velocityRef = entity.CreateComponent<VelocityComponent>();
            
            // Act
            var result = (ComponentRef) positionRef != velocityRef;
            
            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public void ComponentRef_ImplicitConversion_ToUntyped_RefWorks()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var typedRef = entity.CreateComponent<PositionComponent>();
            
            // Act - Implicit conversion to untyped reference
            ComponentRef untypedRef = typedRef;
            
            // Assert
            Assert.IsTrue(untypedRef.NotNull);
            Assert.AreEqual(typedRef.EntityId, untypedRef.EntityId);
        }

        [Test]
        public void ComponentRef_ExpandMethod_CreatesValidUntypedReference()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var typedRef = entity.CreateComponent<PositionComponent>();
            
            // Act
            var untypedRef = typedRef.Untyped();
            
            // Assert
            Assert.IsTrue(untypedRef.NotNull);
            Assert.AreEqual(typedRef.EntityId, untypedRef.EntityId);
            Assert.AreEqual(typeof(PositionComponent), untypedRef.Core.RefLocator.GetT());
        }

        [Test]
        public void ComponentRef_GetComponentDataThroughRWProperty_Works()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();
            componentRef.RW = new PositionComponent { X = 50, Y = 100 };
            
            // Act
            var retrievedValue = componentRef.RW;
            
            // Assert
            Assert.AreEqual(50, retrievedValue.X);
            Assert.AreEqual(100, retrievedValue.Y);
        }

        [Test]
        public void Entity_GetComponents_Array_ReturnsCorrectCount()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();
            entity.CreateComponent<HealthComponent>();
            
            // Act
            var allComponents = entity.GetComponents();
            
            // Assert
            Assert.AreEqual(3, allComponents.Length);
            
            // Verify each component is of expected type
            var hasPosition = false;
            var hasVelocity = false;
            var hasHealth = false;
            
            foreach (var comp in allComponents)
            {
                var type = comp.RuntimeType;
                if (type == typeof(PositionComponent)) hasPosition = true;
                else if (type == typeof(VelocityComponent)) hasVelocity = true;
                else if (type == typeof(HealthComponent)) hasHealth = true;
            }
            
            Assert.IsTrue(hasPosition && hasVelocity && hasHealth);
        }
    
        [Test]
        public void Entity_CanCreateComponentWithInitialValue()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var initialValue = new PositionComponent { X = 30, Y = 40 };
            
            // Act
            var positionRef = entity.CreateComponent<PositionComponent>(initialValue);
            
            // Assert
            Assert.IsNotNull(positionRef);
            Assert.AreEqual(30, positionRef.RW.X);
            Assert.AreEqual(40, positionRef.RW.Y);
        }
        
        [Test]
        public void Entity_CreateComponentWithInitialValueAndModify()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var initialValue = new PositionComponent { X = 50, Y = 60 };
            
            // Act
            var positionRef = entity.CreateComponent<PositionComponent>(initialValue);
            positionRef.RW = new PositionComponent { X = 70, Y = 80 };
            
            // Assert
            Assert.IsNotNull(positionRef);
            Assert.AreEqual(70, positionRef.RW.X);  // Modified value
            Assert.AreEqual(80, positionRef.RW.Y);  // Modified value
        }
        
        [Test]
        public void Entity_CreateMultipleComponentsWithInitialValues()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionValue = new PositionComponent { X = 10, Y = 20 };
            var velocityValue = new VelocityComponent { X = 5, Y = 10 };
            var healthValue = new HealthComponent { Value = 100f };
            
            // Act
            var positionRef = entity.CreateComponent<PositionComponent>(positionValue);
            var velocityRef = entity.CreateComponent<VelocityComponent>(velocityValue);
            var healthRef = entity.CreateComponent<HealthComponent>(healthValue);
            
            // Assert
            Assert.IsNotNull(positionRef);
            Assert.IsNotNull(velocityRef);
            Assert.IsNotNull(healthRef);
            
            Assert.AreEqual(10, positionRef.RW.X);
            Assert.AreEqual(20, positionRef.RW.Y);
            Assert.AreEqual(5, velocityRef.RW.X);
            Assert.AreEqual(10, velocityRef.RW.Y);
            Assert.AreEqual(100f, healthRef.RW.Value);
        }
        
        [Test]
        public void Entity_CreateComponentWithInitialValueAndCompareWithDefault()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var initialValue = new PositionComponent { X = 100, Y = 200 };
            
            // Act
            var defaultRef = entity1.CreateComponent<PositionComponent>();  // Default initialization
            var initialRef = entity2.CreateComponent<PositionComponent>(initialValue);  // With initial value
            
            // Assert
            // Default initialized component should have default values (0, 0)
            Assert.AreEqual(0, defaultRef.RW.X);
            Assert.AreEqual(0, defaultRef.RW.Y);
            
            // Component with initial value should have the specified values
            Assert.AreEqual(100, initialRef.RW.X);
            Assert.AreEqual(200, initialRef.RW.Y);
        }
        
        #endregion

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