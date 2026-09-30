using System.Threading.Tasks;
using CoreECS.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace CoreECS.Test.Analyzers
{
    [TestFixture]
    public class UnsafeTransitivityAnalyzerTestUnit
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

        private static Task VerifyWithDeclarationsAsync(string declarations, string body)
        {
            var test = new CSharpAnalyzerTest<RefInvalidatedByStructuralChangeAnalyzer, NUnitVerifier>
            {
                TestCode = AnalyzerTestSource.WrapWith(declarations, body),
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            };
            return test.RunAsync();
        }

        [Test]
        public async Task DirectWrapperCall_InvalidatesLiveRef_Reports()
        {
            await VerifyAsync(@"
                void Wrapper() => entity.CreateComponent<VelocityComponent>();
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:Wrapper()|};
                position.X = 1;
");
        }

        [Test]
        public async Task TransitiveWrapperCall_InvalidatesLiveRef_Reports()
        {
            await VerifyAsync(@"
                void Inner() { world.CreateEntity(); }
                void Outer() { Inner(); }
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:Outer()|};
                position.X = 1;
");
        }

        [Test]
        public async Task RecursiveUnsafeMethods_ConvergeAndReport()
        {
            await VerifyAsync(@"
                void A() { B(); }
                void B() { A(); world.CreateEntity(); }
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:A()|};
                position.X = 1;
");
        }

        [Test]
        public async Task SafeMethodCall_DoesNotInvalidate()
        {
            await VerifyAsync(@"
                void Safe() { var x = 1; }
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                Safe();
                position.X = 1;
");
        }

        [Test]
        public async Task VirtualDispatch_ToUnsafeOverride_InvalidatesLiveRef()
        {
            await VerifyWithDeclarationsAsync(@"
public class BaseAction
{
    public virtual void Run() { }
}

public class DerivedAction : BaseAction
{
    public override void Run() { TestWorld.Instance.CreateEntity(); }
}
", @"
                TestWorld.Instance = world;
                BaseAction action = new DerivedAction();
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:action.Run()|};
                position.X = 1;
");
        }

        [Test]
        public async Task InterfaceDispatch_ToUnsafeImplementation_InvalidatesLiveRef()
        {
            await VerifyWithDeclarationsAsync(@"
public interface IAction
{
    void Run();
}

public sealed class ActionImpl : IAction
{
    public void Run() { TestWorld.Instance.CreateEntity(); }
}
", @"
                TestWorld.Instance = world;
                IAction action = new ActionImpl();
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:action.Run()|};
                position.X = 1;
");
        }

        [Test]
        public async Task LambdaContainingStructuralChange_MakesMethodUnsafe()
        {
            await VerifyAsync(@"
                void Outer()
                {
                    Action action = () => world.CreateEntity();
                }
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:Outer()|};
                position.X = 1;
");
        }

        [Test]
        public async Task UnsafePropertyGetter_InvalidatesLiveRef()
        {
            await VerifyWithDeclarationsAsync(@"
public class UnsafeAccessor
{
    public int Value
    {
        get
        {
            TestWorld.Instance.CreateEntity();
            return 0;
        }
    }
}
", @"
                TestWorld.Instance = world;
                var accessor = new UnsafeAccessor();
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                var value = {|ECS0001:accessor.Value|};
                position.X = 1;
");
        }

        [Test]
        public async Task UnsafeConstructor_InvalidatesLiveRef()
        {
            await VerifyWithDeclarationsAsync(@"
public class UnsafeCtor
{
    public UnsafeCtor()
    {
        TestWorld.Instance.CreateEntity();
    }
}
", @"
                TestWorld.Instance = world;
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:new UnsafeCtor()|};
                position.X = 1;
");
        }
    }
}
