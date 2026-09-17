using System.Diagnostics;
using CoreECS.Defines;

namespace CoreECS.Test
{
    /// <summary>
    /// Pre-optimization baseline for RW/RO access and Flush settlement.
    /// Task 9/10 turn the printed numbers into assertions.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public class PerformanceBaselineTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public float X;
            public float Y;
        }

        private const int AccessIterations = 200_000;
        private const int Rounds = 4;
        private const int CollectorFanout = 1000;

        [Test]
        public void Baseline_NonCachedRoVsRw_ByCollectorCount()
        {
            foreach (var collectors in new[] { 0, 100, 1000 })
            {
                var ro = MeasureAccess(collectors, readOnly: true);
                var rw = MeasureAccess(collectors, readOnly: false);
                Console.WriteLine(
                    $"[baseline] collectors={collectors} RO={ro:F3}ms RW={rw:F3}ms ratio={rw / ro:F3}x");
            }
        }

        [Test]
        public void Baseline_FlushSettlement()
        {
            var f1 = MeasureFlush(collectors: CollectorFanout, changedEntities: 1, writesPerEntity: AccessIterations);
            Console.WriteLine($"[baseline] F1 collectors={CollectorFanout} entities=1 writes={AccessIterations} flush={f1:F3}ms");

            var f2 = MeasureFlush(collectors: CollectorFanout, changedEntities: 1000, writesPerEntity: 1);
            Console.WriteLine($"[baseline] F2 collectors={CollectorFanout} entities=1000 flush={f2:F3}ms");
        }

        [Test]
        public void Baseline_PipelineWritesPlusFlush()
        {
            var total = MeasurePipeline(collectors: CollectorFanout, writes: AccessIterations);
            Console.WriteLine($"[baseline] F3 collectors={CollectorFanout} writes={AccessIterations} total={total:F3}ms");
        }

        private static double MeasureAccess(int collectorCount, bool readOnly)
        {
            return MeasureBest(() =>
            {
                var world = new World();
                world.Startup();
                var collectors = CreateCollectors(world, collectorCount);
                var entity = world.CreateEntity();
                var position = entity.CreateComponent<Position>();
                FlushAll(collectors);

                var sum = 0f;
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < AccessIterations; i++)
                {
                    if (readOnly) sum += position.RO.X;
                    else position.RW.X = i;
                }
                sw.Stop();

                FlushAll(collectors);
                foreach (var c in collectors) c.Dispose();
                world.Shutdown();
                Assert.IsTrue(sum >= 0f || !readOnly);
                return TicksToMs(sw.ElapsedTicks);
            });
        }

        private static double MeasureFlush(int collectors, int changedEntities, int writesPerEntity)
        {
            return MeasureBest(() =>
            {
                var world = new World();
                world.Startup();
                var collectorList = CreateCollectors(world, collectors);
                var entities = new List<Entity>();
                for (var i = 0; i < changedEntities; i++)
                {
                    var e = world.CreateEntity();
                    e.CreateComponent<Position>();
                    entities.Add(e);
                }
                FlushAll(collectorList);

                for (var w = 0; w < writesPerEntity; w++)
                {
                    for (var i = 0; i < entities.Count; i++)
                        entities[i].GetComponent<Position>().RW.X = w;
                }

                var sw = Stopwatch.StartNew();
                FlushAll(collectorList);
                sw.Stop();

                foreach (var c in collectorList) c.Dispose();
                world.Shutdown();
                return TicksToMs(sw.ElapsedTicks);
            });
        }

        private static double MeasurePipeline(int collectors, int writes)
        {
            return MeasureBest(() =>
            {
                var world = new World();
                world.Startup();
                var collectorList = CreateCollectors(world, collectors);
                var entity = world.CreateEntity();
                var position = entity.CreateComponent<Position>();
                FlushAll(collectorList);

                var sw = Stopwatch.StartNew();
                for (var i = 0; i < writes; i++)
                    position.RW.X = i;
                FlushAll(collectorList);
                sw.Stop();

                foreach (var c in collectorList) c.Dispose();
                world.Shutdown();
                return TicksToMs(sw.ElapsedTicks);
            });
        }

        private static List<IEntityCollector> CreateCollectors(World world, int count)
        {
            var result = new List<IEntityCollector>(count);
            for (var i = 0; i < count; i++)
            {
                result.Add(world.CreateCollector(
                    EntityMatcher.With.OfAll<Position>(),
                    EntityCollectorFlag.RevisionAsChange));
            }
            return result;
        }

        private static void FlushAll(List<IEntityCollector> collectors)
        {
            for (var i = 0; i < collectors.Count; i++) collectors[i].Flush();
        }

        private static double MeasureBest(Func<double> scenario)
        {
            scenario();
            var best = scenario();
            for (var i = 1; i < Rounds; i++)
            {
                var current = scenario();
                if (current < best) best = current;
            }
            return best;
        }

        private static double TicksToMs(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    }

    /// <summary>Pre-optimization numbers captured by Task 0 (see baseline doc).</summary>
    internal static class PerformanceBaselineValues
    {
        public static double F1Ms = 0.127d;
        public static double F2Ms = 0.987d;
        public static double F3Ms = 12463.047d;
    }
}
