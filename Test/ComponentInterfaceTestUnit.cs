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
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
            _world = null;
            Log.Logger = null;
            StructuralCreateComponent.World = null;
            StructuralDestroyComponent.Entity = default;
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
        public void CreateComponent_OnCreateMutatesStoredComponentAndReceivesAdaptedEntityId()
        {
            ComponentManager manager = StartWorldAndGetComponentManager();
            Entity entity = _world!.CreateEntity();

            IComponentRefCore core = manager.CreateComponent<MutatingComponent>(entity.EntityId);
            var component = new ComponentRef<MutatingComponent>(core);

            Assert.That(component.RO.Created, Is.True);
            Assert.That(component.RO.CreatedFor.Index, Is.EqualTo(entity.EntityId));
            Assert.That(component.RO.CreatedFor.Generation, Is.EqualTo(1));
        }

        [Test]
        public void CreateComponent_WhenHookThrows_LogsAndCompletesCreation()
        {
            ComponentManager manager = StartWorldAndGetComponentManager();
            Entity entity = _world!.CreateEntity();

            IComponentRefCore core = null!;
            Assert.DoesNotThrow(() => core = manager.CreateComponent<ThrowingComponent>(entity.EntityId));

            Assert.That(core.RefLocator.NotNull(core.Version, core.Offset), Is.True);
            Assert.That(_logger!.Exceptions, Has.Count.EqualTo(1));
            Assert.That(_logger.Exceptions[0], Is.TypeOf<HookFailureException>());
        }

        [Test]
        public void OnCreate_WhenHookCreatesEntity_GuardRejectsNestedMutationAndOuterCreateRemainsCoherent()
        {
            ComponentManager manager = StartWorldAndGetComponentManager();
            Entity entity = _world!.CreateEntity();
            StructuralCreateComponent.World = _world;

            IComponentRefCore core = manager.CreateComponent<StructuralCreateComponent>(entity.EntityId);

            Assert.That(core.RefLocator.NotNull(core.Version, core.Offset), Is.True);
            Assert.That(_world.Query(EntityMatcher.With, new List<ulong>()), Is.EqualTo(1));
            Assert.That(_logger!.Exceptions, Has.Count.EqualTo(1));
            Assert.That(_logger.Exceptions[0], Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void OnDestroy_WhenHookCreatesComponent_GuardRejectsNestedMutationAndOuterDestroyRemainsCoherent()
        {
            ComponentManager manager = StartWorldAndGetComponentManager();
            Entity entity = _world!.CreateEntity();
            StructuralDestroyComponent.Entity = entity;
            IComponentRefCore core = manager.CreateComponent<StructuralDestroyComponent>(entity.EntityId);

            manager.DestroyComponent(core);

            Assert.That(core.RefLocator, Is.Null);
            Assert.That(entity.HasComponent<NestedComponent>(), Is.False);
            Assert.That(_logger!.Exceptions, Has.Count.EqualTo(1));
            Assert.That(_logger.Exceptions[0], Is.TypeOf<InvalidOperationException>());
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
    }
}
