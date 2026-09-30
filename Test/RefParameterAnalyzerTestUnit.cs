using System.Threading.Tasks;
using CoreECS.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace CoreECS.Test.Analyzers
{
    [TestFixture]
    public class RefParameterAnalyzerTestUnit
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
        public async Task RefParameter_UsedAfterStructuralChange_ReportsEcs0002()
        {
            await VerifyAsync(@"
                void Fill(ref PositionComponent value)
                {
                    {|ECS0002:entity.CreateComponent<VelocityComponent>()|};
                    value.X = 1;
                }
                Fill(ref entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task RefParameter_StructuralChangeWithoutLaterUse_DoesNotReport()
        {
            await VerifyAsync(@"
                void Fill(ref PositionComponent value)
                {
                    entity.CreateComponent<VelocityComponent>();
                }
                Fill(ref entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task SpanParameter_UsedAfterStructuralChange_ReportsEcs0002()
        {
            await VerifyAsync(@"
                void Fill(Span<PositionComponent> values)
                {
                    {|ECS0002:world.CreateEntity()|};
                    var first = values[0];
                }
                Fill(structure.GetReadWriteDenseColumn<PositionComponent>());
");
        }

        [Test]
        public async Task OutParameter_UsedAfterStructuralChange_ReportsEcs0002()
        {
            await VerifyAsync(@"
                void Reset(out int value)
                {
                    value = 0;
                    {|ECS0002:entity.SetMask(1UL)|};
                    value = 1;
                }
                Reset(out var value);
");
        }

        [Test]
        public async Task InParameter_ReadBeforeStructuralChange_DoesNotReport()
        {
            await VerifyAsync(@"
                void Fill(in PositionComponent value)
                {
                    var x = value.X;
                    entity.CreateComponent<VelocityComponent>();
                }
                Fill(in entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task InParameter_ReadAfterStructuralChange_ReportsEcs0002()
        {
            await VerifyAsync(@"
                void Fill(in PositionComponent value)
                {
                    {|ECS0002:entity.CreateComponent<VelocityComponent>()|};
                    var x = value.X;
                }
                Fill(in entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task MultipleRefParameters_AllNamesInMessage()
        {
            var diagnostics = await AnalyzerHarness.GetDiagnosticsAsync(AnalyzerTestSource.Wrap(@"
                void Combine(ref PositionComponent left, Span<PositionComponent> right)
                {
                    world.CreateEntity();
                    left.X = 1;
                    var first = right[0];
                }
                Combine(ref entity.GetComponent<PositionComponent>().RW, structure.GetReadWriteDenseColumn<PositionComponent>());
"));
            var ecs0002 = diagnostics.Single(diagnostic => diagnostic.Id == "ECS0002");
            Assert.That(ecs0002.GetMessage(), Does.Contain("'left, right'"));
            Assert.That(ecs0002.GetMessage(), Does.Contain("'CreateEntity'"));
        }

        [Test]
        public async Task ComponentRefParameter_StructuralChange_DoesNotReportEcs0002()
        {
            await VerifyAsync(@"
                void Touch(ComponentRef<PositionComponent> handle)
                {
                    entity.SetMask(1UL);
                    handle.RW.X = 1;
                }
                Touch(default);
");
        }

        [Test]
        public async Task RefParameter_TransitiveUnsafeCall_ReportsEcs0002()
        {
            await VerifyAsync(@"
                void Inner() { world.CreateEntity(); }
                void Forward(ref PositionComponent value)
                {
                    {|ECS0002:Inner()|};
                    value.X = 1;
                }
                Forward(ref entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task RefParameter_ForwardedToUnsafeMethod_ReportsBothEcs0002()
        {
            await VerifyAsync(@"
                void Mutate(ref PositionComponent value)
                {
                    {|ECS0002:entity.SetMask(1UL)|};
                    value.X = 1;
                }
                void Forward(ref PositionComponent value)
                {
                    {|ECS0002:Mutate(ref value)|};
                    value.X = 2;
                }
                Forward(ref entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task SafeRefParameterMethod_DoesNotReport()
        {
            await VerifyAsync(@"
                void Fill(ref PositionComponent value)
                {
                    value.X = 1;
                }
                Fill(ref entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task RefPassedToUnsafeMethod_ReportsEcs0001AtCall()
        {
            await VerifyAsync(@"
                void Mutate(ref PositionComponent value)
                {
                    {|ECS0002:entity.SetMask(1UL)|};
                    value.X = 1;
                }
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:Mutate(ref position)|};
                position.X = 1;
");
        }
    }
}
