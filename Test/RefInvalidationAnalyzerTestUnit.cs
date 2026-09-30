using System.Threading.Tasks;
using CoreECS.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace CoreECS.Test.Analyzers
{
    [TestFixture]
    public class RefInvalidationAnalyzerTestUnit
    {
        private static Task VerifyAsync(string body)
        {
            var test = new CSharpAnalyzerTest<RefInvalidatedByStructuralChangeAnalyzer, NUnitVerifier>
            {
                TestCode = AnalyzerTestSource.Wrap(body),
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            };
            return test.RunAsync();
        }

        [Test]
        public async Task RwRef_UsedAfterCreateComponent_Reports()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                position.X = 1;
                {|ECS0001:entity.CreateComponent<VelocityComponent>()|};
                position.Y = 2;
");
        }

        [Test]
        public async Task RwRef_FinishedBeforeCreateComponent_DoesNotReport()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                position.X = 1;
                position.Y = 2;
                entity.CreateComponent<VelocityComponent>();
");
        }

        [Test]
        public async Task RwRef_AcquiredAfterCreateComponent_DoesNotReport()
        {
            await VerifyAsync(@"
                entity.CreateComponent<VelocityComponent>();
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                position.X = 1;
");
        }

        [TestCase("entity.CreateComponent<VelocityComponent>()")]
        [TestCase("entity.CreateComponent<VelocityComponent>(new VelocityComponent())")]
        [TestCase("entity.DestroyComponent<PositionComponent>()")]
        [TestCase("entity.SetMask(1UL)")]
        [TestCase("world.CreateEntity()")]
        [TestCase("world.CreateEntity(0UL)")]
        [TestCase("world.DestroyEntity(0UL)")]
        [TestCase("world.DestroyEntity(entity)")]
        [TestCase("commands.Playback()")]
        public async Task AllTriggerApis_InvalidateLiveRwRef(string trigger)
        {
            await VerifyAsync($@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {{|ECS0001:{trigger}|}};
                position.X = 1;
");
        }

        [Test]
        public async Task ComponentRefHandle_IsSafeAcrossStructuralChange()
        {
            await VerifyAsync(@"
                var positionRef = entity.GetComponent<PositionComponent>();
                entity.CreateComponent<VelocityComponent>();
                positionRef.RW.X = 1;
");
        }

        [Test]
        public async Task ValueCopyOfRwAccess_DoesNotReport()
        {
            await VerifyAsync(@"
                var x = entity.GetComponent<PositionComponent>().RW.X;
                entity.CreateComponent<VelocityComponent>();
");
        }

        [Test]
        public async Task ReadOnlyRef_UsedAfterStructuralChange_Reports()
        {
            await VerifyAsync(@"
                ref readonly var position = ref entity.GetComponent<PositionComponent>().RO;
                var x = position.X;
                {|ECS0001:entity.DestroyComponent<PositionComponent>()|};
                var y = position.Y;
");
        }

        [Test]
        public async Task ReadWriteSpan_UsedAfterCreateEntity_Reports()
        {
            await VerifyAsync(@"
                var positions = structure.GetReadWriteDenseColumn<PositionComponent>();
                {|ECS0001:world.CreateEntity()|};
                var first = positions[0];
");
        }

        [Test]
        public async Task ReadOnlySpan_UsedAfterCreateEntity_Reports()
        {
            await VerifyAsync(@"
                var positions = structure.GetReadOnlyDenseColumn<PositionComponent>();
                {|ECS0001:world.CreateEntity()|};
                var first = positions[0];
");
        }

        [Test]
        public async Task Span_FinishedBeforeStructuralChange_DoesNotReport()
        {
            await VerifyAsync(@"
                var positions = structure.GetReadWriteDenseColumn<PositionComponent>();
                var first = positions[0];
                world.CreateEntity();
");
        }

        [Test]
        public async Task ExplicitSpanTypedLocal_Reports()
        {
            await VerifyAsync(@"
                Span<PositionComponent> positions = structure.GetReadWriteDenseColumn<PositionComponent>();
                {|ECS0001:world.CreateEntity()|};
                positions[0].X = 1;
");
        }

        [Test]
        public async Task UnrelatedSpan_DoesNotReport()
        {
            await VerifyAsync(@"
                Span<PositionComponent> positions = stackalloc PositionComponent[4];
                world.CreateEntity();
                positions[0].X = 1;
");
        }

        [Test]
        public async Task RefInOuterBlock_TriggerInNestedBlock_Reports()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                if (position.X > 0)
                {
                    {|ECS0001:world.CreateEntity()|};
                }
                position.Y = 1;
");
        }

        [Test]
        public async Task RefUsedAtEndOfMethodAfterTrigger_Reports()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:world.DestroyEntity(0UL)|};
                position.X = 1;
");
        }

        [Test]
        public async Task ReassignmentToDefault_EndsLiveness()
        {
            await VerifyAsync(@"
                var positions = structure.GetReadWriteDenseColumn<PositionComponent>();
                positions = default;
                world.CreateEntity();
                var first = positions[0];
");
        }

        [Test]
        public async Task ReassignmentToNewSource_RestartsLiveness()
        {
            await VerifyAsync(@"
                ReadOnlySpan<PositionComponent> positions = structure.GetReadWriteDenseColumn<PositionComponent>();
                positions = default;
                positions = structure.GetReadOnlyDenseColumn<PositionComponent>();
                {|ECS0001:world.CreateEntity()|};
                var first = positions[0];
");
        }

        [Test]
        public async Task DerivedSpan_Reports()
        {
            await VerifyAsync(@"
                var positions = structure.GetReadWriteDenseColumn<PositionComponent>();
                var slice = positions.Slice(0, 1);
                {|ECS0001:world.CreateEntity()|};
                var first = slice[0];
");
        }

        [Test]
        public async Task RefPassedByRef_EscapesAndReports()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                Helpers.Mutate(ref position);
                {|ECS0001:world.CreateEntity()|};
");
        }

        [Test]
        public async Task RefInsideLocalFunction_Reports()
        {
            await VerifyAsync(@"
                void Local()
                {
                    ref var position = ref entity.GetComponent<PositionComponent>().RW;
                    {|ECS0001:world.CreateEntity()|};
                    position.X = 1;
                }
                Local();
");
        }

        [Test]
        public async Task TwoTriggersDuringLiveness_ReportTwice()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:world.CreateEntity()|};
                {|ECS0001:world.CreateEntity()|};
                position.X = 1;
");
        }

        [Test]
        public async Task PragmaDisable_SuppressesEcs0001()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
#pragma warning disable ECS0001
                world.CreateEntity();
#pragma warning restore ECS0001
                position.X = 1;
");
        }
    }
}
