using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;

namespace CoreECS.Test
{
    /// <summary>
    /// Execution order resolution for the Phase 4 scheduling tree: the tree is flattened in
    /// registration order (depth-first; a group's contents land at the group's position),
    /// Before / After anchors are resolved into a stable topological sort, unresolvable
    /// anchors are logged and ignored, and cycles fall back to the flatten order. The resolved
    /// sequence is applied to the running systems at the next BeginTick.
    /// </summary>
    [TestFixture]
    public class SystemOrderingTestUnit
    {
        private static readonly List<string> ExecutionLog = new List<string>();

        private World _world = null!;
        private OrderTestLogger _logger = null!;

        [SetUp]
        public void Setup()
        {
            ExecutionLog.Clear();
            _logger = new OrderTestLogger();
            Log.Logger = _logger;
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
            Log.Logger = null;
        }

        private static string[] ResolvedOrder(World world)
        {
            var order = world.GetManager<SystemManager>().Schedule.BuildExecutionOrder();
            var names = new List<string>();
            foreach (var type in order) names.Add(type.Name);
            return names.ToArray();
        }

        private static string[] RunningOrder(World world)
        {
            var names = new List<string>();
            foreach (var system in world.GetManager<SystemManager>().Systems) names.Add(system.GetType().Name);
            return names.ToArray();
        }

        [Test]
        public void BuildExecutionOrder_WithoutAnchors_ReturnsFlattenOrder()
        {
            _world.RegisterSystem<SystemA>();
            _world.RegisterSystem<SystemB>();
            _world.RegisterSystem<SystemC>();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB", "SystemC" }, ResolvedOrder(_world));
        }

        [Test]
        public void NestedGroups_FlattenDepthFirstInChildOrder()
        {
            _world.RegisterGroup("Outer");
            _world.RegisterSystem<SystemA>("Outer");
            _world.RegisterGroup("Inner", "Outer");
            _world.RegisterSystem<SystemB>("Inner");
            _world.RegisterSystem<SystemC>("Outer");
            _world.RegisterSystem<SystemD>();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB", "SystemC", "SystemD" }, ResolvedOrder(_world));
        }

        [Test]
        public void EarlyGroup_ChangesFlattenPosition()
        {
            _world.RegisterSystem<SystemA>();
            _world.RegisterSystem<SystemB>();
            _world.RegisterGroup("Early", GroupInsertMode.Early);
            _world.RegisterSystem<SystemC>("Early");

            CollectionAssert.AreEqual(new[] { "SystemC", "SystemA", "SystemB" }, ResolvedOrder(_world));
        }

        [Test]
        public void AfterSystem_ReordersDeclaringSystemAfterForwardReferencedTarget()
        {
            _world.RegisterSystem<SystemA>().After<SystemB>();
            _world.RegisterSystem<SystemB>();

            CollectionAssert.AreEqual(new[] { "SystemB", "SystemA" }, ResolvedOrder(_world));
            Assert.AreEqual(0, _logger.ErrorMessages.Count);
        }

        [Test]
        public void BeforeSystem_ReordersDeclaringSystemBeforeTarget()
        {
            _world.RegisterSystem<SystemB>();
            _world.RegisterSystem<SystemA>().Before<SystemB>();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, ResolvedOrder(_world));
        }

        [Test]
        public void AfterGroup_PlacesSystemAfterEntireGroupSubtree()
        {
            _world.RegisterSystem<SystemA>().After("Gameplay");
            _world.RegisterGroup("Gameplay");
            _world.RegisterSystem<SystemB>("Gameplay");
            _world.RegisterSystem<SystemC>("Gameplay");

            CollectionAssert.AreEqual(new[] { "SystemB", "SystemC", "SystemA" }, ResolvedOrder(_world));
        }

        [Test]
        public void BeforeGroup_PlacesSystemBeforeEntireGroupSubtree()
        {
            _world.RegisterGroup("Gameplay");
            _world.RegisterSystem<SystemB>("Gameplay");
            _world.RegisterSystem<SystemC>("Gameplay");
            _world.RegisterSystem<SystemA>().Before("Gameplay");

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB", "SystemC" }, ResolvedOrder(_world));
        }

        [Test]
        public void GroupAnchor_AppliesToEntireSubtree()
        {
            _world.RegisterGroup("Gameplay").After<SystemX>();
            _world.RegisterSystem<SystemA>("Gameplay");
            _world.RegisterSystem<SystemB>("Gameplay");
            _world.RegisterSystem<SystemX>();

            CollectionAssert.AreEqual(new[] { "SystemX", "SystemA", "SystemB" }, ResolvedOrder(_world));
        }

        [Test]
        public void CrossLevelAnchor_TargetsNestedGroupFromRootSystem()
        {
            _world.RegisterSystem<SystemX>().After("Inner");
            _world.RegisterGroup("Outer");
            _world.RegisterGroup("Inner", "Outer");
            _world.RegisterSystem<SystemA>("Inner");

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemX" }, ResolvedOrder(_world));
        }

        [Test]
        public void UnresolvableSystemAnchor_IsLoggedAndIgnored()
        {
            _world.RegisterSystem<SystemA>().After<SystemMissing>();
            _world.RegisterSystem<SystemB>();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, ResolvedOrder(_world));
            Assert.AreEqual(1, _logger.ErrorMessages.Count);
            StringAssert.Contains("SystemMissing", _logger.ErrorMessages[0]);
        }

        [Test]
        public void UnresolvableGroupAnchor_IsLoggedAndIgnored()
        {
            _world.RegisterSystem<SystemA>().After("MissingGroup");
            _world.RegisterSystem<SystemB>();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, ResolvedOrder(_world));
            Assert.AreEqual(1, _logger.ErrorMessages.Count);
            StringAssert.Contains("MissingGroup", _logger.ErrorMessages[0]);
        }

        [Test]
        public void Cycle_FallsBackToFlattenOrderLogsErrorAndStaysRunnable()
        {
            _world.RegisterSystem<SystemA>().Before<SystemB>();
            _world.RegisterSystem<SystemB>().Before<SystemA>();

            _world.BeginTick();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, RunningOrder(_world));
            Assert.AreEqual(1, _logger.ErrorMessages.Count);
            StringAssert.Contains("cycle", _logger.ErrorMessages[0]);

            _world.Tick();
            _world.EndTick();

            Assert.IsTrue(_world.FindSystem<SystemA>().OnTickCalled);
            Assert.IsTrue(_world.FindSystem<SystemB>().OnTickCalled);
        }

        [Test]
        public void UnconstrainedSystems_KeepRegistrationOrderAroundAnchoredSystem()
        {
            _world.RegisterSystem<SystemA>();
            _world.RegisterSystem<SystemB>();
            _world.RegisterSystem<SystemC>().After<SystemD>();
            _world.RegisterSystem<SystemD>();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB", "SystemD", "SystemC" }, ResolvedOrder(_world));
        }

        [Test]
        public void BeginTick_ExecutionFollowsResolvedOrder()
        {
            _world.RegisterSystem<SystemB>();
            _world.RegisterSystem<SystemA>().Before<SystemB>();

            _world.BeginTick();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, RunningOrder(_world));

            _world.Tick();
            _world.EndTick();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, ExecutionLog);
        }

        private class RecordingSystem : ISystem
        {
            public bool OnTickCalled { get; private set; }

            public void OnTick(ulong tickMask)
            {
                OnTickCalled = true;
                ExecutionLog.Add(GetType().Name);
            }
        }

        private class SystemA : RecordingSystem
        {
        }

        private class SystemB : RecordingSystem
        {
        }

        private class SystemC : RecordingSystem
        {
        }

        private class SystemD : RecordingSystem
        {
        }

        private class SystemX : RecordingSystem
        {
        }

        private class SystemMissing : RecordingSystem
        {
        }

        private class OrderTestLogger : ILogger
        {
            public readonly List<string> ErrorMessages = new List<string>();

            public void Debug(string msg, object context = null)
            {
            }

            public void Info(string msg, object context = null)
            {
            }

            public void Warn(string msg, object context = null)
            {
            }

            public void Err(string msg, object context = null)
            {
                ErrorMessages.Add(msg);
            }

            public void Exp(Exception err, object context = null)
            {
            }
        }
    }
}
