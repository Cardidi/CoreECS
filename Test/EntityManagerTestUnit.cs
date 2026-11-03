using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityManagerTestUnit
    {
        private World m_world;
        private ComponentManager m_components;
        private EntityManager m_entities;

        [SetUp]
        public void SetUp()
        {
            m_world = new World();
            m_world.Startup();
            m_components = m_world.GetManager<ComponentManager>();
            m_entities = m_world.GetManager<EntityManager>();
            OwnedA.DestroyCount = 0;
            OwnedB.DestroyCount = 0;
            Unrelated.DestroyCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            m_world?.Shutdown();
        }

        [Test]
        public void CreateGetAndIsAlive_AreBackedByEntityTable()
        {
            EntityId entityId = m_entities.Create(0x42);

            Assert.That(m_entities.IsAlive(entityId), Is.True);
            Assert.That(m_entities.GetMask(entityId), Is.EqualTo(0x42));
        }

        [Test]
        public void Destroy_DefersIndexReuseUntilCommitAndAdvancesGeneration()
        {
            EntityId destroyed = m_entities.Create(0);

            m_entities.Destroy(destroyed);
            EntityId beforeCommit = m_entities.Create(0);

            Assert.That(m_entities.IsAlive(destroyed), Is.False);
            Assert.That(beforeCommit.Index, Is.Not.EqualTo(destroyed.Index));

            m_entities.CommitReleases();
            EntityId reused = m_entities.Create(0);

            Assert.That(reused.Index, Is.EqualTo(destroyed.Index));
            Assert.That(reused.Generation, Is.Not.EqualTo(destroyed.Generation));
            Assert.That(m_entities.IsAlive(destroyed), Is.False);
            Assert.That(m_entities.IsAlive(reused), Is.True);
        }

        [Test]
        public void Destroy_TraversesOnlyOwnedIntrusiveHandlerChain()
        {
            EntityId owner = m_entities.Create(0);
            EntityId other = m_entities.Create(0);
            ComponentRef<OwnedA> ownedA = m_components.Create<OwnedA>(owner);
            ComponentRef<OwnedB> ownedB = m_components.Create<OwnedB>(owner);
            ComponentRef<Unrelated> unrelated = m_components.Create<Unrelated>(other);
            var ownedAStorage = m_components.GetStorage<OwnedA>(false);
            var ownedBStorage = m_components.GetStorage<OwnedB>(false);
            var unrelatedStorage = m_components.GetStorage<Unrelated>(false);

            m_entities.Destroy(owner);

            Assert.That(ownedA.NotNull, Is.False);
            Assert.That(ownedB.NotNull, Is.False);
            Assert.That(unrelated.NotNull, Is.True);
            Assert.That(OwnedA.DestroyCount, Is.EqualTo(1));
            Assert.That(OwnedB.DestroyCount, Is.EqualTo(1));
            Assert.That(Unrelated.DestroyCount, Is.Zero);
            Assert.That(ownedAStorage.MarkRemoveCallCountForTesting, Is.EqualTo(1));
            Assert.That(ownedBStorage.MarkRemoveCallCountForTesting, Is.EqualTo(1));
            Assert.That(unrelatedStorage.MarkRemoveCallCountForTesting, Is.Zero,
                "Destroy must not visit an unrelated component storage.");
        }

        [Test]
        public void DestroyingSameEntityRepeatedly_IsSafeNoOp()
        {
            EntityId entityId = m_entities.Create(0);

            Assert.That(m_entities.Destroy(entityId), Is.True);
            Assert.That(m_entities.Destroy(entityId), Is.False);
            Assert.That(m_entities.Destroy(default(EntityId)), Is.False);
        }

        private struct OwnedA : IComponent<OwnedA>
        {
            internal static int DestroyCount;
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) => DestroyCount++;
        }

        private struct OwnedB : IComponent<OwnedB>
        {
            internal static int DestroyCount;
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) => DestroyCount++;
        }

        private struct Unrelated : IComponent<Unrelated>
        {
            internal static int DestroyCount;
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) => DestroyCount++;
        }
    }
}
