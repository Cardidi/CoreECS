using CoreECS.Utils;

namespace CoreECS.Test
{
    [TestFixture]
    public class EventDispatchGuardTestUnit
    {
        [Test]
        public void Enter_TwiceWithoutExit_Throws()
        {
            var state = new EventDispatchState();
            using var guard = new EventDispatchGuard(state);

            Assert.Throws<InvalidOperationException>(() => new EventDispatchGuard(state));
        }

        [Test]
        public void Dispose_AllowsReentry()
        {
            var state = new EventDispatchState();
            using (new EventDispatchGuard(state))
            {
            }

            Assert.DoesNotThrow(() => new EventDispatchGuard(state).Dispose());
        }

        [Test]
        public void IndependentStates_DoNotInterfere()
        {
            var first = new EventDispatchState();
            var second = new EventDispatchState();

            using var guardA = new EventDispatchGuard(first);
            Assert.DoesNotThrow(() => new EventDispatchGuard(second).Dispose());
        }
    }
}
