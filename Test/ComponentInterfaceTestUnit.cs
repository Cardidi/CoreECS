using System.Reflection;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Test
{
    [TestFixture]
    [NonParallelizable]
    public class ComponentInterfaceTestUnit
    {
        private World? _world;
        private RecordingLogger? _logger;

        [SetUp]
        public void SetUp()
        {
            _logger = new RecordingLogger();
            Log.Logger = _logger;
            TagWithPublicHooks.CreateCallCount = 0;
            TagWithPublicHooks.DestroyCallCount = 0;
            StructuralCreateComponent.World = null;
            StructuralDestroyComponent.Entity = default;
            WorldShutdownComponent.World = null;
            ManagerTeardownComponent.EntityManager = null;
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
            _world = null;
            Log.Logger = null;
            StructuralCreateComponent.World = null;
            StructuralDestroyComponent.Entity = default;
            WorldShutdownComponent.World = null;
            ManagerTeardownComponent.EntityManager = null;
        }

        [Test]
        public void IComponent_DataHooksRequireEntityIdImplementations()
        {
            MethodInfo create = typeof(IComponent<MutatingComponent>).GetMethod(nameof(MutatingComponent.OnCreate))!;
            MethodInfo destroy = typeof(IComponent<MutatingComponent>).GetMethod(nameof(MutatingComponent.OnDestroy))!;

            Assert.That(create.GetParameters().Single().ParameterType, Is.EqualTo(typeof(EntityId)));
            Assert.That(destroy.GetParameters().Single().ParameterType, Is.EqualTo(typeof(EntityId)));
            Assert.That(create.IsAbstract, Is.True);
            Assert.That(destroy.IsAbstract, Is.True);
        }

        [Test]
        public void EmptyTag_UsesTagKindWithoutDeclaringHooks()
        {
            ComponentTypeMetadata metadata = ComponentTypeRegistry.GetOrRegister<EmptyTag>();

            Assert.That(metadata.Kind, Is.EqualTo(ComponentKind.Tag));
        }

        [Test]
        public void TagDispatcher_DoesNotInvokePublicSameNamedHooks()
        {
            var tag = new TagWithPublicHooks();
            EntityId entityId = EntityId.Create(1, 1);

            ComponentHookDispatcher<TagWithPublicHooks>.InvokeCreate(ref tag, entityId);
            ComponentHookDispatcher<TagWithPublicHooks>.InvokeDestroy(ref tag, entityId);

            Assert.That(TagWithPublicHooks.CreateCallCount, Is.Zero);
            Assert.That(TagWithPublicHooks.DestroyCallCount, Is.Zero);
        }

        [Test]
        public void Register_TagWithInstanceFields_ThrowsConfigurationError()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => ComponentTypeRegistry.Register(typeof(TagWithData)))!;

            Assert.That(exception.Message, Does.Contain("instance fields"));
        }

        [Test]
        public void CreateComponent_TagWithInstanceFields_RejectsBeforePublishingAnyState()
        {
            ComponentManager manager = StartWorldAndGetComponentManager();
            Entity entity = _world!.CreateEntity();
            EntityGraph graph = _world.GetManager<EntityManager>().Get(entity.EntityId)!;
            int componentCountBefore = graph.RwComponents.Count;
            int storageCountBefore = manager.StorageRegistry.Count;

            Exception exception = Assert.Catch(
                () => entity.CreateComponent<TagWithData>())!;

            Assert.That(manager.StorageRegistry.Count, Is.EqualTo(storageCountBefore));
            Assert.That(graph.RwComponents, Has.Count.EqualTo(componentCountBefore));
            Assert.That(exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(exception.Message, Does.Contain("instance fields"));
        }

        [Test]
        public void CreateComponent_OnCreateMutatesStoredComponentAndReceivesNativeEntityId()
        {
            StartWorldAndGetComponentManager();
            Entity entity = _world!.CreateEntity();

            ComponentRef<MutatingComponent> component = entity.CreateComponent<MutatingComponent>();

            Assert.That(component.RO.Created, Is.True);
            Assert.That(component.RO.CreatedFor, Is.EqualTo(entity.EntityId));
        }

        [Test]
        public void CreateComponent_WhenHookThrows_LogsAndCompletesCreation()
        {
            StartWorldAndGetComponentManager();
            Entity entity = _world!.CreateEntity();

            ComponentRef<ThrowingComponent> component = default;
            Assert.DoesNotThrow(() => component = entity.CreateComponent<ThrowingComponent>());

            Assert.That(component.NotNull, Is.True);
            Assert.That(_logger!.Exceptions, Has.Count.EqualTo(1));
            Assert.That(_logger.Exceptions[0], Is.TypeOf<HookFailureException>());
        }

        [Test]
        public void OnCreate_WhenHookCreatesEntity_GuardRejectsNestedMutationAndOuterCreateRemainsCoherent()
        {
            StartWorldAndGetComponentManager();
            Entity entity = _world!.CreateEntity();
            StructuralCreateComponent.World = _world;

            ComponentRef<StructuralCreateComponent> component = entity.CreateComponent<StructuralCreateComponent>();

            Assert.That(component.NotNull, Is.True);
            Assert.That(_world.Query(EntityMatcher.With, new List<ulong>()), Is.EqualTo(1));
            Assert.That(_logger!.Exceptions, Has.Count.EqualTo(1));
            Assert.That(_logger.Exceptions[0], Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void OnDestroy_WhenHookCreatesComponent_GuardRejectsNestedMutationAndOuterDestroyRemainsCoherent()
        {
            StartWorldAndGetComponentManager();
            Entity entity = _world!.CreateEntity();
            StructuralDestroyComponent.Entity = entity;
            ComponentRef<StructuralDestroyComponent> component = entity.CreateComponent<StructuralDestroyComponent>();

            entity.DestroyComponent(component);

            Assert.That(component.NotNull, Is.False);
            Assert.That(entity.HasComponent<NestedComponent>(), Is.False);
            Assert.That(_logger!.Exceptions, Has.Count.EqualTo(1));
            Assert.That(_logger.Exceptions[0], Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void OnCreate_WhenHookShutsDownWorld_GuardRejectsTeardownAndOuterCreateRemainsCoherent()
        {
            StartWorldAndGetComponentManager();
            Entity entity = _world!.CreateEntity();
            WorldShutdownComponent.World = _world;

            ComponentRef<WorldShutdownComponent> component = entity.CreateComponent<WorldShutdownComponent>();

            Assert.That(_world.Ready, Is.True);
            Assert.That(entity.IsValid, Is.True);
            Assert.That(component.NotNull, Is.True);
            Assert.That(entity.HasComponent<WorldShutdownComponent>(), Is.True);
            Assert.That(_logger!.Exceptions, Has.Count.EqualTo(1));
            Assert.That(_logger.Exceptions[0], Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void OnCreate_WhenHookDirectlyDestroysEntityManager_GuardRejectsTeardownAndOuterCreateRemainsCoherent()
        {
            StartWorldAndGetComponentManager();
            EntityManager entityManager = _world!.GetManager<EntityManager>();
            Entity entity = _world.CreateEntity();
            ManagerTeardownComponent.EntityManager = entityManager;

            ComponentRef<ManagerTeardownComponent> component = entity.CreateComponent<ManagerTeardownComponent>();

            Assert.That(_world.Ready, Is.True);
            Assert.That(entity.IsValid, Is.True);
            Assert.That(entityManager.EntityCaches, Has.Count.EqualTo(1));
            Assert.That(component.NotNull, Is.True);
            Assert.That(entity.HasComponent<ManagerTeardownComponent>(), Is.True);
            Assert.That(_logger!.Exceptions, Has.Count.EqualTo(1));
            Assert.That(_logger.Exceptions[0], Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void LegacyManagerConstructors_AreAvailableBeforeWorldStartup()
        {
            var world = new World();
            var componentManager = new ComponentManager();
            var entityManager = new EntityManager(world, componentManager);
            var injectionProxy = new ConstructorOnlyInjectionProxy();

            Assert.DoesNotThrow(() => new EntityMatchManager(world, entityManager));
            Assert.DoesNotThrow(() => new SystemManager(world, injectionProxy));
        }

        [Test]
        public void FullManagerConstructors_UseExplicitGuardDependency()
        {
            var world = new World();
            var componentManager = new ComponentManager();
            var entityManager = new EntityManager(world, componentManager);
            var entityMatchManager = new EntityMatchManager(world, entityManager, componentManager);
            var systemManager = new SystemManager(
                world, new ConstructorOnlyInjectionProxy(), componentManager);

            using (componentManager.StructuralMutationGuard.EnterHook())
            {
                Assert.Throws<InvalidOperationException>(entityMatchManager.OnManagerDestroyed);
                Assert.Throws<InvalidOperationException>(systemManager.OnManagerDestroyed);
            }
        }

        [Test]
        public void LegacyManagerConstructors_ResolveSameWorldGuardAfterStartup()
        {
            ComponentManager componentManager = StartWorldAndGetComponentManager();
            EntityManager entityManager = _world!.GetManager<EntityManager>();
            var entityMatchManager = new EntityMatchManager(_world, entityManager);
            var systemManager = new SystemManager(_world, _world.InjectionProxy);

            using (componentManager.StructuralMutationGuard.EnterHook())
            {
                Assert.Throws<InvalidOperationException>(entityMatchManager.OnManagerDestroyed);
                Assert.Throws<InvalidOperationException>(systemManager.OnManagerDestroyed);
            }
        }

        [Test]
        public void WorldStartup_DiSelectsFullManagerConstructors()
        {
            StartWorldAndGetComponentManager();

            Assert.That(
                _world!.GetManager<EntityMatchManager>().HasInjectedGuardForTesting,
                Is.True);
            Assert.That(
                _world.GetManager<SystemManager>().HasInjectedGuardForTesting,
                Is.True);
        }

        private ComponentManager StartWorldAndGetComponentManager()
        {
            _world = new World();
            _world.Startup();
            return _world.GetManager<ComponentManager>();
        }

        private struct MutatingComponent : IComponent<MutatingComponent>
        {
            public bool Created;
            public EntityId CreatedFor;

            public void OnCreate(EntityId entityId)
            {
                Created = true;
                CreatedFor = entityId;
            }

            public void OnDestroy(EntityId entityId)
            {
            }
        }

        private struct EmptyTag : ITagComponent<EmptyTag>
        {
        }

        private struct TagWithPublicHooks : ITagComponent<TagWithPublicHooks>
        {
            public static int CreateCallCount;
            public static int DestroyCallCount;

            public void OnCreate(EntityId entityId)
            {
                CreateCallCount++;
            }

            public void OnDestroy(EntityId entityId)
            {
                DestroyCallCount++;
            }
        }

        private struct TagWithData : ITagComponent<TagWithData>
        {
            public int Value;
        }

        private struct ThrowingComponent : IComponent<ThrowingComponent>
        {
            public void OnCreate(EntityId entityId)
            {
                throw new HookFailureException();
            }

            public void OnDestroy(EntityId entityId)
            {
            }
        }

        private struct StructuralCreateComponent : IComponent<StructuralCreateComponent>
        {
            public static World? World;

            public void OnCreate(EntityId entityId)
            {
                World!.CreateEntity();
            }

            public void OnDestroy(EntityId entityId)
            {
            }
        }

        private struct StructuralDestroyComponent : IComponent<StructuralDestroyComponent>
        {
            public static Entity Entity;

            public void OnCreate(EntityId entityId)
            {
            }

            public void OnDestroy(EntityId entityId)
            {
                Entity.CreateComponent<NestedComponent>();
            }
        }

        private struct NestedComponent : IComponent<NestedComponent>
        {
            public void OnCreate(EntityId entityId)
            {
            }

            public void OnDestroy(EntityId entityId)
            {
            }
        }

        private struct WorldShutdownComponent : IComponent<WorldShutdownComponent>
        {
            public static World? World;

            public void OnCreate(EntityId entityId)
            {
                World!.Shutdown();
            }

            public void OnDestroy(EntityId entityId)
            {
            }
        }

        private struct ManagerTeardownComponent : IComponent<ManagerTeardownComponent>
        {
            public static EntityManager? EntityManager;

            public void OnCreate(EntityId entityId)
            {
                EntityManager!.OnManagerDestroyed();
            }

            public void OnDestroy(EntityId entityId)
            {
            }
        }

        private sealed class HookFailureException : Exception
        {
        }

        private sealed class RecordingLogger : ILogger
        {
            internal List<Exception> Exceptions { get; } = new();

            public void Debug(string msg, object? context = null)
            {
            }

            public void Info(string msg, object? context = null)
            {
            }

            public void Warn(string msg, object? context = null)
            {
            }

            public void Err(string msg, object? context = null)
            {
            }

            public void Exp(Exception err, object? context = null)
            {
                Exceptions.Add(err);
            }
        }

        private sealed class ConstructorOnlyInjectionProxy : IInjectionProxy
        {
            public IServiceProvider ServiceProvider => null!;

            public object CreateObject(Type objectType)
            {
                throw new NotSupportedException();
            }

            public T CreateObject<T>()
            {
                throw new NotSupportedException();
            }
        }
    }
}
