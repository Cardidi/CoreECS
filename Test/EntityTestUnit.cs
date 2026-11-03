using System.Reflection;
using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityTestUnit
    {
        private World m_world;

        [SetUp]
        public void SetUp()
        {
            m_world = new World();
            m_world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            m_world?.Shutdown();
        }

        [Test]
        public void Identity_ContainsOnlyWorldAndFullEntityId()
        {
            FieldInfo[] fields = typeof(Entity).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(fields.Select(field => field.FieldType),
                Is.EquivalentTo(new[] { typeof(World), typeof(EntityId) }));
        }

        [Test]
        public void Equality_IsWorldLocalAndIncludesEntityGeneration()
        {
            var otherWorld = new World();
            otherWorld.Startup();
            try
            {
                Entity first = m_world.CreateEntity();
                Entity same = m_world.GetEntity(first.EntityId);
                Entity foreign = otherWorld.CreateEntity();

                Assert.That(same, Is.EqualTo(first));
                Assert.That(same.GetHashCode(), Is.EqualTo(first.GetHashCode()));
                Assert.That(foreign.EntityId, Is.EqualTo(first.EntityId));
                Assert.That(foreign, Is.Not.EqualTo(first));

                m_world.DestroyEntity(first);
                EndTick(m_world);
                Entity reused = m_world.CreateEntity();
                Assert.That(reused.EntityId.Index, Is.EqualTo(first.EntityId.Index));
                Assert.That(reused.EntityId.Generation, Is.Not.EqualTo(first.EntityId.Generation));
                Assert.That(reused, Is.Not.EqualTo(first));
            }
            finally
            {
                otherWorld.Shutdown();
            }
        }

        [Test]
        public void Mask_IsCreationMetadataWithoutASetter()
        {
            Entity entity = m_world.CreateEntity(0x42);

            Assert.That(entity.Mask, Is.EqualTo(0x42));
            Assert.That(typeof(Entity).GetProperty(nameof(Entity.Mask))!.CanWrite, Is.False);
        }

        [Test]
        public void ComponentOperations_OnDestroyedEntity_Throw()
        {
            Entity entity = m_world.CreateEntity();
            m_world.DestroyEntity(entity);
            var output = new List<ComponentRef>();

            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidOperationException>(() => _ = entity.Mask);
                Assert.Throws<InvalidOperationException>(() => entity.CreateComponent<Position>());
                Assert.Throws<InvalidOperationException>(() => entity.CreateComponent(new Position()));
                Assert.Throws<InvalidOperationException>(() => entity.GetComponent<Position>());
                Assert.Throws<InvalidOperationException>(() => entity.TryGetComponent<Position>(out _));
                Assert.Throws<InvalidOperationException>(() => entity.GetOrCreateComponent<Position>(out _));
                Assert.Throws<InvalidOperationException>(() => entity.HasComponent<Position>());
                Assert.Throws<InvalidOperationException>(() => entity.DestroyComponent<Position>());
                Assert.Throws<InvalidOperationException>(() => entity.GetComponents(output));
            });
        }

        [Test]
        public void CreateComponent_DuplicateTypeThrowsAndPreservesOriginal()
        {
            Entity entity = m_world.CreateEntity();
            ComponentRef<Position> original = entity.CreateComponent(new Position { X = 7 });

            Assert.Throws<InvalidOperationException>(() => entity.CreateComponent(new Position { X = 99 }));
            Assert.That(entity.GetComponent<Position>(), Is.EqualTo(original));
            Assert.That(original.RO.X, Is.EqualTo(7));
        }

        [Test]
        public void GetTryGetHasAndDestroy_UseUniqueComponentOwnership()
        {
            Entity entity = m_world.CreateEntity();

            Assert.That(entity.HasComponent<Position>(), Is.False);
            Assert.That(entity.GetComponent<Position>().NotNull, Is.False);
            Assert.That(entity.TryGetComponent<Position>(out ComponentRef<Position> missing), Is.False);
            Assert.That(missing.NotNull, Is.False);
            Assert.Throws<InvalidOperationException>(() => entity.DestroyComponent<Position>());

            ComponentRef<Position> created = entity.CreateComponent(new Position { X = 3 });
            Assert.That(entity.HasComponent<Position>(), Is.True);
            Assert.That(entity.TryGetComponent<Position>(out ComponentRef<Position> found), Is.True);
            Assert.That(found, Is.EqualTo(created));

            entity.DestroyComponent<Position>();
            Assert.That(created.NotNull, Is.False);
            Assert.That(entity.HasComponent<Position>(), Is.False);
        }

        [Test]
        public void DestroyComponent_RejectsForeignEntityAndForeignWorldReferences()
        {
            Entity owner = m_world.CreateEntity();
            Entity other = m_world.CreateEntity();
            ComponentRef<Position> owned = owner.CreateComponent<Position>();

            Assert.Throws<InvalidOperationException>(() => other.DestroyComponent(owned));
            Assert.That(owned.NotNull, Is.True);

            var otherWorld = new World();
            otherWorld.Startup();
            try
            {
                Entity sameNativeIdentity = otherWorld.CreateEntity();
                Assert.That(sameNativeIdentity.EntityId, Is.EqualTo(owner.EntityId));
                Assert.Throws<InvalidOperationException>(() => sameNativeIdentity.DestroyComponent(owned));
                Assert.That(owned.NotNull, Is.True);
            }
            finally
            {
                otherWorld.Shutdown();
            }
        }

        [Test]
        public void GetOrCreateComponent_ReturnsWhetherComponentPreviouslyExisted()
        {
            Entity entity = m_world.CreateEntity();

            bool existedOnCreate = entity.GetOrCreateComponent(out ComponentRef<Position> created,
                new Position { X = 11 });
            bool existedOnGet = entity.GetOrCreateComponent(out ComponentRef<Position> found,
                new Position { X = 99 });

            Assert.That(existedOnCreate, Is.False);
            Assert.That(existedOnGet, Is.True);
            Assert.That(found, Is.EqualTo(created));
            Assert.That(found.RO.X, Is.EqualTo(11));
        }

        [Test]
        public void GetComponents_AppendsUntypedReferencesAndReturnsAddedCount()
        {
            Entity entity = m_world.CreateEntity();
            entity.CreateComponent<Position>();
            entity.CreateComponent<Velocity>();
            var output = new List<ComponentRef> { default };

            int added = entity.GetComponents(output);

            Assert.That(added, Is.EqualTo(2));
            Assert.That(output, Has.Count.EqualTo(3));
            Assert.That(output.Skip(1).Any(component => component.Inspect<Position>()), Is.True);
            Assert.That(output.Skip(1).Any(component => component.Inspect<Velocity>()), Is.True);
        }

        [Test]
        public void PublicSurface_HasOnlyNonAllocUntypedPluralComponentApi()
        {
            MethodInfo[] pluralMethods = typeof(Entity).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.Name == nameof(Entity.GetComponents))
                .ToArray();

            Assert.That(pluralMethods, Has.Length.EqualTo(1));
            Assert.That(pluralMethods[0].IsGenericMethod, Is.False);
            Assert.That(pluralMethods[0].ReturnType, Is.EqualTo(typeof(int)));
            Assert.That(pluralMethods[0].GetParameters().Select(parameter => parameter.ParameterType),
                Is.EqualTo(new[] { typeof(ICollection<ComponentRef>) }));
            Assert.That(typeof(ComponentRef).GetProperty("RuntimeType"), Is.Null);
        }

        private static void EndTick(World world)
        {
            world.BeginTick();
            world.Tick();
            world.EndTick();
        }

        private struct Position : IComponent<Position>
        {
            internal int X;
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct Velocity : IComponent<Velocity>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }
    }
}
