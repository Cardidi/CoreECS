using System.Reflection;
using CoreECS.Defines;
using CoreECS.Managers;
using Microsoft.Extensions.DependencyInjection;

namespace CoreECS.Test
{
    /// <summary>
    /// Contract for the Phase 5 World merge: World is the only world type
    /// (MinimalWorld is deleted), core managers are built in so subclasses
    /// cannot lose them, and OnRegister stays the extension point for
    /// custom managers.
    /// </summary>
    [TestFixture]
    public class WorldMergeTestUnit
    {
        [Test]
        public void World_IsSoleConcreteEntryPoint_MinimalWorldRemoved()
        {
            var world = new World();

            Assert.IsFalse(typeof(World).IsAbstract);
            Assert.IsInstanceOf<IWorld>(world);
            Assert.IsNull(typeof(World).Assembly.GetType("CoreECS.MinimalWorld"));
        }

        [Test]
        public void World_BuildsInCoreManagers_EvenWhenOnRegisterIsOverridden()
        {
            var world = new CoreOnlyProbeWorld();
            world.Startup();

            Assert.IsNotNull(world.GetManager<ComponentManager>());
            Assert.IsNotNull(world.GetManager<EntityManager>());
            Assert.IsNotNull(world.GetManager<EntityMatchManager>());
            Assert.IsNotNull(world.GetManager<SystemManager>());

            world.Shutdown();
        }

        [Test]
        public void World_OnRegister_RegistersCustomManagers()
        {
            var world = new CustomManagerProbeWorld();
            world.Startup();

            var manager = world.GetManager<ProbeManager>();
            Assert.IsNotNull(manager);
            Assert.IsTrue(manager.OnManagerCreatedCalled);
            Assert.IsTrue(manager.OnWorldStartedCalled);

            world.Shutdown();
            Assert.IsTrue(manager.OnWorldEndedCalled);
            Assert.IsTrue(manager.OnManagerDestroyedCalled);
        }

        [Test]
        public void World_HookSurface_IsConvergedToRegisterSetupCleanup()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            Assert.IsNull(typeof(World).GetMethod("OnTickBegin", flags));
            Assert.IsNull(typeof(World).GetMethod("OnTick", flags));
            Assert.IsNull(typeof(World).GetMethod("OnTickEnd", flags));
            Assert.IsNull(typeof(World).GetMethod("RegisterServices", flags));
            Assert.IsNull(typeof(World).GetMethod("OnConstruct", flags));
            Assert.IsNull(typeof(World).GetMethod("OnFirstStart", flags));
            Assert.IsNull(typeof(World).GetMethod("OnStart", flags));
            Assert.IsNull(typeof(World).GetMethod("OnShutdown", flags));

            var setup = typeof(World).GetMethod("OnSetup", flags);
            Assert.IsNotNull(setup);
            Assert.IsTrue(setup!.IsVirtual);
            Assert.AreEqual(0, setup.GetParameters().Length);

            var cleanup = typeof(World).GetMethod("OnCleanup", flags);
            Assert.IsNotNull(cleanup);
            Assert.IsTrue(cleanup!.IsVirtual);
            Assert.AreEqual(0, cleanup.GetParameters().Length);

            var register = typeof(World).GetMethod("OnRegister", flags);
            Assert.IsNotNull(register);
            Assert.IsTrue(register!.IsVirtual);
            CollectionAssert.AreEqual(
                new[] { typeof(IManagerRegister), typeof(IServiceCollection) },
                register.GetParameters().Select(p => p.ParameterType).ToArray());
        }

        [Test]
        public void World_TicksSystems_WhenOnSetupIsOverriddenWithoutBase()
        {
            var world = new SetupProbeWorld();
            world.Startup();
            world.RegisterSystem<TickProbeSystem>();

            world.BeginTick();
            world.Tick();
            world.EndTick();

            Assert.IsTrue(world.SetupCalled);
            Assert.AreEqual(1u, world.TickCount);
            Assert.IsTrue(world.FindSystem<TickProbeSystem>().TickCalled);

            world.Shutdown();
        }

        [Test]
        public void World_UserServiceRegistration_TakesPrecedenceOverBuiltIn()
        {
            var world = new PrecedenceProbeWorld();
            world.Startup();

            Assert.AreSame(world.PrebuiltManager, world.GetManager<ProbeManager>());

            world.Shutdown();
        }

        [Test]
        public void World_SecondShutdown_ThrowsAndDoesNotCleanupAgain()
        {
            var world = new CleanupProbeWorld();
            world.Startup();
            world.Shutdown();

            Assert.AreEqual(1, world.CleanupCount);
            Assert.Throws<InvalidOperationException>(() => world.Shutdown());
            Assert.AreEqual(1, world.CleanupCount);
        }

        private class CoreOnlyProbeWorld : World
        {
            protected override void OnRegister(IManagerRegister register, IServiceCollection services)
            {
            }
        }

        private class CustomManagerProbeWorld : World
        {
            protected override void OnRegister(IManagerRegister register, IServiceCollection services)
            {
                register.RegisterManager<ProbeManager>();
            }
        }

        private class SetupProbeWorld : World
        {
            public bool SetupCalled { get; private set; }

            protected override void OnSetup()
            {
                SetupCalled = true;
            }
        }

        private class TickProbeSystem : ISystem
        {
            public bool TickCalled { get; private set; }

            public void OnTick(ulong tickMask)
            {
                TickCalled = true;
            }
        }

        private class PrecedenceProbeWorld : World
        {
            public ProbeManager PrebuiltManager { get; } = new ProbeManager();

            protected override void OnRegister(IManagerRegister register, IServiceCollection services)
            {
                services.AddSingleton(PrebuiltManager);
                register.RegisterManager<ProbeManager>();
            }
        }

        private class CleanupProbeWorld : World
        {
            public int CleanupCount { get; private set; }

            protected override void OnCleanup()
            {
                CleanupCount += 1;
            }
        }

        private class ProbeManager : IWorldManager
        {
            public bool OnManagerCreatedCalled { get; private set; }

            public bool OnWorldStartedCalled { get; private set; }

            public bool OnWorldEndedCalled { get; private set; }

            public bool OnManagerDestroyedCalled { get; private set; }

            public void OnManagerCreated()
            {
                OnManagerCreatedCalled = true;
            }

            public void OnWorldStarted()
            {
                OnWorldStartedCalled = true;
            }

            public void OnWorldEnded()
            {
                OnWorldEndedCalled = true;
            }

            public void OnManagerDestroyed()
            {
                OnManagerDestroyedCalled = true;
            }
        }
    }
}
