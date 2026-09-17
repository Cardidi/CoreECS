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

            public void OnCreate(ulong entityId) => CreateCount += 1;

            public void OnDestroy(ulong entityId) => DestroyCount += 1;

            public static int CreateCount;
            public static int DestroyCount;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private sealed class RecordingObserver : IStructureObserver
        {
            public readonly List<(uint TypeId, int Row)> Added = new();
            public readonly List<(uint TypeId, int Row)> Removed = new();
            public readonly List<(uint TypeId, int Row)> Changed = new();

            public void OnComponentAdded(Structure structure, int row, uint typeId) => Added.Add((typeId, row));

            public void OnComponentRemoved(Structure structure, int row, uint typeId) => Removed.Add((typeId, row));

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
            Assert.AreEqual(generation + 1U, firstLocation.Generation);
            Assert.IsNull(firstLocation.Structure);

            Assert.IsTrue(m_table.TryGetLocation(secondId, out var moved));
            Assert.AreEqual(0, moved.Row);
            Assert.IsTrue(m_orchestrator.HasComponent<ManaComponent>(secondId));
            Assert.AreEqual(2, moved.Structure.GetDiscreteRef<ManaComponent>(moved.Row).Value);
        }
    }
}
