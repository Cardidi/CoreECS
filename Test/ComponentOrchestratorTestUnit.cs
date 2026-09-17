using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentOrchestratorTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct ManaComponent : IDiscreteComponent<ManaComponent>
        {
            public int Value;

            public void OnCreate(ulong entityId)
            {
                CreateCount += 1;
                CreateAction?.Invoke(entityId);
            }

            public void OnDestroy(ulong entityId)
            {
                DestroyCount += 1;
                LastDestroyedValue = Value;
                DestroyAction?.Invoke(entityId);
            }

            public static int CreateCount;
            public static int DestroyCount;
            public static int LastDestroyedValue;
            public static Action<ulong> CreateAction;
            public static Action<ulong> DestroyAction;
        }

        private struct OtherDiscrete : IDiscreteComponent<OtherDiscrete>
        {
            public int Value;
        }

        private struct DenseLifecycle : IComponent<DenseLifecycle>
        {
            public int Value;

            public void OnDestroy(ulong entityId)
            {
                DestroyCount += 1;
                LastDestroyedValue = Value;
            }

            public static int DestroyCount;
            public static int LastDestroyedValue;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private struct Health : IComponent<Health>
        {
            public int Value;

            public void OnCreate(ulong entityId)
            {
                CreateCount += 1;
                LastCreatedEntity = entityId;
            }

            public void OnDestroy(ulong entityId)
            {
                DestroyCount += 1;
                LastDestroyedValue = Value;
                DestroyAction?.Invoke(entityId);
            }

            public static int CreateCount;
            public static int DestroyCount;
            public static ulong LastCreatedEntity;
            public static int LastDestroyedValue;
            public static Action<ulong> DestroyAction;
        }

        private sealed class RecordingObserver : IStructureObserver
        {
            public readonly List<(uint TypeId, int Row)> Added = new();
            public readonly List<(uint TypeId, int Row)> Removed = new();
            public readonly List<(uint TypeId, int Row)> Changed = new();

            public Structure LastAddedStructure;
            public Structure LastRemovedStructure;

            public void OnComponentAdded(Structure structure, int row, uint typeId)
            {
                Added.Add((typeId, row));
                LastAddedStructure = structure;
            }

            public void OnComponentRemoved(Structure structure, int row, uint typeId)
            {
                Removed.Add((typeId, row));
                LastRemovedStructure = structure;
            }

            public void OnComponentChanged(Structure structure, int row, uint typeId) => Changed.Add((typeId, row));
        }

        private StructureRegistry m_registry;
        private EntityTable m_table;
        private RecordingObserver m_observer;
        private ComponentOrchestrator m_orchestrator;

        [SetUp]
        public void SetUp()
        {
            ManaComponent.CreateCount = 0;
            ManaComponent.DestroyCount = 0;
            ManaComponent.LastDestroyedValue = 0;
            ManaComponent.CreateAction = null;
            ManaComponent.DestroyAction = null;
            DenseLifecycle.DestroyCount = 0;
            DenseLifecycle.LastDestroyedValue = 0;
            Health.CreateCount = 0;
            Health.DestroyCount = 0;
            Health.LastCreatedEntity = 0UL;
            Health.LastDestroyedValue = 0;
            Health.DestroyAction = null;
            m_registry = new StructureRegistry();
            m_table = new EntityTable();
            m_observer = new RecordingObserver();
            m_orchestrator = new ComponentOrchestrator(m_registry, m_table, m_observer);
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        [Test]
        public void CreateEntity_SelectsMaskStructureAndAppends()
        {
            var (entityId, location) = m_orchestrator.CreateEntity(0b101UL);

            var structure = m_registry.GetOrCreate(Array.Empty<uint>(), 0b101UL);
            Assert.AreEqual(1UL, entityId);
            Assert.AreSame(structure, location.Structure);
            Assert.AreEqual(0, location.Row);
            Assert.AreEqual(1, structure.Count);
            Assert.AreEqual(entityId, structure.Entities[0]);
            Assert.AreEqual(1, m_table.Count);

            var (secondId, secondLocation) = m_orchestrator.CreateEntity(0b010UL);
            Assert.AreEqual(2UL, secondId);
            Assert.AreNotSame(location.Structure, secondLocation.Structure);
            Assert.AreEqual(2, m_registry.Count);
        }

        [Test]
        public void DestroyEntity_ReleasesLocationAndEmptiesRow()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var structure = location.Structure;
            var generation = location.Generation;

            m_orchestrator.DestroyEntity(entityId);

            Assert.AreEqual(0, m_table.Count);
            Assert.IsFalse(m_table.TryGetLocation(entityId, out _));
            Assert.AreEqual(0, structure.Count);
            Assert.IsNull(location.Structure);
            Assert.AreEqual(generation + 1U, location.Generation);

            m_orchestrator.DestroyEntity(entityId + 100UL);
            Assert.AreEqual(0, m_table.Count);

            Assert.Throws<InvalidOperationException>(() => m_orchestrator.AddTagComponent<PlayerTag>(entityId));
        }

        [Test]
        public void AddDiscreteComponent_SetsValueRaisesObserverEventAndInvokesOnCreate()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var typeId = IdOf<ManaComponent>();

            var core = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 7 });

            Assert.AreEqual(1, ManaComponent.CreateCount);
            Assert.IsTrue(location.Structure.HasDiscrete(typeId, location.Row));
            Assert.AreEqual(7, location.Structure.GetDiscreteRef<ManaComponent>(location.Row).Value);
            Assert.AreEqual(1, m_observer.Added.Count);
            Assert.AreEqual((typeId, location.Row), m_observer.Added[0]);
            Assert.IsTrue(core.NotNull);
            Assert.AreEqual(entityId, core.EntityId);
            Assert.AreEqual(ComponentKind.Discrete, core.Kind);
            Assert.AreEqual(location.Structure.GetDiscreteVersion(typeId, location.Row), core.Version);
        }

        [Test]
        public void RemoveDiscreteComponent_InvokesOnDestroyAndRaisesObserverEvent()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var typeId = IdOf<ManaComponent>();
            var core = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 3 });

            m_orchestrator.RemoveDiscreteComponent<ManaComponent>(entityId);

            Assert.AreEqual(1, ManaComponent.DestroyCount);
            Assert.AreEqual(3, ManaComponent.LastDestroyedValue);
            Assert.IsFalse(location.Structure.HasDiscrete(typeId, location.Row));
            Assert.AreEqual(1, m_observer.Removed.Count);
            Assert.AreEqual((typeId, location.Row), m_observer.Removed[0]);
            Assert.IsFalse(core.NotNull);

            m_orchestrator.RemoveDiscreteComponent<ManaComponent>(entityId);
            Assert.AreEqual(1, ManaComponent.DestroyCount);
        }

        [Test]
        public void AddTagComponent_AndRemoveTagComponent_RoundTripWithObserverEvents()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var typeId = IdOf<PlayerTag>();

            var core = m_orchestrator.AddTagComponent<PlayerTag>(entityId);

            Assert.IsTrue(location.Structure.HasTag(typeId, location.Row));
            Assert.AreEqual(1, m_observer.Added.Count);
            Assert.AreEqual((typeId, location.Row), m_observer.Added[0]);
            Assert.IsTrue(core.NotNull);
            Assert.AreEqual(ComponentKind.Tag, core.Kind);

            m_orchestrator.RemoveTagComponent<PlayerTag>(entityId);

            Assert.IsFalse(location.Structure.HasTag(typeId, location.Row));
            Assert.AreEqual(1, m_observer.Removed.Count);
            Assert.AreEqual((typeId, location.Row), m_observer.Removed[0]);
            Assert.IsFalse(core.NotNull);

            Assert.DoesNotThrow(() => m_orchestrator.RemoveTagComponent<PlayerTag>(entityId));
            Assert.AreEqual(1, m_observer.Removed.Count);
        }

        [Test]
        public void HasComponent_ReportsDenseDiscreteAndTagPresence()
        {
            var denseStructure = m_registry.GetOrCreate(new[] { IdOf<Position>() }, 0b1UL);
            var (denseEntityId, denseLocation) = m_table.Create();
            denseStructure.Append(denseEntityId, denseLocation);
            denseStructure.SetDenseValue(denseLocation.Row, new Position { X = 1 }, ComponentVersion.Next());

            Assert.IsTrue(m_orchestrator.HasComponent<Position>(denseEntityId));
            Assert.IsFalse(m_orchestrator.HasComponent<ManaComponent>(denseEntityId));
            Assert.IsFalse(m_orchestrator.HasComponent<PlayerTag>(denseEntityId));

            var (entityId, _) = m_orchestrator.CreateEntity();
            Assert.IsFalse(m_orchestrator.HasComponent<Position>(entityId));

            m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 1 });
            m_orchestrator.AddTagComponent<PlayerTag>(entityId);

            Assert.IsTrue(m_orchestrator.HasComponent<ManaComponent>(entityId));
            Assert.IsTrue(m_orchestrator.HasComponent<PlayerTag>(entityId));
            Assert.IsFalse(m_orchestrator.HasComponent<Position>(entityId));
            Assert.IsFalse(m_orchestrator.HasComponent<Position>(entityId + 100UL));
        }

        [Test]
        public void GetComponentRef_ReturnsVersionedCoreForDiscreteAndPresenceCoreForTag()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();

            Assert.IsNull(m_orchestrator.GetComponentRef<ManaComponent>(entityId));
            Assert.IsNull(m_orchestrator.GetComponentRef<PlayerTag>(entityId));
            Assert.IsNull(m_orchestrator.GetComponentRef<ManaComponent>(entityId + 100UL));

            var discrete = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 5 });
            Assert.IsNotNull(discrete);
            Assert.IsTrue(discrete.NotNull);
            Assert.AreEqual(entityId, discrete.EntityId);
            Assert.AreEqual(ComponentKind.Discrete, discrete.Kind);
            Assert.AreEqual(location.Structure.GetDiscreteVersion(IdOf<ManaComponent>(), location.Row), discrete.Version);
            Assert.AreEqual(0u, discrete.Revision);

            var tag = m_orchestrator.AddTagComponent<PlayerTag>(entityId);
            Assert.IsNotNull(tag);
            Assert.IsTrue(tag.NotNull);
            Assert.AreEqual(entityId, tag.EntityId);
            Assert.AreEqual(ComponentKind.Tag, tag.Kind);
            Assert.AreEqual(0u, tag.Version);

            m_orchestrator.RemoveTagComponent<PlayerTag>(entityId);
            Assert.IsFalse(tag.NotNull);

            m_orchestrator.RemoveDiscreteComponent<ManaComponent>(entityId);
            Assert.IsFalse(discrete.NotNull);
        }

        [Test]
        public void DestroyEntity_WithDiscreteComponent_InvokesOnDestroyAndPreservesOtherEntity()
        {
            var (firstId, firstLocation) = m_orchestrator.CreateEntity();
            var (secondId, _) = m_orchestrator.CreateEntity();
            m_orchestrator.AddDiscreteComponent(firstId, new ManaComponent { Value = 1 });
            m_orchestrator.AddDiscreteComponent(secondId, new ManaComponent { Value = 2 });
            var generation = firstLocation.Generation;

            m_orchestrator.DestroyEntity(firstId);

            Assert.AreEqual(1, m_table.Count);
            Assert.IsFalse(m_table.TryGetLocation(firstId, out _));
            Assert.AreEqual(1, ManaComponent.DestroyCount);
            Assert.AreEqual(1, ManaComponent.LastDestroyedValue);
            Assert.AreEqual(generation + 1U, firstLocation.Generation);
            Assert.IsNull(firstLocation.Structure);

            Assert.IsTrue(m_table.TryGetLocation(secondId, out var moved));
            Assert.AreEqual(0, moved.Row);
            Assert.IsTrue(m_orchestrator.HasComponent<ManaComponent>(secondId));
            Assert.AreEqual(2, moved.Structure.GetDiscreteRef<ManaComponent>(moved.Row).Value);
        }

        [Test]
        public void GetComponentRef_Dense_ReturnsVersionedCore()
        {
            var structure = m_registry.GetOrCreate(new[] { IdOf<Position>() }, 0UL);
            var (entityId, location) = m_table.Create();
            structure.Append(entityId, location);
            structure.SetDenseValue(location.Row, new Position { X = 3 }, 7);

            var core = m_orchestrator.GetComponentRef<Position>(entityId);

            Assert.IsNotNull(core);
            Assert.IsTrue(core.NotNull);
            Assert.AreEqual(ComponentKind.Dense, core.Kind);
            Assert.AreEqual(7u, core.Version);
            Assert.AreEqual(entityId, core.EntityId);
        }

        [Test]
        public void DestroyEntity_WithDenseComponent_InvokesOnDestroyBeforeRemoval()
        {
            ComponentHookDispatcher.RegisterDense<DenseLifecycle>();
            var structure = m_registry.GetOrCreate(new[] { IdOf<DenseLifecycle>() }, 0UL);
            var (entityId, location) = m_table.Create();
            structure.Append(entityId, location);
            structure.SetDenseValue(location.Row, new DenseLifecycle { Value = 42 }, ComponentVersion.Next());

            m_orchestrator.DestroyEntity(entityId);

            Assert.AreEqual(1, DenseLifecycle.DestroyCount);
            Assert.AreEqual(42, DenseLifecycle.LastDestroyedValue);
            Assert.AreEqual(0, structure.Count);
            Assert.AreEqual(0, m_table.Count);
        }

        [Test]
        public void DestroyEntity_HookDestroysSameEntity_IsNoOpAndCompletesOnce()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 4 });
            var reentrantCalls = 0;
            ManaComponent.DestroyAction = id =>
            {
                reentrantCalls += 1;
                m_orchestrator.DestroyEntity(id);
            };

            Assert.DoesNotThrow(() => m_orchestrator.DestroyEntity(entityId));

            Assert.AreEqual(1, reentrantCalls);
            Assert.AreEqual(1, ManaComponent.DestroyCount);
            Assert.AreEqual(0, m_table.Count);
            Assert.IsNull(location.Structure);
        }

        [Test]
        public void DestroyEntity_HookDestroysEarlierEntityInSameStructure_CompletesBoth()
        {
            var (firstId, firstLocation) = m_orchestrator.CreateEntity();
            var (secondId, secondLocation) = m_orchestrator.CreateEntity();
            var structure = firstLocation.Structure;
            Assert.AreSame(structure, secondLocation.Structure);
            m_orchestrator.AddDiscreteComponent(firstId, new ManaComponent { Value = 1 });
            m_orchestrator.AddDiscreteComponent(secondId, new ManaComponent { Value = 2 });
            ManaComponent.DestroyAction = id =>
            {
                if (id == secondId) m_orchestrator.DestroyEntity(firstId);
            };

            Assert.DoesNotThrow(() => m_orchestrator.DestroyEntity(secondId));

            Assert.AreEqual(2, ManaComponent.DestroyCount);
            Assert.AreEqual(0, m_table.Count);
            Assert.AreEqual(0, structure.Count);
            Assert.IsNull(firstLocation.Structure);
            Assert.IsNull(secondLocation.Structure);
        }

        [Test]
        public void DestroyEntity_HookAddsNewDiscreteStore_CompletesWithoutEnumerationError()
        {
            var (firstId, firstLocation) = m_orchestrator.CreateEntity();
            var (secondId, _) = m_orchestrator.CreateEntity();
            var structure = firstLocation.Structure;
            m_orchestrator.AddDiscreteComponent(firstId, new ManaComponent { Value = 1 });
            ManaComponent.DestroyAction = id =>
            {
                if (id == firstId)
                {
                    m_orchestrator.AddDiscreteComponent(secondId, new OtherDiscrete { Value = 9 });
                }
            };

            Assert.DoesNotThrow(() => m_orchestrator.DestroyEntity(firstId));

            Assert.AreEqual(1, m_table.Count);
            Assert.IsTrue(m_table.TryGetLocation(secondId, out var moved));
            Assert.AreEqual(0, moved.Row);
            Assert.IsTrue(structure.HasDiscrete(IdOf<OtherDiscrete>(), moved.Row));
            Assert.AreEqual(9, structure.GetDiscreteRef<OtherDiscrete>(moved.Row).Value);
        }

        [Test]
        public void DestroyEntity_HookThrows_LogsAndCompletesDestroy()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 1 });
            ManaComponent.DestroyAction = _ => throw new InvalidOperationException("boom");

            Assert.DoesNotThrow(() => m_orchestrator.DestroyEntity(entityId));

            Assert.AreEqual(1, ManaComponent.DestroyCount);
            Assert.AreEqual(0, m_table.Count);
            Assert.IsNull(location.Structure);
        }

        [Test]
        public void AddDiscreteComponent_OnCreateThrows_LogsAndKeepsComponent()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            ManaComponent.CreateAction = _ => throw new InvalidOperationException("boom");

            var core = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 2 });

            Assert.IsTrue(core.NotNull);
            Assert.AreEqual(1, ManaComponent.CreateCount);
            Assert.IsTrue(location.Structure.HasDiscrete(IdOf<ManaComponent>(), location.Row));
        }

        [Test]
        public void AddDenseComponent_MigratesEntityAndPreservesDiscreteAndTagState()
        {
            var (entityId, location) = m_orchestrator.CreateEntity(0b10UL);
            var source = location.Structure;
            var mana = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 9 });
            var tag = m_orchestrator.AddTagComponent<PlayerTag>(entityId);

            var core = m_orchestrator.AddDenseComponent(entityId, new Health { Value = 55 });

            var target = location.Structure;
            Assert.AreNotSame(source, target);
            Assert.AreEqual(0b10UL, target.Mask);
            Assert.AreEqual(0b10UL, target.Key.Mask);
            Assert.AreEqual(1, target.Count);
            Assert.AreEqual(0, source.Count);
            Assert.AreEqual(0, location.Row);
            Assert.IsTrue(target.HasDense(IdOf<Health>()));
            Assert.AreEqual(55, target.GetDenseRef<Health>(location.Row).Value);
            Assert.AreEqual(core.Version, target.GetDenseVersion(IdOf<Health>(), location.Row));
            Assert.AreNotEqual(0u, core.Version);

            Assert.IsTrue(target.HasDiscrete(IdOf<ManaComponent>(), location.Row));
            Assert.AreEqual(9, target.GetDiscreteRef<ManaComponent>(location.Row).Value);
            Assert.AreEqual(mana.Version, target.GetDiscreteVersion(IdOf<ManaComponent>(), location.Row));
            Assert.IsTrue(target.HasTag(IdOf<PlayerTag>(), location.Row));

            Assert.IsTrue(core.NotNull);
            Assert.AreEqual(entityId, core.EntityId);
            Assert.AreEqual(ComponentKind.Dense, core.Kind);
            Assert.IsTrue(mana.NotNull);
            Assert.IsTrue(tag.NotNull);
        }

        [Test]
        public void AddDenseComponent_EmitsAddEventWithTargetRowAndInvokesOnCreate()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            m_observer.Added.Clear();

            var core = m_orchestrator.AddDenseComponent(entityId, new Health { Value = 7 });

            Assert.AreEqual(1, m_observer.Added.Count);
            Assert.AreEqual((IdOf<Health>(), location.Row), m_observer.Added[0]);
            Assert.AreSame(location.Structure, m_observer.LastAddedStructure);
            Assert.AreEqual(1, Health.CreateCount);
            Assert.AreEqual(entityId, Health.LastCreatedEntity);
            Assert.IsTrue(core.NotNull);
        }

        [Test]
        public void ComponentRefCore_CapturedBeforeAddDense_RemainsNotNullAfterMigration()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var position = m_orchestrator.AddDenseComponent(entityId, new Position { X = 3 });
            var mana = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 5 });
            var tag = m_orchestrator.AddTagComponent<PlayerTag>(entityId);
            var source = location.Structure;

            m_orchestrator.AddDenseComponent(entityId, new Health { Value = 1 });

            Assert.AreNotSame(source, location.Structure);
            Assert.AreSame(location, position.Location);

            Assert.IsTrue(position.NotNull);
            Assert.AreEqual(entityId, position.EntityId);
            Assert.AreEqual(3, location.Structure.GetDenseRef<Position>(location.Row).X);
            Assert.AreEqual(position.Version, location.Structure.GetDenseVersion(IdOf<Position>(), location.Row));

            Assert.IsTrue(mana.NotNull);
            Assert.AreEqual(entityId, mana.EntityId);
            Assert.AreEqual(5, location.Structure.GetDiscreteRef<ManaComponent>(location.Row).Value);

            Assert.IsTrue(tag.NotNull);
            Assert.AreEqual(entityId, tag.EntityId);
        }

        [Test]
        public void RemoveDenseComponent_DropsTypeAndPreservesOtherDenseDiscreteAndTag()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var position = m_orchestrator.AddDenseComponent(entityId, new Position { X = 8 });
            var positionStructure = location.Structure;
            var mana = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 5 });
            var tag = m_orchestrator.AddTagComponent<PlayerTag>(entityId);
            m_orchestrator.AddDenseComponent(entityId, new Health { Value = 3 });
            var healthStructure = location.Structure;
            m_observer.Removed.Clear();

            m_orchestrator.RemoveDenseComponent<Health>(entityId);

            var target = location.Structure;
            Assert.AreSame(positionStructure, target);
            Assert.AreNotSame(healthStructure, target);
            Assert.AreEqual(0, healthStructure.Count);
            Assert.AreEqual(1, target.Count);
            Assert.IsFalse(target.HasDense(IdOf<Health>()));
            Assert.IsTrue(target.HasDense(IdOf<Position>()));
            Assert.AreEqual(8, target.GetDenseRef<Position>(location.Row).X);
            Assert.IsTrue(position.NotNull);
            Assert.AreEqual(position.Version, target.GetDenseVersion(IdOf<Position>(), location.Row));
            Assert.IsTrue(mana.NotNull);
            Assert.IsTrue(target.HasDiscrete(IdOf<ManaComponent>(), location.Row));
            Assert.AreEqual(5, target.GetDiscreteRef<ManaComponent>(location.Row).Value);
            Assert.IsTrue(tag.NotNull);
            Assert.IsTrue(target.HasTag(IdOf<PlayerTag>(), location.Row));

            Assert.AreEqual(1, m_observer.Removed.Count);
            Assert.AreEqual((IdOf<Health>(), location.Row), m_observer.Removed[0]);
            Assert.AreSame(target, m_observer.LastRemovedStructure);
        }

        [Test]
        public void RemoveDenseComponent_InvokesOnDestroyWhileOldValueStillReadable()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            m_orchestrator.AddDenseComponent(entityId, new Health { Value = 42 });

            m_orchestrator.RemoveDenseComponent<Health>(entityId);

            Assert.AreEqual(1, Health.DestroyCount);
            Assert.AreEqual(42, Health.LastDestroyedValue);
            Assert.IsFalse(location.Structure.HasDense(IdOf<Health>()));
            Assert.IsFalse(m_orchestrator.HasComponent<Health>(entityId));
        }

        [Test]
        public void AddDenseComponent_AlreadyPresentAndRemoveDenseComponent_Absent_Throw()
        {
            var structure = m_registry.GetOrCreate(new[] { IdOf<Position>() }, 0UL);
            var (entityId, location) = m_table.Create();
            structure.Append(entityId, location);
            structure.SetDenseValue(location.Row, new Position { X = 1 }, ComponentVersion.Next());

            Assert.Throws<InvalidOperationException>(() =>
            {
                m_orchestrator.AddDenseComponent(entityId, new Position { X = 2 });
            });
            Assert.IsTrue(structure.HasDense(IdOf<Position>()));
            Assert.AreEqual(1, structure.Count);

            var (otherId, otherLocation) = m_orchestrator.CreateEntity();
            Assert.Throws<InvalidOperationException>(() =>
            {
                m_orchestrator.RemoveDenseComponent<Position>(otherId);
            });
            Assert.IsFalse(otherLocation.Structure.HasDense(IdOf<Position>()));
        }

        [Test]
        public void SequentialAddDenseComponent_BuildsSortedCompositionAndPreservesMask()
        {
            var positionId = IdOf<Position>();
            var healthId = IdOf<Health>();
            var lowId = Math.Min(positionId, healthId);
            var highId = Math.Max(positionId, healthId);

            var (entityId, location) = m_orchestrator.CreateEntity(0b100UL);

            if (positionId == highId)
            {
                m_orchestrator.AddDenseComponent(entityId, new Position { X = 1 });
                m_orchestrator.AddDenseComponent(entityId, new Health { Value = 2 });
            }
            else
            {
                m_orchestrator.AddDenseComponent(entityId, new Health { Value = 2 });
                m_orchestrator.AddDenseComponent(entityId, new Position { X = 1 });
            }

            var target = location.Structure;
            Assert.AreEqual(2, target.Key.DenseCount);
            Assert.AreEqual(lowId, target.Key.DenseTypeIds[0]);
            Assert.AreEqual(highId, target.Key.DenseTypeIds[1]);
            Assert.AreEqual(0b100UL, target.Key.Mask);
            Assert.AreSame(target, m_registry.GetOrCreate(new[] { lowId, highId }, 0b100UL));
        }

        [Test]
        public void DestroyEntity_HookMutatesDyingEntity_ThrowsAndDestroyCompletes()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var structure = location.Structure;
            m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 1 });
            var mutationRejected = false;
            ManaComponent.DestroyAction = id =>
            {
                try
                {
                    m_orchestrator.AddDenseComponent(id, new Health { Value = 7 });
                }
                catch (InvalidOperationException)
                {
                    mutationRejected = true;
                }
            };

            Assert.DoesNotThrow(() => m_orchestrator.DestroyEntity(entityId));

            Assert.IsTrue(mutationRejected);
            Assert.AreEqual(1, ManaComponent.DestroyCount);
            Assert.AreEqual(0, m_table.Count);
            Assert.AreEqual(0, structure.Count);
            Assert.AreEqual(1, m_registry.Count);
        }

        [Test]
        public void RemoveDenseComponent_HookDestroysSameEntity_IsRejectedAndRemovalCompletes()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            m_orchestrator.AddDenseComponent(entityId, new Health { Value = 5 });
            var destroyRejected = false;
            Health.DestroyAction = id =>
            {
                try
                {
                    m_orchestrator.DestroyEntity(id);
                }
                catch (InvalidOperationException)
                {
                    destroyRejected = true;
                }
            };

            m_orchestrator.RemoveDenseComponent<Health>(entityId);

            Assert.IsTrue(destroyRejected);
            Assert.AreEqual(1, Health.DestroyCount);
            Assert.AreEqual(1, m_table.Count);
            Assert.IsTrue(m_table.TryGetLocation(entityId, out var current));
            Assert.IsNotNull(current.Structure);
            Assert.IsFalse(m_orchestrator.HasComponent<Health>(entityId));
        }

        [Test]
        public void RemoveDenseComponent_HookRemovesSameComponent_IsRejectedAndRemovalCompletesOnce()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            m_orchestrator.AddDenseComponent(entityId, new Health { Value = 5 });
            var recursiveRejected = false;
            Health.DestroyAction = id =>
            {
                try
                {
                    m_orchestrator.RemoveDenseComponent<Health>(id);
                }
                catch (InvalidOperationException)
                {
                    recursiveRejected = true;
                }
            };

            m_orchestrator.RemoveDenseComponent<Health>(entityId);

            Assert.IsTrue(recursiveRejected);
            Assert.AreEqual(1, Health.DestroyCount);
            Assert.AreEqual(1, m_table.Count);
            Assert.IsFalse(m_orchestrator.HasComponent<Health>(entityId));
        }

        [Test]
        public void RemoveDenseComponent_HookMigratesEntity_IsRejectedAndRemovalCompletes()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            m_orchestrator.AddDenseComponent(entityId, new Health { Value = 5 });
            var migrationRejected = false;
            Health.DestroyAction = id =>
            {
                try
                {
                    m_orchestrator.AddDenseComponent(id, new Position { X = 1 });
                }
                catch (InvalidOperationException)
                {
                    migrationRejected = true;
                }
            };

            m_orchestrator.RemoveDenseComponent<Health>(entityId);

            Assert.IsTrue(migrationRejected);
            Assert.AreEqual(1, m_table.Count);
            Assert.IsFalse(m_orchestrator.HasComponent<Health>(entityId));
            Assert.IsFalse(m_orchestrator.HasComponent<Position>(entityId));
        }

        [Test]
        public void RemoveDiscreteComponent_HookDestroysSameEntity_IsRejectedAndRemovalCompletes()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 3 });
            var destroyRejected = false;
            ManaComponent.DestroyAction = id =>
            {
                try
                {
                    m_orchestrator.DestroyEntity(id);
                }
                catch (InvalidOperationException)
                {
                    destroyRejected = true;
                }
            };

            m_orchestrator.RemoveDiscreteComponent<ManaComponent>(entityId);

            Assert.IsTrue(destroyRejected);
            Assert.AreEqual(1, m_table.Count);
            Assert.IsFalse(m_orchestrator.HasComponent<ManaComponent>(entityId));
        }
    }
}
