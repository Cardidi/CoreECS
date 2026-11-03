using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace CoreECS.Test
{
    [TestFixture]
    [NonParallelizable]
    public class WorldStartupRollbackTestUnit
    {
        [TestCase(StartupFailurePhase.OnRegister)]
        [TestCase(StartupFailurePhase.ServiceProvider)]
        [TestCase(StartupFailurePhase.ManagerActivation)]
        [TestCase(StartupFailurePhase.OnManagerCreated)]
        [TestCase(StartupFailurePhase.OnWorldStarted)]
        [TestCase(StartupFailurePhase.OnSetup)]
        public void FailedFirstStartup_PropagatesOriginalRollsBackAndRetriesFromOnRegister(
            StartupFailurePhase phase)
        {
            var state = new StartupState(phase);
            var world = new FaultInjectingWorld(state);

            Exception caught = Assert.Throws<InvalidOperationException>(world.Startup)!;

            Assert.Multiple(() =>
            {
                Assert.That(caught, Is.SameAs(state.StartupException));
                Assert.That(world.Ready, Is.False);
                Assert.That(world.Ticking, Is.False);
                Assert.That(world.TickCount, Is.Zero);
                Assert.That(world.InjectionProxy, Is.Null);
                Assert.That(state.RegisterCount, Is.EqualTo(1));
                Assert.That(state.CleanupCount, Is.Zero);
                Assert.That(state.Events, Is.EqualTo(ExpectedEventsAfterFailure(phase)));
            });
            Assert.Throws<InvalidOperationException>(() =>
                world.GetManager<FirstLifecycleManager>());

            world.Startup();
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(world.Ready, Is.True);
                    Assert.That(state.RegisterCount, Is.EqualTo(2));
                    Assert.That(world.GetManager<FirstLifecycleManager>(), Is.Not.Null);
                });
            }
            finally
            {
                world.Shutdown();
            }
        }

        [Test]
        public void StartupRollback_WhenRollbackHooksThrow_LogsAndPreservesOriginalException()
        {
            var state = new StartupState(StartupFailurePhase.OnSetup)
            {
                ThrowOnWorldEnded = true,
                ThrowOnManagerDestroyed = true,
                ThrowOnProxyDispose = true,
            };
            var logger = new CapturingLogger();
            ILogger previousLogger = Log.Logger;
            Log.Logger = logger;
            try
            {
                var world = new FaultInjectingWorld(state);

                Exception caught = Assert.Throws<InvalidOperationException>(world.Startup)!;

                Assert.Multiple(() =>
                {
                    Assert.That(caught, Is.SameAs(state.StartupException));
                    Assert.That(world.Ready, Is.False);
                    Assert.That(world.InjectionProxy, Is.Null);
                    Assert.That(state.Events, Is.EqualTo(new[]
                    {
                        "register",
                        "first.created",
                        "second.created",
                        "first.started",
                        "second.started",
                        "setup",
                        "second.ended",
                        "first.ended",
                        "second.destroyed",
                        "first.destroyed",
                    }));
                    Assert.That(logger.Exceptions, Is.EqualTo(new[]
                    {
                        state.WorldEndedException,
                        state.ManagerDestroyedException,
                        state.ProxyDisposeException,
                    }));
                    Assert.That(state.ProxyDisposeCount, Is.EqualTo(1));
                    Assert.That(state.ProviderDisposeCount, Is.EqualTo(1));
                });
            }
            finally
            {
                Log.Logger = previousLogger;
            }
        }

        [TestCase(StartupFailurePhase.ManagerActivation)]
        [TestCase(StartupFailurePhase.OnManagerCreated)]
        [TestCase(StartupFailurePhase.OnWorldStarted)]
        [TestCase(StartupFailurePhase.OnSetup)]
        public void FailedStartup_AfterProxyCreationDisposesGraphAndRetryBuildsANewGraph(
            StartupFailurePhase phase)
        {
            var state = new StartupState(phase);
            var world = new FaultInjectingWorld(state);

            Assert.Throws<InvalidOperationException>(world.Startup);

            DisposableGraphResource firstResource = state.Resources.Single();
            FirstLifecycleManager firstManager = state.FirstManagers.Single();
            Assert.Multiple(() =>
            {
                Assert.That(firstResource.Disposed, Is.True);
                Assert.That(firstManager.Disposed, Is.True);
                Assert.That(state.ProxyDisposeCount, Is.EqualTo(1));
                Assert.That(state.ProviderDisposeCount, Is.EqualTo(1));
                if (phase != StartupFailurePhase.ManagerActivation)
                    Assert.That(firstManager.DestroyedBeforeDispose, Is.True);
            });

            world.Startup();
            DisposableGraphResource secondResource = state.Resources.Last();
            FirstLifecycleManager secondManager = state.FirstManagers.Last();
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(secondResource, Is.Not.SameAs(firstResource));
                    Assert.That(secondManager, Is.Not.SameAs(firstManager));
                    Assert.That(secondResource.Disposed, Is.False);
                    Assert.That(secondManager.Disposed, Is.False);
                });

                world.Shutdown();

                Assert.Multiple(() =>
                {
                    Assert.That(secondResource.Disposed, Is.False,
                        "successful Shutdown must retain the reusable DI graph");
                    Assert.That(secondManager.Disposed, Is.False,
                        "successful Shutdown must retain manager instances for Task 3");
                    Assert.That(state.ProxyDisposeCount, Is.EqualTo(1));
                    Assert.That(state.ProviderDisposeCount, Is.EqualTo(1));
                });
            }
            finally
            {
                if (world.InjectionProxy is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        [Test]
        public void FailedStartup_WhenProxyIsNotDisposableDisposesItsServiceProvider()
        {
            var state = new StartupState(StartupFailurePhase.OnSetup)
            {
                UseNonDisposableProxy = true,
            };
            var world = new FaultInjectingWorld(state);

            Exception caught = Assert.Throws<InvalidOperationException>(world.Startup)!;

            Assert.Multiple(() =>
            {
                Assert.That(caught, Is.SameAs(state.StartupException));
                Assert.That(state.ProxyDisposeCount, Is.Zero);
                Assert.That(state.ProviderDisposeCount, Is.EqualTo(1));
                Assert.That(state.Resources.Single().Disposed, Is.True);
                Assert.That(state.FirstManagers.Single().Disposed, Is.True);
            });
        }

        [Test]
        public void ManagerAlias_ReceivesEachLifecyclePhaseOncePerInstance()
        {
            var world = new AliasWorld();

            world.Startup();
            AliasManager byImplementation = world.GetManager<AliasManager>();
            IAliasManager byInterface = world.GetManager<IAliasManager>();

            Assert.Multiple(() =>
            {
                Assert.That(byInterface, Is.SameAs(byImplementation));
                Assert.That(byImplementation.CreatedCount, Is.EqualTo(1));
                Assert.That(byImplementation.StartedCount, Is.EqualTo(1));
            });

            world.Shutdown();

            Assert.Multiple(() =>
            {
                Assert.That(byImplementation.EndedCount, Is.EqualTo(1));
                Assert.That(byImplementation.DestroyedCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Startup_UsesCustomInjectionProxyFactoryForWorldAndManagers()
        {
            var marker = new FactoryMarker();
            var factory = new TrackingInjectionProxyFactory();
            var world = new CustomFactoryWorld(factory, marker);

            world.Startup();
            try
            {
                FactoryManager manager = world.GetManager<FactoryManager>();
                Assert.Multiple(() =>
                {
                    Assert.That(factory.CreateCollectionCount, Is.EqualTo(1));
                    Assert.That(factory.CreateProxyCount, Is.EqualTo(1));
                    Assert.That(world.InjectionProxy, Is.SameAs(factory.LastProxy));
                    Assert.That(world.InjectionProxy, Is.TypeOf<TrackingInjectionProxy>());
                    Assert.That(manager.Marker, Is.SameAs(marker));
                });
            }
            finally
            {
                world.Shutdown();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OnRegister_CustomManagerDescriptorTakesPriorityWithoutDuplication(
            bool useFactory)
        {
            var world = new CustomDescriptorWorld(useFactory);

            world.Startup();
            AliasManager concrete = world.GetManager<AliasManager>();
            IAliasManager alias = world.GetManager<IAliasManager>();
            AliasManager[] registered = world.InjectionProxy.ServiceProvider
                .GetServices<AliasManager>().ToArray();
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(concrete, Is.SameAs(world.Instance));
                    Assert.That(alias, Is.SameAs(world.Instance));
                    Assert.That(registered, Has.Length.EqualTo(1));
                    Assert.That(registered[0], Is.SameAs(world.Instance));
                    Assert.That(world.FactoryCalls, Is.EqualTo(useFactory ? 1 : 0));
                    Assert.That(world.Instance.CreatedCount, Is.EqualTo(1));
                    Assert.That(world.Instance.StartedCount, Is.EqualTo(1));
                });
            }
            finally
            {
                world.Shutdown();
            }

            Assert.Multiple(() =>
            {
                Assert.That(world.Instance.EndedCount, Is.EqualTo(1));
                Assert.That(world.Instance.DestroyedCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void OnRegister_CannotReplaceCoreManagerService()
        {
            var replacement = new ComponentManager();
            var world = new CoreReplacementWorld(replacement);

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(world.Startup)!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.Message, Does.Contain(nameof(ComponentManager)));
                Assert.That(world.Ready, Is.False);
                Assert.That(world.InjectionProxy, Is.Null);
            });
        }

        private static string[] ExpectedEventsAfterFailure(StartupFailurePhase phase)
        {
            switch (phase)
            {
                case StartupFailurePhase.OnRegister:
                case StartupFailurePhase.ServiceProvider:
                case StartupFailurePhase.ManagerActivation:
                    return new[] { "register" };
                case StartupFailurePhase.OnManagerCreated:
                    return new[]
                    {
                        "register",
                        "first.created",
                        "second.created",
                        "first.destroyed",
                    };
                case StartupFailurePhase.OnWorldStarted:
                    return new[]
                    {
                        "register",
                        "first.created",
                        "second.created",
                        "first.started",
                        "second.started",
                        "first.ended",
                        "second.destroyed",
                        "first.destroyed",
                    };
                case StartupFailurePhase.OnSetup:
                    return new[]
                    {
                        "register",
                        "first.created",
                        "second.created",
                        "first.started",
                        "second.started",
                        "setup",
                        "second.ended",
                        "first.ended",
                        "second.destroyed",
                        "first.destroyed",
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(phase));
            }
        }

        public enum StartupFailurePhase
        {
            OnRegister,
            ServiceProvider,
            ManagerActivation,
            OnManagerCreated,
            OnWorldStarted,
            OnSetup,
        }

        private sealed class StartupState
        {
            private bool m_failureArmed = true;

            internal StartupState(StartupFailurePhase phase)
            {
                Phase = phase;
            }

            internal StartupFailurePhase Phase { get; }
            internal InvalidOperationException StartupException { get; } =
                new InvalidOperationException("injected startup failure");
            internal InvalidOperationException WorldEndedException { get; } =
                new InvalidOperationException("injected world-ended rollback failure");
            internal InvalidOperationException ManagerDestroyedException { get; } =
                new InvalidOperationException("injected manager-destroyed rollback failure");
            internal InvalidOperationException ProxyDisposeException { get; } =
                new InvalidOperationException("injected proxy disposal failure");
            internal List<string> Events { get; } = new List<string>();
            internal List<DisposableGraphResource> Resources { get; } =
                new List<DisposableGraphResource>();
            internal List<FirstLifecycleManager> FirstManagers { get; } =
                new List<FirstLifecycleManager>();
            internal int RegisterCount { get; set; }
            internal int CleanupCount { get; set; }
            internal int ProxyDisposeCount { get; set; }
            internal int ProviderDisposeCount { get; set; }
            internal bool ThrowOnWorldEnded { get; set; }
            internal bool ThrowOnManagerDestroyed { get; set; }
            internal bool ThrowOnProxyDispose { get; set; }
            internal bool UseNonDisposableProxy { get; set; }

            internal DisposableGraphResource CreateResource()
            {
                var resource = new DisposableGraphResource();
                Resources.Add(resource);
                return resource;
            }

            internal void FailOnce(StartupFailurePhase phase)
            {
                if (m_failureArmed && Phase == phase)
                {
                    m_failureArmed = false;
                    throw StartupException;
                }
            }
        }

        private sealed class FaultInjectingWorld : World
        {
            private readonly StartupState m_state;
            private readonly FaultInjectingProxyFactory m_factory;

            internal FaultInjectingWorld(StartupState state)
            {
                m_state = state;
                m_factory = new FaultInjectingProxyFactory(state);
            }

            protected override IInjectionProxyFactory GetInjectionProxyFactory() => m_factory;

            protected override void OnRegister(IManagerRegister managers,
                IServiceCollection services)
            {
                m_state.RegisterCount++;
                m_state.Events.Add("register");
                services.AddSingleton(m_state);
                services.AddSingleton(_ => m_state.CreateResource());
                managers.RegisterManager<FirstLifecycleManager>();
                managers.RegisterManager<SecondLifecycleManager>();
                m_state.FailOnce(StartupFailurePhase.OnRegister);
            }

            protected override void OnSetup()
            {
                m_state.Events.Add("setup");
                m_state.FailOnce(StartupFailurePhase.OnSetup);
            }

            protected override void OnCleanup()
            {
                m_state.CleanupCount++;
                m_state.Events.Add("cleanup");
            }
        }

        private sealed class FirstLifecycleManager : IWorldManager, IDisposable
        {
            private readonly StartupState m_state;
            private bool m_destroyed;

            public FirstLifecycleManager(StartupState state,
                DisposableGraphResource resource)
            {
                m_state = state;
                Resource = resource;
                state.FirstManagers.Add(this);
            }

            internal DisposableGraphResource Resource { get; }
            internal bool Disposed { get; private set; }
            internal bool DestroyedBeforeDispose { get; private set; }
            public void OnManagerCreated() => m_state.Events.Add("first.created");
            public void OnWorldStarted() => m_state.Events.Add("first.started");
            public void OnWorldEnded() => m_state.Events.Add("first.ended");
            public void OnManagerDestroyed()
            {
                m_state.Events.Add("first.destroyed");
                m_destroyed = true;
            }

            public void Dispose()
            {
                DestroyedBeforeDispose = m_destroyed;
                Disposed = true;
            }
        }

        private sealed class DisposableGraphResource : IDisposable
        {
            internal bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class SecondLifecycleManager : IWorldManager
        {
            private readonly StartupState m_state;

            public SecondLifecycleManager(StartupState state)
            {
                m_state = state;
            }

            public void OnManagerCreated()
            {
                m_state.Events.Add("second.created");
                m_state.FailOnce(StartupFailurePhase.OnManagerCreated);
            }

            public void OnWorldStarted()
            {
                m_state.Events.Add("second.started");
                m_state.FailOnce(StartupFailurePhase.OnWorldStarted);
            }

            public void OnWorldEnded()
            {
                m_state.Events.Add("second.ended");
                if (m_state.ThrowOnWorldEnded)
                    throw m_state.WorldEndedException;
            }

            public void OnManagerDestroyed()
            {
                m_state.Events.Add("second.destroyed");
                if (m_state.ThrowOnManagerDestroyed)
                    throw m_state.ManagerDestroyedException;
            }
        }

        private sealed class FaultInjectingProxyFactory : IInjectionProxyFactory
        {
            private readonly StartupState m_state;

            internal FaultInjectingProxyFactory(StartupState state)
            {
                m_state = state;
            }

            public IServiceCollection CreateServiceCollection() => new ServiceCollection();

            public IInjectionProxy CreateProxy(IServiceCollection collection)
            {
                m_state.FailOnce(StartupFailurePhase.ServiceProvider);
                var holder = new ProxyHolder();
                collection.AddSingleton(holder);
                collection.AddSingleton<IInjectionProxy>(_ => holder.Proxy!);
                IServiceProvider inner = collection.BuildServiceProvider();
                var provider = new FaultInjectingServiceProvider(inner, m_state);
                holder.Proxy = m_state.UseNonDisposableProxy
                    ? (IInjectionProxy)new NonDisposableTrackingInjectionProxy(provider)
                    : new FaultInjectingProxy(provider, m_state);
                return holder.Proxy;
            }

            private sealed class ProxyHolder
            {
                internal IInjectionProxy? Proxy;
            }
        }

        private sealed class FaultInjectingServiceProvider : IServiceProvider, IDisposable
        {
            private readonly IServiceProvider m_inner;
            private readonly StartupState m_state;
            private bool m_disposed;

            internal FaultInjectingServiceProvider(IServiceProvider inner, StartupState state)
            {
                m_inner = inner;
                m_state = state;
            }

            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(SecondLifecycleManager))
                    m_state.FailOnce(StartupFailurePhase.ManagerActivation);
                return m_inner.GetService(serviceType);
            }

            public void Dispose()
            {
                if (m_disposed) return;
                m_disposed = true;
                m_state.ProviderDisposeCount++;
                if (m_inner is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        private sealed class FaultInjectingProxy : IInjectionProxy, IDisposable
        {
            private readonly StartupState m_state;
            private bool m_disposed;

            internal FaultInjectingProxy(IServiceProvider serviceProvider, StartupState state)
            {
                ServiceProvider = serviceProvider;
                m_state = state;
            }

            public IServiceProvider ServiceProvider { get; }

            public object CreateObject(Type objectType) =>
                ActivatorUtilities.CreateInstance(ServiceProvider, objectType);

            public T CreateObject<T>() =>
                ActivatorUtilities.CreateInstance<T>(ServiceProvider);

            public void Dispose()
            {
                if (m_disposed) return;
                m_disposed = true;
                m_state.ProxyDisposeCount++;
                if (ServiceProvider is IDisposable disposable)
                    disposable.Dispose();
                if (m_state.ThrowOnProxyDispose)
                    throw m_state.ProxyDisposeException;
            }
        }

        private sealed class NonDisposableTrackingInjectionProxy : IInjectionProxy
        {
            internal NonDisposableTrackingInjectionProxy(IServiceProvider serviceProvider)
            {
                ServiceProvider = serviceProvider;
            }

            public IServiceProvider ServiceProvider { get; }
            public object CreateObject(Type objectType) =>
                ActivatorUtilities.CreateInstance(ServiceProvider, objectType);
            public T CreateObject<T>() =>
                ActivatorUtilities.CreateInstance<T>(ServiceProvider);
        }

        private interface IAliasManager : IWorldManager
        {
        }

        private sealed class AliasManager : IAliasManager
        {
            internal int CreatedCount { get; private set; }
            internal int StartedCount { get; private set; }
            internal int EndedCount { get; private set; }
            internal int DestroyedCount { get; private set; }

            public void OnManagerCreated() => CreatedCount++;
            public void OnWorldStarted() => StartedCount++;
            public void OnWorldEnded() => EndedCount++;
            public void OnManagerDestroyed() => DestroyedCount++;
        }

        private sealed class AliasWorld : World
        {
            protected override void OnRegister(IManagerRegister managers,
                IServiceCollection services) =>
                managers.RegisterManager<IAliasManager, AliasManager>();
        }

        private sealed class CustomDescriptorWorld : World
        {
            private readonly bool m_useFactory;

            internal CustomDescriptorWorld(bool useFactory)
            {
                m_useFactory = useFactory;
            }

            internal AliasManager Instance { get; } = new AliasManager();
            internal int FactoryCalls { get; private set; }

            protected override void OnRegister(IManagerRegister managers,
                IServiceCollection services)
            {
                managers.RegisterManager<IAliasManager, AliasManager>();
                if (m_useFactory)
                {
                    services.AddSingleton<AliasManager>(_ =>
                    {
                        FactoryCalls++;
                        return Instance;
                    });
                }
                else
                {
                    services.AddSingleton(Instance);
                }
            }
        }

        private sealed class CoreReplacementWorld : World
        {
            private readonly ComponentManager m_replacement;

            internal CoreReplacementWorld(ComponentManager replacement)
            {
                m_replacement = replacement;
            }

            protected override void OnRegister(IManagerRegister managers,
                IServiceCollection services)
            {
                services.AddSingleton(m_replacement);
            }
        }

        private sealed class FactoryMarker
        {
        }

        private sealed class FactoryManager : IWorldManager
        {
            public FactoryManager(FactoryMarker marker)
            {
                Marker = marker;
            }

            internal FactoryMarker Marker { get; }
            public void OnManagerCreated() { }
            public void OnWorldStarted() { }
            public void OnWorldEnded() { }
            public void OnManagerDestroyed() { }
        }

        private sealed class CustomFactoryWorld : World
        {
            private readonly IInjectionProxyFactory m_factory;
            private readonly FactoryMarker m_marker;

            internal CustomFactoryWorld(IInjectionProxyFactory factory, FactoryMarker marker)
            {
                m_factory = factory;
                m_marker = marker;
            }

            protected override IInjectionProxyFactory GetInjectionProxyFactory() => m_factory;

            protected override void OnRegister(IManagerRegister managers,
                IServiceCollection services)
            {
                services.AddSingleton(m_marker);
                managers.RegisterManager<FactoryManager>();
            }
        }

        private sealed class TrackingInjectionProxyFactory : IInjectionProxyFactory
        {
            internal int CreateCollectionCount { get; private set; }
            internal int CreateProxyCount { get; private set; }
            internal IInjectionProxy? LastProxy { get; private set; }

            public IServiceCollection CreateServiceCollection()
            {
                CreateCollectionCount++;
                return new ServiceCollection();
            }

            public IInjectionProxy CreateProxy(IServiceCollection collection)
            {
                CreateProxyCount++;
                var holder = new ProxyHolder();
                collection.AddSingleton(holder);
                collection.AddSingleton<IInjectionProxy>(_ => holder.Proxy!);
                IServiceProvider provider = collection.BuildServiceProvider();
                holder.Proxy = new TrackingInjectionProxy(provider);
                LastProxy = holder.Proxy;
                return holder.Proxy;
            }

            private sealed class ProxyHolder
            {
                internal IInjectionProxy? Proxy;
            }
        }

        private sealed class TrackingInjectionProxy : IInjectionProxy
        {
            internal TrackingInjectionProxy(IServiceProvider serviceProvider)
            {
                ServiceProvider = serviceProvider;
            }

            public IServiceProvider ServiceProvider { get; }

            public object CreateObject(Type objectType) =>
                ActivatorUtilities.CreateInstance(ServiceProvider, objectType);

            public T CreateObject<T>() =>
                ActivatorUtilities.CreateInstance<T>(ServiceProvider);
        }

        private sealed class CapturingLogger : ILogger
        {
            internal List<Exception> Exceptions { get; } = new List<Exception>();

            public void Debug(string msg, object? context = null) { }
            public void Info(string msg, object? context = null) { }
            public void Warn(string msg, object? context = null) { }
            public void Err(string msg, object? context = null) { }
            public void Exp(Exception err, object? context = null) => Exceptions.Add(err);
        }
    }
}
