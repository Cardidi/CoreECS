using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityManagerTestUnit
    {
        private World _world;
        private EntityManager _entityManager;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
            _entityManager = _world.GetManager<EntityManager>();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
        }

        [Test]
        public void EntityManager_CreateEntity_AllocatesIncreasingIdsAndRegistersLocations()
        {
            // Act
            var first = _entityManager.CreateEntity();
            var second = _entityManager.CreateEntity();

            // Assert
            Assert.AreEqual(1UL, first.EntityId);
            Assert.AreEqual(2UL, second.EntityId);
            Assert.IsTrue(first.IsValid);
            Assert.IsTrue(second.IsValid);
            Assert.AreEqual(2, _entityManager.Table.Count);

            Assert.IsTrue(_entityManager.Table.TryGetLocation(first.EntityId, out var firstLocation));
            Assert.IsTrue(_entityManager.Table.TryGetLocation(second.EntityId, out var secondLocation));
            Assert.IsNotNull(firstLocation);
            Assert.IsNotNull(secondLocation);
            Assert.AreNotSame(firstLocation, secondLocation);
        }

        [Test]
        public void EntityManager_CreateEntity_WithInitialMask_SelectsMaskStructure()
        {
            // Act
            var entity = _entityManager.CreateEntity(0b1010);

            // Assert
            Assert.IsTrue(entity.IsValid);
            Assert.AreEqual(0b1010UL, entity.Mask);
            Assert.IsTrue(_entityManager.Table.TryGetLocation(entity.EntityId, out var location));
            Assert.IsNotNull(location.Structure);
            Assert.AreEqual(0b1010UL, location.Structure.Mask);
        }

        [Test]
        public void EntityManager_GetEntity_ReturnsLiveHandleAndDefaultForUnknownId()
        {
            // Arrange
            var created = _entityManager.CreateEntity();

            // Act
            var retrieved = _entityManager.GetEntity(created.EntityId);
            var unknown = _entityManager.GetEntity(999999);

            // Assert
            Assert.IsTrue(retrieved.IsValid);
            Assert.AreEqual(created.EntityId, retrieved.EntityId);
            Assert.AreSame(_world, retrieved.World);

            Assert.IsFalse(unknown.IsValid);
            Assert.AreEqual(0UL, unknown.EntityId);
        }

        [Test]
        public void EntityManager_DestroyEntity_RemovesEntityAndReleasesLocationWithNewGeneration()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            Assert.IsTrue(_entityManager.Table.TryGetLocation(entity.EntityId, out var location));
            var generation = location.Generation;

            // Act
            _entityManager.DestroyEntity(entity.EntityId);

            // Assert
            Assert.AreEqual(0, _entityManager.Table.Count);
            Assert.IsFalse(_entityManager.Table.TryGetLocation(entity.EntityId, out _));
            Assert.IsFalse(entity.IsValid);
            Assert.IsNull(location.Structure);
            Assert.AreEqual(generation + 1U, location.Generation);
        }

        [Test]
        public void EntityManager_CreateEntity_AfterDestroy_ReusesReleasedLocationWithNewerGeneration()
        {
            // Arrange - drain the shared pool so the released location is the only reuse candidate
            EntityLocation.Pool.Clear();
            var first = _entityManager.CreateEntity();
            Assert.IsTrue(_entityManager.Table.TryGetLocation(first.EntityId, out var staleLocation));
            var staleGeneration = staleLocation.Generation;

            // Act
            _entityManager.DestroyEntity(first.EntityId);
            var second = _entityManager.CreateEntity();

            // Assert
            Assert.AreNotEqual(first.EntityId, second.EntityId);
            Assert.IsTrue(_entityManager.Table.TryGetLocation(second.EntityId, out var current));
            Assert.AreSame(staleLocation, current);
            Assert.Greater(current.Generation, staleGeneration);
        }

        [Test]
        public void EntityManager_DestroyEntity_UnknownId_IsNoOp()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();

            // Act & Assert - unknown ids and repeated destroys are ignored
            Assert.DoesNotThrow(() => _entityManager.DestroyEntity(999999));
            Assert.DoesNotThrow(() => _entityManager.DestroyEntity(entity.EntityId));
            Assert.DoesNotThrow(() => _entityManager.DestroyEntity(entity.EntityId));

            Assert.AreEqual(0, _entityManager.Table.Count);
            Assert.IsFalse(_entityManager.Table.TryGetLocation(entity.EntityId, out _));
        }

        [Test]
        public void EntityManager_OnEntityGotComp_EmitsEntityIdAndComponentType()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            ulong capturedEntityId = 0;
            Type capturedType = null;
            _entityManager.OnEntityGotComp.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            entity.CreateComponent<PositionComponent>();

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PositionComponent), capturedType);
        }

        [Test]
        public void EntityManager_OnEntityLoseComp_EmitsOnComponentDestroy()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();
            ulong capturedEntityId = 0;
            Type capturedType = null;
            _entityManager.OnEntityLoseComp.Add((entityId, compType) =>
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
        public void EntityManager_OnEntityLoseComp_EmitsNullTypeOnEntityDestroy()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            ulong capturedEntityId = 0;
            Type capturedType = typeof(object);
            var loseCount = 0;
            _entityManager.OnEntityLoseComp.Add((entityId, compType) =>
            {
                loseCount += 1;
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            _entityManager.DestroyEntity(entity.EntityId);

            // Assert - exactly one lose event, with a null component type
            Assert.AreEqual(1, loseCount);
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.IsNull(capturedType);
        }

        [Test]
        public void EntityManager_OnEntityLoseComp_ReentrantDestroy_EmitsExactlyOneEvent()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            entity.CreateComponent<SelfDestroyingComponent>();
            var loseCount = 0;
            _entityManager.OnEntityLoseComp.Add((entityId, compType) => loseCount += 1);
            SelfDestroyingComponent.DestroyAction = id => _world.DestroyEntity(_entityManager.GetEntity(id));

            // Act - the OnDestroy hook re-enters destroy for the same entity
            _world.DestroyEntity(entity);

            // Assert - the manager guard rejects the re-entrant call and emits once
            Assert.AreEqual(1, loseCount);
            Assert.AreEqual(0, _entityManager.Table.Count);
            Assert.IsFalse(entity.IsValid);

            SelfDestroyingComponent.DestroyAction = null;
        }

        [Test]
        public void EntityManager_OnEntityChangeComp_EmitsOnWritableAccess()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();
            ulong capturedEntityId = 0;
            Type capturedType = null;
            _entityManager.OnEntityChangeComp.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            componentRef.RW.X = 1.0f;

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PositionComponent), capturedType);
        }

        [Test]
        public void ComponentRef_RW_ChangeHandlerMigratesEntity_WritesLiveLocation()
        {
            // Arrange - fill the shared position structure so the migrating entity's old
            // row is taken over by another entity, and seed the target structure.
            var first = _entityManager.CreateEntity();
            var second = _entityManager.CreateEntity();
            var migrating = _entityManager.CreateEntity();
            var target = _entityManager.CreateEntity();

            first.CreateComponent<PositionComponent>().RW = new PositionComponent { X = 1 };
            second.CreateComponent<PositionComponent>().RW = new PositionComponent { X = 2 };
            var migratingRef = migrating.CreateComponent<PositionComponent>();
            migratingRef.RW = new PositionComponent { X = 3 };
            target.CreateComponent<PositionComponent>().RW = new PositionComponent { X = 4 };
            target.CreateComponent<VelocityComponent>();

            // The change handler migrates the entity by adding a dense component.
            _entityManager.OnEntityChangeComp.Add((entityId, _) =>
            {
                if (entityId == migrating.EntityId && !migrating.HasComponent<VelocityComponent>())
                    migrating.CreateComponent<VelocityComponent>();
            });

            // Act - RW emits the change event (migrating the entity), then resolves the ref
            ref var writable = ref migratingRef.RW;
            writable.X = 30;

            // Assert - the write lands on the migrating entity, not on the row's new owner
            Assert.AreEqual(30.0f, migrating.GetComponent<PositionComponent>().RW.X);
            Assert.AreEqual(1.0f, first.GetComponent<PositionComponent>().RW.X);
            Assert.AreEqual(2.0f, second.GetComponent<PositionComponent>().RW.X);
            Assert.AreEqual(4.0f, target.GetComponent<PositionComponent>().RW.X);
        }

        [Test]
        public void EntityManager_Events_AreNotNull()
        {
            Assert.IsNotNull(_entityManager.OnEntityGotComp);
            Assert.IsNotNull(_entityManager.OnEntityLoseComp);
            Assert.IsNotNull(_entityManager.OnEntityChangeComp);
        }

        [Test]
        public void EntityManager_WorldProperty_ReturnsCorrectWorld()
        {
            Assert.IsNotNull(_entityManager.World);
            Assert.AreSame(_world, _entityManager.World);
        }

        [Test]
        public void EntityManager_Shutdown_ReleasesAllLocationsAndRejectsNewEntities()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            Assert.IsTrue(_entityManager.Table.TryGetLocation(entity.EntityId, out var location));

            // Act
            _world.Shutdown();

            // Assert - shutdown releases every location (v1 parity) and the manager rejects new work
            Assert.AreEqual(0, _entityManager.Table.Count);
            Assert.IsNull(location.Structure);
            Assert.IsFalse(entity.IsValid);
            Assert.Throws<InvalidOperationException>(() => _entityManager.CreateEntity());

            _world = null;
        }

        // Test components
        private struct PositionComponent : IComponent<PositionComponent>
        {
            public float X;
            public float Y;
        }

        private struct VelocityComponent : IComponent<VelocityComponent>
        {
        }

        private struct ManaComponent : ISparseComponent<ManaComponent>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private struct SelfDestroyingComponent : IComponent<SelfDestroyingComponent>
        {
            public static Action<ulong> DestroyAction;

            public void OnDestroy(ulong entityId) => DestroyAction?.Invoke(entityId);
        }
    }
}
