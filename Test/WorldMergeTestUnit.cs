using CoreECS.Defines;
using CoreECS.Managers;

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

        private class CoreOnlyProbeWorld : World
        {
            protected override void OnRegister(IManagerRegister register)
            {
            }
        }

        private class CustomManagerProbeWorld : World
        {
            protected override void OnRegister(IManagerRegister register)
            {
                register.RegisterManager<ProbeManager>();
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
