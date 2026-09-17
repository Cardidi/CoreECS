using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
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
        public void ComponentManager_OnComponentCreated_Dense_EmitsEntityIdAndType()
        {
            // Arrange
            ulong capturedEntityId = 0;
            Type capturedType = null;
            _componentManager.OnComponentCreated.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            var entity = _world.CreateEntity();

            // Act
            entity.CreateComponent<PositionComponent>();

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PositionComponent), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentCreated_Discrete_EmitsEntityIdAndType()
        {
            // Arrange
            ulong capturedEntityId = 0;
            Type capturedType = null;
            _componentManager.OnComponentCreated.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            var entity = _world.CreateEntity();

            // Act
            entity.CreateComponent(new ManaComponent { Value = 3 });

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(ManaComponent), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentCreated_Tag_EmitsEntityIdAndType()
        {
            // Arrange
            ulong capturedEntityId = 0;
            Type capturedType = null;
            _componentManager.OnComponentCreated.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            var entity = _world.CreateEntity();

            // Act
            entity.CreateComponent<PlayerTag>();

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PlayerTag), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentRemoved_Dense_EmitsEntityIdAndType()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();

            ulong capturedEntityId = 0;
            Type capturedType = null;
            _componentManager.OnComponentRemoved.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            entity.DestroyComponent(componentRef);

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PositionComponent), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentRemoved_Discrete_EmitsEntityIdAndType()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent(new ManaComponent { Value = 3 });

            ulong capturedEntityId = 0;
            Type capturedType = null;
            _componentManager.OnComponentRemoved.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            entity.DestroyComponent(componentRef);

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(ManaComponent), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentRemoved_Tag_EmitsEntityIdAndType()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PlayerTag>();

            ulong capturedEntityId = 0;
            Type capturedType = null;
            _componentManager.OnComponentRemoved.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            entity.DestroyComponent<PlayerTag>();

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PlayerTag), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentChanged_Dense_EmitsOnWritableAccess()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();

            ulong capturedEntityId = 0;
            Type capturedType = null;
            var changeCount = 0;
            _componentManager.OnComponentChanged.Add((entityId, compType) =>
            {
                changeCount += 1;
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            componentRef.RW.X = 1.0f;

            // Assert
            Assert.AreEqual(1, changeCount);
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PositionComponent), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentChanged_Discrete_EmitsOnWritableAccess()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent(new ManaComponent { Value = 1 });

            ulong capturedEntityId = 0;
            Type capturedType = null;
            var changeCount = 0;
            _componentManager.OnComponentChanged.Add((entityId, compType) =>
            {
                changeCount += 1;
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            componentRef.RW.Value = 2;

            // Assert
            Assert.AreEqual(1, changeCount);
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(ManaComponent), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentRemoved_EntityDestroy_EmitsNoComponentSignal()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent(new ManaComponent { Value = 1 });
            entity.CreateComponent<PlayerTag>();

            var removedCount = 0;
            _componentManager.OnComponentRemoved.Add((entityId, compType) => removedCount += 1);

            // Act - the kernel raises no per-component removal events on entity destroy
            _world.DestroyEntity(entity);

            // Assert
            Assert.AreEqual(0, removedCount);
        }

        [Test]
        public void ComponentManager_OnCreateAndOnDestroyHooks_RunThroughEntity()
        {
            // Arrange
            LifecycleComponent.CreateCount = 0;
            LifecycleComponent.DestroyCount = 0;
            var entity = _world.CreateEntity();

            // Act - create
            var componentRef = entity.CreateComponent<LifecycleComponent>();

            // Assert - creation hook ran on the stored instance
            Assert.IsTrue(componentRef.RW.OnCreateCalled);
            Assert.IsFalse(componentRef.RW.OnDestroyCalled);
            Assert.AreEqual(1, LifecycleComponent.CreateCount);

            // Act - destroy the entity
            _world.DestroyEntity(entity);

            // Assert - destruction hook ran and the ref is cut
            Assert.IsFalse(componentRef.NotNull);
            Assert.AreEqual(1, LifecycleComponent.DestroyCount);
        }

        // Test components
        private struct PositionComponent : IComponent<PositionComponent>
        {
            public float X;
            public float Y;
        }

        private struct ManaComponent : IDiscreteComponent<ManaComponent>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private struct LifecycleComponent : IComponent<LifecycleComponent>
        {
            public static int CreateCount;
            public static int DestroyCount;

            public bool OnCreateCalled;
            public bool OnDestroyCalled;

            public void OnCreate(ulong entityId)
            {
                CreateCount += 1;
                OnCreateCalled = true;
            }

            public void OnDestroy(ulong entityId)
            {
                DestroyCount += 1;
                OnDestroyCalled = true;
            }
        }
    }
}
