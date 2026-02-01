using System;
using System.Runtime.Serialization;
using CoreECS.Utils;
using NUnit.Framework;

namespace TinyECS.Test
{
    [TestFixture]
    public class InjectorTestUnit
    {
        private Injector _injector;
        
        [SetUp]
        public void Setup()
        {
            _injector = new Injector();
        }
        
        [Test]
        public void Register_AddsInstanceToCollection()
        {
            // Arrange
            var service = new TestService();
            
            // Act
            _injector.Register(service);
            
            // Assert
            Assert.That(_injector.Instances.Count, Is.EqualTo(1));
            Assert.That(_injector.Instances[0], Is.SameAs(service));
        }
        
        [Test]
        public void Register_NullInstance_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _injector.Register(null));
        }
        
        [Test]
        public void InjectConstructor_WithMatchingDependencies_InjectsCorrectly()
        {
            // Arrange
            var service = new TestService();
            _injector.Register(service);
            
            // Create consumer without parameters using reflection
            var consumer = (ServiceConsumer)FormatterServices.GetUninitializedObject(typeof(ServiceConsumer));
            
            // Act
            _injector.InjectConstructor(consumer);
            
            // Assert
            Assert.IsNotNull(consumer.Service);
            Assert.That(consumer.Service, Is.SameAs(service));
        }
        
        [Test]
        public void InjectConstructor_WithMultipleDependencies_InjectsCorrectly()
        {
            // Arrange
            var service1 = new TestService();
            var service2 = new AnotherTestService();
            _injector.Register(service1);
            _injector.Register(service2);
            
            // Create consumer without parameters using reflection
            var consumer = (MultiServiceConsumer)FormatterServices.GetUninitializedObject(typeof(MultiServiceConsumer));
            
            // Act
            _injector.InjectConstructor(consumer);
            
            // Assert
            Assert.IsNotNull(consumer.Service1);
            Assert.IsNotNull(consumer.Service2);
            Assert.That(consumer.Service1, Is.SameAs(service1));
            Assert.That(consumer.Service2, Is.SameAs(service2));
        }
        
        [Test]
        public void InjectConstructor_WithInheritance_InjectsCorrectly()
        {
            // Arrange
            var service = new DerivedTestService();
            _injector.Register(service);
            
            // Create consumer without parameters using reflection
            var consumer = (BaseServiceConsumer)FormatterServices.GetUninitializedObject(typeof(BaseServiceConsumer));
            
            // Act
            _injector.InjectConstructor(consumer);
            
            // Assert
            Assert.IsNotNull(consumer.Service);
            Assert.That(consumer.Service, Is.SameAs(service));
        }
        
        [Test]
        public void InjectConstructor_WithDefaultValue_UsesDefaultWhenNotRegistered()
        {
            // Arrange
            var service = new TestService();
            _injector.Register(service);
            
            // Create consumer without parameters using reflection
            var consumer = (ConsumerWithDefaultValue)FormatterServices.GetUninitializedObject(typeof(ConsumerWithDefaultValue));
            
            // Act
            _injector.InjectConstructor(consumer);
            
            // Assert
            Assert.IsNotNull(consumer.Service);
            Assert.That(consumer.OptionalValue, Is.EqualTo("default"));
        }
        
        [Test]
        public void InjectConstructor_NoMatchingConstructor_ThrowsException()
        {
            // Arrange
            var consumer = (UnresolvableConsumer)FormatterServices.GetUninitializedObject(typeof(UnresolvableConsumer));
            
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => _injector.InjectConstructor(consumer));
        }
        
        [Test]
        public void InjectConstructor_NullInstance_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _injector.InjectConstructor(null));
        }
        
        private class TestService { }
        
        private class AnotherTestService { }
        
        private class DerivedTestService : TestService { }
        
        private class ServiceConsumer
        {
            public TestService Service { get; private set; }
            
            public ServiceConsumer(TestService service)
            {
                Service = service;
            }
        }
        
        private class MultiServiceConsumer
        {
            public TestService Service1 { get; private set; }
            public AnotherTestService Service2 { get; private set; }
            
            public MultiServiceConsumer(TestService service1, AnotherTestService service2)
            {
                Service1 = service1;
                Service2 = service2;
            }
        }
        
        private class BaseServiceConsumer
        {
            public TestService Service { get; private set; }
            
            public BaseServiceConsumer(TestService service)
            {
                Service = service;
            }
        }
        
        private class ConsumerWithDefaultValue
        {
            public TestService Service { get; private set; }
            public string OptionalValue { get; private set; }
            
            public ConsumerWithDefaultValue(TestService service, string optionalValue = "default")
            {
                Service = service;
                OptionalValue = optionalValue;
            }
        }
        
        // Add a class for testing unresolvable constructor
        private class UnresolvableConsumer
        {
            public UnresolvableConsumer(UnregisteredService service)
            {}
        }
        
        // Add an unregistered service class
        private class UnregisteredService { }
    }
}