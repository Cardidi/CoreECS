using System;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentManagerTestUnit
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
            Lifecycle.Reset();
            HookOrder.Reset();
            HookOrder.Manager = m_components;
            CapturingHook.Captured = default(EntityId);
            SilentTag.SameNamedCreateCalls = 0;
            SilentTag.SameNamedDestroyCalls = 0;
        }

        [TearDown]
        public void TearDown()
        {
            HookOrder.Reset();
            m_world?.Shutdown();
        }

        [Test]
        public void Create_RejectsDuplicateComponentTypePerEntity()
        {
            EntityId entityId = m_entities.Create(0);
            m_components.Create<Position>(entityId);

            Assert.Throws<InvalidOperationException>(() => m_components.Create<Position>(entityId));
            Assert.That(m_components.Has<Position>(entityId), Is.True);
        }

        [Test]
        public void Hooks_RunAgainstPublishedPayloadBeforeLogicalRemoval()
        {
            EntityId entityId = m_entities.Create(0);
            HookOrder.EntityId = entityId;

            ComponentRef<HookOrder> component = m_components.Create(entityId, new HookOrder { Value = 4 });

            Assert.That(HookOrder.CreateSawMembership, Is.True);
            Assert.That(component.RO.Value, Is.EqualTo(5));

            m_components.Destroy(component);

            Assert.That(HookOrder.DestroySawMembership, Is.True);
            Assert.That(HookOrder.DestroySawValue, Is.EqualTo(5));
            Assert.That(m_components.Has<HookOrder>(entityId), Is.False);
            Assert.That(component.NotNull, Is.False);
        }

        [Test]
        public void Destroy_IsLogicalImmediatelyAndPhysicalOnlyAfterCompact()
        {
            EntityId first = m_entities.Create(0);
            EntityId removed = m_entities.Create(0);
            EntityId third = m_entities.Create(0);
            m_components.Create(first, new Position { X = 1 });
            ComponentRef<Position> removedRef = m_components.Create(removed, new Position { X = 2 });
            m_components.Create(third, new Position { X = 3 });
            ComponentStorage<Position> storage = m_components.GetStorage<Position>(false);

            m_components.Destroy(removedRef);

            Assert.That(storage.LiveCount, Is.EqualTo(2));
            Assert.That(storage.AllocatedCount, Is.EqualTo(3));
            Assert.That(m_components.Has<Position>(removed), Is.False);
            Assert.That(removedRef.NotNull, Is.False);

            m_components.CompactDirtyStores();

            Assert.That(storage.LiveCount, Is.EqualTo(2));
            Assert.That(storage.AllocatedCount, Is.EqualTo(2));
        }

        [Test]
        public void DataHooks_RunOnceAndTagHooksAreSkipped()
        {
            EntityId entityId = m_entities.Create(0);

            ComponentRef<Lifecycle> data = m_components.Create<Lifecycle>(entityId);
            ComponentRef<SilentTag> tag = m_components.Create<SilentTag>(entityId);
            m_components.Destroy(data);
            m_components.Destroy(tag);

            Assert.That(Lifecycle.Created, Is.EqualTo(1));
            Assert.That(Lifecycle.Destroyed, Is.EqualTo(1));
            Assert.That(SilentTag.SameNamedCreateCalls, Is.Zero);
            Assert.That(SilentTag.SameNamedDestroyCalls, Is.Zero);
        }

        [Test]
        public void LegacyPublicConstructorsRemainAvailableForDependencyInjection()
        {
            Assert.That(typeof(ComponentManager).GetConstructor(Type.EmptyTypes), Is.Not.Null);
            Assert.That(typeof(EntityManager).GetConstructor(new[] { typeof(IWorld), typeof(ComponentManager) }), Is.Not.Null);
        }

        [Test]
        public void HookReceivesFullNativeEntityIdAfterIndexReuse()
        {
            EntityId first = m_entities.Create(0);
            m_entities.Destroy(first);
            m_entities.CommitReleases();
            EntityId reused = m_entities.Create(0);

            m_components.Create<CapturingHook>(reused);

            Assert.That(reused.Index, Is.EqualTo(first.Index));
            Assert.That(reused.Generation, Is.Not.EqualTo(first.Generation));
            Assert.That(CapturingHook.Captured, Is.EqualTo(reused));
        }

        private struct Position : IComponent<Position>
        {
            internal int X;
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct HookOrder : IComponent<HookOrder>
        {
            internal static ComponentManager Manager;
            internal static EntityId EntityId;
            internal static bool CreateSawMembership;
            internal static bool DestroySawMembership;
            internal static int DestroySawValue;

            internal int Value;

            public void OnCreate(EntityId entityId)
            {
                CreateSawMembership = Manager.Has<HookOrder>(entityId);
                Value++;
            }

            public void OnDestroy(EntityId entityId)
            {
                DestroySawMembership = Manager.Has<HookOrder>(entityId);
                DestroySawValue = Value;
            }

            internal static void Reset()
            {
                Manager = null;
                EntityId = default(EntityId);
                CreateSawMembership = false;
                DestroySawMembership = false;
                DestroySawValue = 0;
            }
        }

        private struct Lifecycle : IComponent<Lifecycle>
        {
            internal static int Created;
            internal static int Destroyed;

            public void OnCreate(EntityId entityId) => Created++;
            public void OnDestroy(EntityId entityId) => Destroyed++;

            internal static void Reset()
            {
                Created = 0;
                Destroyed = 0;
            }
        }

        private struct CapturingHook : IComponent<CapturingHook>
        {
            internal static EntityId Captured;

            public void OnCreate(EntityId entityId) => Captured = entityId;
            public void OnDestroy(EntityId entityId) { }
        }

        private struct SilentTag : ITagComponent<SilentTag>
        {
            internal static int SameNamedCreateCalls;
            internal static int SameNamedDestroyCalls;

            public void OnCreate(EntityId entityId) => SameNamedCreateCalls++;
            public void OnDestroy(EntityId entityId) => SameNamedDestroyCalls++;
        }
    }
}
