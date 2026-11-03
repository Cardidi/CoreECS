using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;

namespace CoreECS.Test
{
    [TestFixture]
    public class SystemOrderingTestUnit
    {
        private World m_world = null!;
        private RecordingLogger m_logger = null!;

        private SystemSchedule Schedule => m_world.GetManager<SystemManager>().Schedule;

        [SetUp]
        public void SetUp()
        {
            m_logger = new RecordingLogger();
            Log.Logger = m_logger;
            m_world = new World();
            m_world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_world.Ready) m_world.Shutdown();
            Log.Logger = null;
        }

        [Test]
        public void BuildExecutionOrder_FlattensMixedChildrenDepthFirst()
        {
            m_world.RegisterSystem<SystemA>();
            m_world.RegisterGroup("Outer");
            m_world.RegisterSystem<SystemB>("Outer");
            m_world.RegisterGroup("Inner", "Outer");
            m_world.RegisterSystem<SystemC>("Inner");
            m_world.RegisterSystem<SystemD>("Outer");
            m_world.RegisterSystem<SystemE>();

            AssertOrder<SystemA, SystemB, SystemC, SystemD, SystemE>();
        }

        [Test]
        public void BuildExecutionOrder_ResolvesSystemBeforeAndAfterAnchors()
        {
            m_world.RegisterSystem<SystemB>();
            m_world.RegisterSystem<SystemA>().Before<SystemB>();
            m_world.RegisterSystem<SystemC>().After<SystemB>();

            AssertOrder<SystemA, SystemB, SystemC>();
        }

        [Test]
        public void BuildExecutionOrder_ExpandsGroupSourceToSystemTarget()
        {
            m_world.RegisterGroup("Gameplay").After<SystemX>();
            m_world.RegisterSystem<SystemA>("Gameplay");
            m_world.RegisterSystem<SystemB>("Gameplay");
            m_world.RegisterSystem<SystemX>();

            AssertOrder<SystemX, SystemA, SystemB>();
        }

        [Test]
        public void BuildExecutionOrder_ExpandsSystemSourceToGroupTarget()
        {
            m_world.RegisterGroup("Gameplay");
            m_world.RegisterSystem<SystemA>("Gameplay");
            m_world.RegisterSystem<SystemB>("Gameplay");
            m_world.RegisterSystem<SystemX>().After("Gameplay");

            AssertOrder<SystemA, SystemB, SystemX>();
        }

        [Test]
        public void BuildExecutionOrder_ExpandsBothGroupSubtreesForGroupAnchor()
        {
            m_world.RegisterGroup("Right");
            m_world.RegisterSystem<SystemC>("Right");
            m_world.RegisterGroup("RightNested", "Right");
            m_world.RegisterSystem<SystemD>("RightNested");
            m_world.RegisterGroup("Left").Before("Right");
            m_world.RegisterSystem<SystemA>("Left");
            m_world.RegisterGroup("LeftNested", "Left");
            m_world.RegisterSystem<SystemB>("LeftNested");

            AssertOrder<SystemA, SystemB, SystemC, SystemD>();
        }

        [Test]
        public void BuildExecutionOrder_ResolvesCrossLevelForwardGroupAnchor()
        {
            m_world.RegisterSystem<SystemX>().After("Inner");
            m_world.RegisterGroup("Outer");
            m_world.RegisterGroup("Inner", "Outer");
            m_world.RegisterSystem<SystemA>("Inner");
            m_world.RegisterSystem<SystemB>("Outer");

            AssertOrder<SystemA, SystemX, SystemB>();
            Assert.That(m_logger.ErrorMessages, Is.Empty);
        }

        [Test]
        public void BuildExecutionOrder_ResolvesForwardSystemAnchorFromGroup()
        {
            m_world.RegisterGroup("Gameplay").After<SystemX>();
            m_world.RegisterSystem<SystemA>("Gameplay");
            m_world.RegisterSystem<SystemX>();

            AssertOrder<SystemX, SystemA>();
            Assert.That(m_logger.ErrorMessages, Is.Empty);
        }

        [Test]
        public void BuildExecutionOrder_DeduplicatesEdgesBeforeIncrementingIndegree()
        {
            m_world.RegisterSystem<SystemB>();
            SystemRegistration registration = m_world.RegisterSystem<SystemA>();
            registration.Before<SystemB>().Before<SystemB>();

            AssertOrder<SystemA, SystemB>();
            Assert.That(Schedule.LastResolvedEdgeCount, Is.EqualTo(1));
        }

        [Test]
        public void BuildExecutionOrder_LogsAndIgnoresUnknownTargets()
        {
            m_world.RegisterSystem<SystemA>()
                .After<SystemMissing>()
                .Before("MissingGroup");
            m_world.RegisterSystem<SystemB>();

            AssertOrder<SystemA, SystemB>();
            Assert.That(m_logger.ErrorMessages, Has.Count.EqualTo(2));
            Assert.That(m_logger.ErrorMessages[0], Does.Contain(nameof(SystemMissing)));
            Assert.That(m_logger.ErrorMessages[1], Does.Contain("MissingGroup"));
        }

        [Test]
        public void BuildExecutionOrder_SkipsSystemSelfEdge()
        {
            m_world.RegisterSystem<SystemA>().Before<SystemA>();
            m_world.RegisterSystem<SystemB>();

            AssertOrder<SystemA, SystemB>();
            Assert.That(Schedule.LastResolvedEdgeCount, Is.Zero);
            Assert.That(m_logger.ErrorMessages, Is.Empty);
        }

        [Test]
        public void BuildExecutionOrder_TreatsGroupSelfAndAncestorTargetsAsNoOp()
        {
            m_world.RegisterGroup("Parent").Before("Parent");
            m_world.RegisterSystem<SystemA>("Parent");
            m_world.RegisterGroup("Child", "Parent").After("Parent");
            m_world.RegisterSystem<SystemB>("Child");
            m_world.RegisterSystem<SystemC>("Parent");

            AssertOrder<SystemA, SystemB, SystemC>();
            Assert.That(Schedule.LastResolvedEdgeCount, Is.Zero);
            Assert.That(m_logger.ErrorMessages, Is.Empty);
        }

        [Test]
        public void BuildExecutionOrder_SystemAnchorToOwnGroupConstrainsOtherMembers()
        {
            m_world.RegisterGroup("Gameplay");
            m_world.RegisterSystem<SystemA>("Gameplay").After("Gameplay");
            m_world.RegisterSystem<SystemB>("Gameplay");

            AssertOrder<SystemB, SystemA>();
            Assert.That(Schedule.LastResolvedEdgeCount, Is.EqualTo(1));
        }

        [Test]
        public void BuildExecutionOrder_ChoosesLowestFlattenIndexAtEveryStep()
        {
            m_world.RegisterSystem<SystemA>().After<SystemC>();
            m_world.RegisterSystem<SystemB>();
            m_world.RegisterSystem<SystemC>();
            m_world.RegisterSystem<SystemD>();

            AssertOrder<SystemB, SystemC, SystemA, SystemD>();
        }

        [Test]
        public void BuildExecutionOrder_PartialCycleLogsAndFallsBackWholeGraph()
        {
            m_world.RegisterSystem<SystemA>().Before<SystemB>();
            m_world.RegisterSystem<SystemB>().Before<SystemA>();
            m_world.RegisterSystem<SystemC>();
            m_world.RegisterSystem<SystemD>().Before<SystemC>();

            AssertOrder<SystemA, SystemB, SystemC, SystemD>();
            Assert.That(m_logger.ErrorMessages, Has.Count.EqualTo(1));
            Assert.That(m_logger.ErrorMessages[0], Does.Contain("cycle").IgnoreCase);
            Assert.That(Schedule.IsDirty, Is.False);
        }

        [Test]
        public void BuildExecutionOrder_ReturnsTypesMarksCleanAndUpdatesDiagnostics()
        {
            Assert.That(Schedule.IsDirty, Is.False);
            Assert.That(Schedule.RebuildCount, Is.Zero);

            SystemRegistration registration = m_world.RegisterSystem<SystemA>();
            Assert.That(Schedule.IsDirty, Is.True);

            IReadOnlyList<Type> order = Schedule.BuildExecutionOrder();

            Assert.That(order, Is.EqualTo(new[] { typeof(SystemA) }));
            Assert.That(Schedule.IsDirty, Is.False);
            Assert.That(Schedule.RebuildCount, Is.EqualTo(1));
            Assert.That(Schedule.LastResolvedEdgeCount, Is.Zero);

            registration.After<SystemA>();
            Assert.That(Schedule.IsDirty, Is.True);
            Schedule.BuildExecutionOrder();
            Assert.That(Schedule.IsDirty, Is.False);
            Assert.That(Schedule.RebuildCount, Is.EqualTo(2));

            m_world.RegisterGroup("Empty");
            Assert.That(Schedule.IsDirty, Is.True);
            Schedule.BuildExecutionOrder();

            m_world.UnregisterSystem<SystemA>();
            Assert.That(Schedule.IsDirty, Is.True);
        }

        private void AssertOrder<T1, T2>()
        {
            Assert.That(Schedule.BuildExecutionOrder(), Is.EqualTo(new[]
            {
                typeof(T1), typeof(T2),
            }));
        }

        private void AssertOrder<T1, T2, T3>()
        {
            Assert.That(Schedule.BuildExecutionOrder(), Is.EqualTo(new[]
            {
                typeof(T1), typeof(T2), typeof(T3),
            }));
        }

        private void AssertOrder<T1, T2, T3, T4>()
        {
            Assert.That(Schedule.BuildExecutionOrder(), Is.EqualTo(new[]
            {
                typeof(T1), typeof(T2), typeof(T3), typeof(T4),
            }));
        }

        private void AssertOrder<T1, T2, T3, T4, T5>()
        {
            Assert.That(Schedule.BuildExecutionOrder(), Is.EqualTo(new[]
            {
                typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5),
            }));
        }

        private abstract class TestSystem : ISystem
        {
            public void OnTick(ulong tickMask) { }
        }

        private sealed class SystemA : TestSystem { }
        private sealed class SystemB : TestSystem { }
        private sealed class SystemC : TestSystem { }
        private sealed class SystemD : TestSystem { }
        private sealed class SystemE : TestSystem { }
        private sealed class SystemX : TestSystem { }
        private sealed class SystemMissing : TestSystem { }

        private sealed class RecordingLogger : ILogger
        {
            internal List<string> ErrorMessages { get; } = new List<string>();

            public void Debug(string msg, object? context = null) { }
            public void Info(string msg, object? context = null) { }
            public void Warn(string msg, object? context = null) { }
            public void Err(string msg, object? context = null) => ErrorMessages.Add(msg);
            public void Exp(Exception err, object? context = null) { }
        }
    }
}
