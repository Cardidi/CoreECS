using System;
using CoreECS.Utils;
using NUnit.Framework;

namespace TinyECS.Test
{
    [TestFixture]
    public class SignalTestUnit
    {
        private Signal<Action> _signal;
        
        [SetUp]
        public void Setup()
        {
            _signal = new Signal<Action>();
        }
        
        [Test]
        public void Add_AddsListenerToSignal()
        {
            // Arrange
            bool called = false;
            Action listener = () => called = true;
            
            // Act
            var disposal = _signal.Add(listener);
            
            // Assert
            Assert.IsNotNull(disposal);
            
            // Verify it's actually added by emitting
            _signal.Emit(h => h());
            Assert.IsTrue(called);
        }
        
        [Test]
        public void Add_WithOrder_ExecutesInCorrectOrder()
        {
            // Arrange
            string executionOrder = "";
            Action first = () => executionOrder += "first";
            Action second = () => executionOrder += "second";
            
            // Act
            _signal.Add(second, 2);
            _signal.Add(first, 1);
            
            // Assert
            _signal.Emit(h => h());
            Assert.AreEqual("firstsecond", executionOrder);
        }
        
        [Test]
        public void Add_DuplicateListener_ThrowsException()
        {
            // Arrange
            Action listener = () => { };
            
            // Act
            _signal.Add(listener);
            
            // Assert
            Assert.Throws<InvalidOperationException>(() => _signal.Add(listener, allowDuplication: false));
        }
        
        [Test]
        public void Remove_RemovesListenerFromSignal()
        {
            // Arrange
            bool called = false;
            Action listener = () => called = true;
            _signal.Add(listener);
            
            // Act
            bool removed = _signal.Remove(listener);
            
            // Assert
            Assert.IsTrue(removed);
            
            // Verify it's actually removed by emitting
            called = false;
            _signal.Emit(h => h());
            Assert.IsFalse(called);
        }
        
        [Test]
        public void Remove_NonExistentListener_ReturnsFalse()
        {
            // Arrange
            Action listener = () => { };
            
            // Act
            bool removed = _signal.Remove(listener);
            
            // Assert
            Assert.IsFalse(removed);
        }
        
        [Test]
        public void Clear_RemovesAllListeners()
        {
            // Arrange
            bool called1 = false;
            bool called2 = false;
            Action listener1 = () => called1 = true;
            Action listener2 = () => called2 = true;
            _signal.Add(listener1);
            _signal.Add(listener2);
            
            // Act
            _signal.Clear();
            
            // Assert
            _signal.Emit(h => h());
            Assert.IsFalse(called1);
            Assert.IsFalse(called2);
        }
        
        [Test]
        public void Emit_WithArgument_PassesArgumentCorrectly()
        {
            // Arrange
            string receivedMessage = "";
            Action<string> listener = message => receivedMessage = message;
            var signal = new Signal<Action<string>>();
            signal.Add(listener);
            
            // Act
            const string testMessage = "Test message";
            signal.Emit(testMessage, (h, msg) => h(msg));
            
            // Assert
            Assert.AreEqual(testMessage, receivedMessage);
        }
        
        [Test]
        public void Emit_WithMultipleArguments_PassesArgumentsCorrectly()
        {
            // Arrange
            int receivedInt = 0;
            string receivedString = "";
            Action<int, string> listener = (num, str) => 
            {
                receivedInt = num;
                receivedString = str;
            };
            var signal = new Signal<Action<int, string>>();
            signal.Add(listener);
            
            // Act
            const int testInt = 42;
            const string testString = "Test";
            signal.Emit(testInt, testString, (h, num, str) => h(num, str));
            
            // Assert
            Assert.AreEqual(testInt, receivedInt);
            Assert.AreEqual(testString, receivedString);
        }
        
        [Test]
        public void Disposal_RemovesListenerFromSignal()
        {
            // Arrange
            bool called = false;
            Action listener = () => called = true;
            var disposal = _signal.Add(listener);
            
            // Act
            disposal.Dispose();
            
            // Assert
            _signal.Emit(h => h());
            Assert.IsFalse(called);
        }
        
        [Test]
        public void Emit_HandlesExceptionsInListeners()
        {
            // Arrange
            bool goodListenerCalled = false;
            Action goodListener = () => goodListenerCalled = true;
            Action badListener = () => throw new InvalidOperationException("Test exception");
            _signal.Add(goodListener);
            _signal.Add(badListener);
            
            // Act & Assert - Should not throw
            Assert.DoesNotThrow(() => _signal.Emit(h => h()));
            
            // Good listener should still be called despite exception in bad listener
            Assert.IsTrue(goodListenerCalled);
        }
    }
}