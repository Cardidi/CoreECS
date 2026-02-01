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
    public class ComponentTestUnit
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
        public void ComponentRef_CanAccessComponentData()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            
            // Act
            positionRef.RW = new PositionComponent { X = 15, Y = 25 };
            
            // Assert
            Assert.AreEqual(15, positionRef.RW.X);
            Assert.AreEqual(25, positionRef.RW.Y);
            
            // Modify through reference
            positionRef.RW.X = 30;
            Assert.AreEqual(30, entity.GetComponent<PositionComponent>().RW.X);
        }
        
        [Test]
        public void ComponentManager_CanHandleMultipleComponentTypes()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            
            // Act
            entity1.CreateComponent<PositionComponent>();
            entity1.CreateComponent<VelocityComponent>();
            entity2.CreateComponent<PositionComponent>();
            entity2.CreateComponent<HealthComponent>();
            
            // Assert
            var componentManager = _world.GetManager<ComponentManager>();
            
            Assert.IsTrue(entity1.HasComponent<PositionComponent>());
            Assert.IsTrue(entity1.HasComponent<VelocityComponent>());
            Assert.IsFalse(entity1.HasComponent<HealthComponent>());
            
            Assert.IsTrue(entity2.HasComponent<PositionComponent>());
            Assert.IsFalse(entity2.HasComponent<VelocityComponent>());
            Assert.IsTrue(entity2.HasComponent<HealthComponent>());
        }
        
        [Test]
        public void Component_OnCreateAndDestroy_CalledCorrectly()
        {
            // Arrange
            var entity = _world.CreateEntity();
            
            // Act
            var componentRef = entity.CreateComponent<LifecycleComponent>();
            
            // Assert
            Assert.IsTrue(componentRef.RW.OnCreateCalled);
            Assert.IsFalse(componentRef.RW.OnDestroyCalled);
            
            // Act
            entity.DestroyComponent(componentRef);
            
            // Assert
            Assert.Throws<NullReferenceException>(() => { _ = componentRef.RW.OnDestroyCalled; });
        }
        
        [Test]
        public void ComponentManager_ComponentPoolExpansion()
        {
            // Arrange
            const int entityCount = 100;
            var entities = new List<Entity>();
            
            // Act
            for (int i = 0; i < entityCount; i++)
            {
                var entity = _world.CreateEntity();
                entity.CreateComponent<LargeComponent>();
                entities.Add(entity);
            }
            
            // Assert
            foreach (var entity in entities)
            {
                Assert.IsTrue(entity.HasComponent<LargeComponent>());
                var component = entity.GetComponent<LargeComponent>();
                Assert.AreEqual(100, component.RW.Value.Data.Length);
            }
            
            // Cleanup
            foreach (var entity in entities)
            {
                _world.DestroyEntity(entity);
            }
        }
        
        [Test]
        public void ComponentRef_CanCheckType()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            
            // Act & Assert
            Assert.IsTrue(positionRef.Core.RefLocator.IsT(typeof(PositionComponent)));
            Assert.IsFalse(positionRef.Core.RefLocator.IsT(typeof(VelocityComponent)));
        }
        
        [Test]
        public void ComponentRef_CanGetEntityType()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            
            // Act
            var entityType = positionRef.Core.RefLocator.GetT();
            
            // Assert
            Assert.AreEqual(typeof(PositionComponent), entityType);
        }
        
        [Test]
        public void ComponentRef_CanGetEntityId()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            
            // Act
            var entityId = positionRef.Core.RefLocator.GetEntityId(positionRef.Core.Offset);
            
            // Assert
            Assert.AreEqual(entity.EntityId, entityId);
        }
        
        [Test]
        public void ComponentRef_CanGetRefCore()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            
            // Act
            var refCore = positionRef.Core;
            
            // Assert
            Assert.IsNotNull(refCore);
            Assert.AreEqual(positionRef.Core.Offset, refCore.Offset);
        }
        
        [Test]
        public void ComponentRef_CanRelocate()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            var originalOffset = positionRef.Core.Offset;
            var originalVersion = positionRef.Core.Version;
            
            // Act
            (positionRef.Core as ComponentRefCore).Allocate(positionRef.Core.RefLocator, originalOffset + 1, positionRef.Core.Version + 1);
            
            // Assert
            Assert.AreEqual(originalOffset + 1, positionRef.Core.Offset);
            Assert.AreEqual(originalVersion + 1, positionRef.Core.Version);
        }

        [Test]
        public void ComponentRef_EqualityOperator_SameCore_EqualsTrue()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef1 = entity.CreateComponent<PositionComponent>();
            var positionRef2 = entity.GetComponent<PositionComponent>(); // Same component, same core
            
            // Act
            var isEqual = positionRef1 == positionRef2;
            var isNotEqual = positionRef1 != positionRef2;
            
            // Assert
            Assert.IsTrue(isEqual);
            Assert.IsFalse(isNotEqual);
        }

        [Test]
        public void ComponentRef_EqualityOperator_DifferentCore_EqualsFalse()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var positionRef1 = entity1.CreateComponent<PositionComponent>();
            var positionRef2 = entity2.CreateComponent<PositionComponent>(); // Different components, different cores
            
            // Act
            var isEqual = positionRef1 == positionRef2;
            var isNotEqual = positionRef1 != positionRef2;
            
            // Assert
            Assert.IsFalse(isEqual);
            Assert.IsTrue(isNotEqual);
        }

        [Test]
        public void ComponentRef_TypedEqualityOperator_SameCore_EqualsTrue()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef1 = entity.CreateComponent<PositionComponent>();
            var positionRef2 = entity.GetComponent<PositionComponent>(); // Same component, same core
            
            // Act
            var isEqual = positionRef1 == positionRef2;
            var isNotEqual = positionRef1 != positionRef2;
            
            // Assert
            Assert.IsTrue(isEqual);
            Assert.IsFalse(isNotEqual);
        }

        [Test]
        public void ComponentRef_TypedEqualityOperator_DifferentCore_EqualsFalse()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var positionRef1 = entity1.CreateComponent<PositionComponent>();
            var positionRef2 = entity2.CreateComponent<PositionComponent>(); // Different components, different cores
            
            // Act
            var isEqual = positionRef1 == positionRef2;
            var isNotEqual = positionRef1 != positionRef2;
            
            // Assert
            Assert.IsFalse(isEqual);
            Assert.IsTrue(isNotEqual);
        }

        [Test]
        public void ComponentRef_EqualityWithNull()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            ComponentRef nullRef = new ComponentRef(null); // Create a null reference
            
            // Act
            var isEqualToNull = nullRef == new ComponentRef(null);
            var isNotEqualToValid = positionRef == new ComponentRef(null);
            var isValidNotEqualToNull = positionRef != new ComponentRef(null);
            
            // Assert
            Assert.IsTrue(isEqualToNull);
            Assert.IsFalse(isNotEqualToValid);
            Assert.IsTrue(isValidNotEqualToNull);
        }

        [Test]
        public void ComponentRef_TypedEqualityWithNull()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            ComponentRef<PositionComponent> nullTypedRef = new ComponentRef<PositionComponent>(null); // Create a null typed reference
            
            // Act
            var isEqualToNull = nullTypedRef == new ComponentRef<PositionComponent>(null);
            var isNotEqualToValid = positionRef == new ComponentRef<PositionComponent>(null);
            var isValidNotEqualToNull = positionRef != new ComponentRef<PositionComponent>(null);
            
            // Assert
            Assert.IsTrue(isEqualToNull);
            Assert.IsFalse(isNotEqualToValid);
            Assert.IsTrue(isValidNotEqualToNull);
        }

        [Test]
        public void ComponentRef_EqualsMethod_SameCore_EqualsTrue()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef1 = entity.CreateComponent<PositionComponent>();
            var positionRef2 = entity.GetComponent<PositionComponent>(); // Same component, same core
            
            // Act
            var equalsResult = positionRef1.Equals(positionRef2);
            var objectEqualsResult = positionRef1.Equals((object)positionRef2);
            
            // Assert
            Assert.IsTrue(equalsResult);
            Assert.IsTrue(objectEqualsResult);
        }

        [Test]
        public void ComponentRef_EqualsMethod_DifferentCore_EqualsFalse()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var positionRef1 = entity1.CreateComponent<PositionComponent>();
            var positionRef2 = entity2.CreateComponent<PositionComponent>(); // Different components, different cores
            
            // Act
            var equalsResult = positionRef1.Equals(positionRef2);
            var objectEqualsResult = positionRef1.Equals((object)positionRef2);
            
            // Assert
            Assert.IsFalse(equalsResult);
            Assert.IsFalse(objectEqualsResult);
        }

        [Test]
        public void ComponentRef_EqualsMethod_WithNull()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            ComponentRef nullRef = new ComponentRef(null);
            
            // Act
            var equalsNull = positionRef.Equals(nullRef);
            var equalsObjectNull = positionRef.Equals((object)null);
            
            // Assert
            Assert.IsFalse(equalsNull);
            Assert.IsFalse(equalsObjectNull);
        }

        [Test]
        public void ComponentRef_TypedEqualsMethod_SameCore_EqualsTrue()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef1 = entity.CreateComponent<PositionComponent>();
            var positionRef2 = entity.GetComponent<PositionComponent>(); // Same component, same core
            
            // Act
            var equalsResult = positionRef1.Equals(positionRef2);
            var objectEqualsResult = positionRef1.Equals((object)positionRef2);
            
            // Assert
            Assert.IsTrue(equalsResult);
            Assert.IsTrue(objectEqualsResult);
        }

        [Test]
        public void ComponentRef_TypedEqualsMethod_DifferentCore_EqualsFalse()
        {
            // Arrange
            var entity1 = _world.CreateEntity();
            var entity2 = _world.CreateEntity();
            var positionRef1 = entity1.CreateComponent<PositionComponent>();
            var positionRef2 = entity2.CreateComponent<PositionComponent>(); // Different components, different cores
            
            // Act
            var equalsResult = positionRef1.Equals(positionRef2);
            var objectEqualsResult = positionRef1.Equals((object)positionRef2);
            
            // Assert
            Assert.IsFalse(equalsResult);
            Assert.IsFalse(objectEqualsResult);
        }

        [Test]
        public void ComponentRef_TypedEqualsMethod_WithNull()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            ComponentRef<PositionComponent> nullRef = new ComponentRef<PositionComponent>(null);
            
            // Act
            var equalsNull = positionRef.Equals(nullRef);
            var equalsObjectNull = positionRef.Equals((object)null);
            
            // Assert
            Assert.IsFalse(equalsNull);
            Assert.IsFalse(equalsObjectNull);
        }

        [Test]
        public void ComponentRef_HashCode_SameCore_Equals()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef1 = entity.CreateComponent<PositionComponent>();
            var positionRef2 = entity.GetComponent<PositionComponent>(); // Same component, same core
            
            // Act
            var hashCode1 = positionRef1.GetHashCode();
            var hashCode2 = positionRef2.GetHashCode();
            
            // Assert
            Assert.AreEqual(hashCode1, hashCode2);
        }

        [Test]
        public void ComponentRef_TypedHashCode_SameCore_Equals()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef1 = entity.CreateComponent<PositionComponent>();
            var positionRef2 = entity.GetComponent<PositionComponent>(); // Same component, same core
            
            // Act
            var hashCode1 = positionRef1.GetHashCode();
            var hashCode2 = positionRef2.GetHashCode();
            
            // Assert
            Assert.AreEqual(hashCode1, hashCode2);
        }

        [Test]
        public void ComponentRef_ImplicitConversion_FromTypedToUntyped()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var typedRef = entity.CreateComponent<PositionComponent>();
            
            // Act - implicit conversion
            ComponentRef untypedRef = typedRef;
            
            // Assert
            Assert.IsTrue(untypedRef.NotNull);
            Assert.AreEqual(typedRef.Core.Offset, untypedRef.Core.Offset);
            Assert.AreEqual(typedRef.Core.Version, untypedRef.Core.Version);
            Assert.AreEqual(typedRef.Core.RefLocator, untypedRef.Core.RefLocator);
        }

        [Test]
        public void ComponentRef_ExplicitConversion_FromUntypedToTyped()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var typedRef = entity.CreateComponent<PositionComponent>();
            ComponentRef untypedRef = typedRef; // Use implicit conversion to get untyped ref
            
            // Act - explicit conversion
            ComponentRef<PositionComponent> convertedTypedRef = (ComponentRef<PositionComponent>)untypedRef;
            
            // Assert
            Assert.IsTrue(convertedTypedRef.NotNull);
            Assert.AreEqual(typedRef.Core.Offset, convertedTypedRef.Core.Offset);
            Assert.AreEqual(typedRef.Core.Version, convertedTypedRef.Core.Version);
            Assert.AreEqual(typedRef.Core.RefLocator, convertedTypedRef.Core.RefLocator);
        }

        [Test]
        public void ComponentRef_ExplicitConversion_InvalidType_ThrowsException()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            var velocityRef = entity.CreateComponent<VelocityComponent>();
            
            // Get untyped reference to velocity component
            ComponentRef untypedRef = velocityRef; // Use implicit conversion
            
            // Act & Assert - trying to convert to wrong type should throw
            Assert.Throws<InvalidCastException>(() => {
                ComponentRef<PositionComponent> convertedTypedRef = (ComponentRef<PositionComponent>)untypedRef;
            });
        }

        [Test]
        public void ComponentRef_Typed_Method_SameType_Success()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            var untypedRef = (ComponentRef)positionRef; // Convert to untyped using implicit conversion
            
            // Act
            var typedRef = untypedRef.Typed<PositionComponent>();
            
            // Assert
            Assert.IsTrue(typedRef.NotNull);
            Assert.AreEqual(positionRef.Core.Offset, typedRef.Core.Offset);
            Assert.AreEqual(positionRef.Core.Version, typedRef.Core.Version);
            Assert.AreEqual(positionRef.Core.RefLocator, typedRef.Core.RefLocator);
        }

        [Test]
        public void ComponentRef_Typed_Method_WrongType_ThrowsException()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            var untypedRef = (ComponentRef)positionRef; // Convert to untyped using implicit conversion
            
            // Act & Assert
            Assert.Throws<InvalidCastException>(() => {
                var typedRef = untypedRef.Typed<VelocityComponent>();
            });
        }

        [Test]
        public void ComponentRef_Untyped_Method_Success()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var typedRef = entity.CreateComponent<PositionComponent>();
            
            // Act
            var untypedRef = typedRef.Untyped();
            
            // Assert
            Assert.IsTrue(untypedRef.NotNull);
            Assert.AreEqual(typedRef.Core.Offset, untypedRef.Core.Offset);
            Assert.AreEqual(typedRef.Core.Version, untypedRef.Core.Version);
            Assert.AreEqual(typedRef.Core.RefLocator, untypedRef.Core.RefLocator);
        }

        [Test]
        public void ComponentRef_Conversion_NullReference_HandlesProperly()
        {
            // Test converting null typed reference to untyped
            ComponentRef<PositionComponent> nullTypedRef = new ComponentRef<PositionComponent>(null);


            Assert.Throws<NullReferenceException>(() =>
            {
                ComponentRef convertedToUntyped = nullTypedRef; // implicit conversion
            });
            
            // Test converting null untyped reference to typed
            ComponentRef nullUntypedRef = new ComponentRef(null);
            Assert.Throws<NullReferenceException>(() => {
                var result = nullUntypedRef.Typed<PositionComponent>(); // Should throw when untyping null
            });
        }

        [Test]
        public void ComponentRef_Typed_WithNoSafeCheck_False_ValidType_Success()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            var untypedRef = (ComponentRef)positionRef; // Convert to untyped using implicit conversion
            
            // Act - with safe check (default)
            var typedRef = untypedRef.Typed<PositionComponent>(noSafeCheck: false);
            
            // Assert
            Assert.IsTrue(typedRef.NotNull);
            Assert.AreEqual(positionRef.Core.Offset, typedRef.Core.Offset);
            Assert.AreEqual(positionRef.Core.Version, typedRef.Core.Version);
            Assert.AreEqual(positionRef.Core.RefLocator, typedRef.Core.RefLocator);
        }

        [Test]
        public void ComponentRef_Untyped_AfterImplicitConversion()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var typedRef = entity.CreateComponent<PositionComponent>();
            
            // Act - First implicit conversion to untyped, then untype should return same
            ComponentRef untypedRef = typedRef;
            var untypedAgainRef = typedRef.Untyped();
            
            // Both untypedRef and untypedAgainRef should be equivalent
            Assert.IsTrue(untypedRef.NotNull);
            Assert.IsTrue(untypedAgainRef.NotNull);
            Assert.AreEqual(untypedRef.Core.Offset, untypedAgainRef.Core.Offset);
            Assert.AreEqual(untypedRef.Core.Version, untypedAgainRef.Core.Version);
        }

        [Test]
        public void ComponentRef_GetComponents_ReturnsUntypedRefs()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            var velocityRef = entity.CreateComponent<VelocityComponent>();
            
            // Act
            var allComponents = entity.GetComponents(); // This returns ComponentRef[] (untyped)
            
            // Assert
            Assert.AreEqual(2, allComponents.Length);
            // Verify that we can convert back to typed references
            bool foundPosition = false;
            bool foundVelocity = false;
            
            foreach (var compRef in allComponents)
            {
                if (compRef.Core.RefLocator.IsT(typeof(PositionComponent)))
                {
                    foundPosition = true;
                    var typedPosRef = compRef.Typed<PositionComponent>();
                    Assert.AreEqual(positionRef.Core.Offset, typedPosRef.Core.Offset);
                }
                else if (compRef.Core.RefLocator.IsT(typeof(VelocityComponent)))
                {
                    foundVelocity = true;
                    var typedVelRef = compRef.Typed<VelocityComponent>();
                    Assert.AreEqual(velocityRef.Core.Offset, typedVelRef.Core.Offset);
                }
            }
            
            Assert.IsTrue(foundPosition);
            Assert.IsTrue(foundVelocity);
        }

        [Test]
        public void ComponentRef_UntypedThenTyped_ReturnsOriginal()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var originalTypedRef = entity.CreateComponent<PositionComponent>();
            
            // Act
            var untypedRef = originalTypedRef.Untyped();           // Typed -> Untyped
            var retypedRef = untypedRef.Typed<PositionComponent>(); // Untyped -> Typed
            
            // Assert
            Assert.IsTrue(retypedRef.NotNull);
            Assert.AreEqual(originalTypedRef.Core.Offset, retypedRef.Core.Offset);
            Assert.AreEqual(originalTypedRef.Core.Version, retypedRef.Core.Version);
            Assert.AreEqual(originalTypedRef.Core.RefLocator, retypedRef.Core.RefLocator);
            Assert.AreEqual(originalTypedRef.RW.X, retypedRef.RW.X);
            Assert.AreEqual(originalTypedRef.RW.Y, retypedRef.RW.Y);
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
        
        private struct LargeComponent : IComponent<LargeComponent>
        {
            public LargeData Value;
            
            public struct LargeData
            {
                public int[] Data;
                
                public LargeData(int size)
                {
                    Data = new int[size];
                }
            }

            public void OnCreate(ulong entityId)
            {
                Value = new LargeData(100);
            }
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