using CoreECS.Defines;
using CoreECS.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace CoreECS.Test
{
    [TestFixture]
    public class BuiltinInjectionProxyTestUnit
    {
        private interface ITestService { }
        private class TestServiceImpl : ITestService { }

        private class NoDependencyType
        {
            public int Value = 42;
        }

        private class TypeWithServiceDependency
        {
            public ITestService Service { get; }
            public TypeWithServiceDependency(ITestService service)
            {
                Service = service;
            }
        }

        private class TypeWithMultipleDeps
        {
            public ITestService Service { get; }
            public NoDependencyType Other { get; }
            public TypeWithMultipleDeps(ITestService service, NoDependencyType other)
            {
                Service = service;
                Other = other;
            }
        }

        private class UnresolvableType
        {
            public UnresolvableType(string unregistered) { }
        }

        [Test]
        public void Constructor_StoresServiceProvider()
        {
            var sp = new ServiceCollection().BuildServiceProvider();
            var proxy = new BuiltinInjectionProxy(sp);

            Assert.That(proxy.ServiceProvider, Is.SameAs(sp));
        }

        [Test]
        public void CreateObject_SimpleType_ReturnsInstance()
        {
            var sp = new ServiceCollection().BuildServiceProvider();
            var proxy = new BuiltinInjectionProxy(sp);

            var result = proxy.CreateObject(typeof(NoDependencyType));

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<NoDependencyType>());
            Assert.That(((NoDependencyType)result).Value, Is.EqualTo(42));
        }

        [Test]
        public void CreateObject_ResolvesRegisteredDependency()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITestService, TestServiceImpl>();
            var sp = services.BuildServiceProvider();
            var proxy = new BuiltinInjectionProxy(sp);

            var result = proxy.CreateObject(typeof(TypeWithServiceDependency));

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<TypeWithServiceDependency>());
            Assert.That(((TypeWithServiceDependency)result).Service, Is.InstanceOf<TestServiceImpl>());
        }

        [Test]
        public void CreateObject_ResolvesMultipleDependencies()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITestService, TestServiceImpl>();
            services.AddSingleton<NoDependencyType>();
            var sp = services.BuildServiceProvider();
            var proxy = new BuiltinInjectionProxy(sp);

            var result = proxy.CreateObject(typeof(TypeWithMultipleDeps));

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<TypeWithMultipleDeps>());
            var obj = (TypeWithMultipleDeps)result;
            Assert.That(obj.Service, Is.InstanceOf<TestServiceImpl>());
            Assert.That(obj.Other, Is.Not.Null);
        }

        [Test]
        public void CreateObject_Generic_SimpleType_ReturnsInstance()
        {
            var sp = new ServiceCollection().BuildServiceProvider();
            var proxy = new BuiltinInjectionProxy(sp);

            var result = proxy.CreateObject<NoDependencyType>();

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<NoDependencyType>());
            Assert.That(result.Value, Is.EqualTo(42));
        }

        [Test]
        public void CreateObject_Generic_ResolvesRegisteredDependency()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITestService, TestServiceImpl>();
            var sp = services.BuildServiceProvider();
            var proxy = new BuiltinInjectionProxy(sp);

            var result = proxy.CreateObject<TypeWithServiceDependency>();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Service, Is.Not.Null);
            Assert.That(result.Service, Is.InstanceOf<TestServiceImpl>());
        }

        [Test]
        public void CreateObject_NullType_Throws()
        {
            var sp = new ServiceCollection().BuildServiceProvider();
            var proxy = new BuiltinInjectionProxy(sp);

            Assert.Throws<NullReferenceException>(() => proxy.CreateObject((Type)null!));
        }

        [Test]
        public void CreateObject_UnresolvableDependency_Throws()
        {
            var sp = new ServiceCollection().BuildServiceProvider();
            var proxy = new BuiltinInjectionProxy(sp);

            Assert.Throws<InvalidOperationException>(() => proxy.CreateObject(typeof(UnresolvableType)));
        }

        [Test]
        public void Factory_CreateServiceCollection_ReturnsServiceCollection()
        {
            var collection = BuiltinInjectionProxyFactory.Instance.CreateServiceCollection();

            Assert.That(collection, Is.Not.Null);
            Assert.That(collection, Is.InstanceOf<ServiceCollection>());
        }

        [Test]
        public void Factory_CreateProxy_ReturnsProxy()
        {
            var collection = BuiltinInjectionProxyFactory.Instance.CreateServiceCollection();
            var proxy = BuiltinInjectionProxyFactory.Instance.CreateProxy(collection);

            Assert.That(proxy, Is.Not.Null);
            Assert.That(proxy, Is.InstanceOf<BuiltinInjectionProxy>());
        }

        [Test]
        public void Factory_CreateProxy_ProxyResolvableFromServiceProvider()
        {
            var factory = BuiltinInjectionProxyFactory.Instance;
            var collection = factory.CreateServiceCollection();
            var proxy = factory.CreateProxy(collection);

            var resolved = proxy.ServiceProvider.GetService<IInjectionProxy>();

            Assert.That(resolved, Is.Not.Null);
        }

        [Test]
        public void Factory_CreateProxy_SameInstanceAsResolvedFromDI()
        {
            var factory = BuiltinInjectionProxyFactory.Instance;
            var collection = factory.CreateServiceCollection();
            var proxy = factory.CreateProxy(collection);

            var resolved = proxy.ServiceProvider.GetService(typeof(IInjectionProxy));

            Assert.That(resolved, Is.SameAs(proxy));
        }

        [Test]
        public void Factory_CreateProxy_PreservesRegisteredServices()
        {
            var factory = BuiltinInjectionProxyFactory.Instance;
            var collection = factory.CreateServiceCollection();
            collection.AddSingleton<ITestService, TestServiceImpl>();
            var proxy = factory.CreateProxy(collection);

            var resolved = proxy.ServiceProvider.GetService<ITestService>();

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved, Is.InstanceOf<TestServiceImpl>());
        }

        [Test]
        public void Factory_EndToEnd_CreateObjectFromFactoryCreatedProxy()
        {
            var factory = BuiltinInjectionProxyFactory.Instance;
            var collection = factory.CreateServiceCollection();
            collection.AddSingleton<ITestService, TestServiceImpl>();
            var proxy = factory.CreateProxy(collection);

            var result = proxy.CreateObject<TypeWithServiceDependency>();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Service, Is.InstanceOf<TestServiceImpl>());
        }

        [Test]
        public void Factory_EndToEnd_NonGenericCreateObjectFromFactoryCreatedProxy()
        {
            var factory = BuiltinInjectionProxyFactory.Instance;
            var collection = factory.CreateServiceCollection();
            collection.AddSingleton<ITestService, TestServiceImpl>();
            var proxy = factory.CreateProxy(collection);

            var result = proxy.CreateObject(typeof(TypeWithServiceDependency));

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<TypeWithServiceDependency>());
            Assert.That(((TypeWithServiceDependency)result).Service, Is.InstanceOf<TestServiceImpl>());
        }

        [Test]
        public void Proxy_DisposeDisposesOwnedProviderAndServicesOnce()
        {
            var resource = new DisposableResource();
            var collection = BuiltinInjectionProxyFactory.Instance.CreateServiceCollection();
            collection.AddSingleton(_ => resource);
            IInjectionProxy proxy = BuiltinInjectionProxyFactory.Instance.CreateProxy(collection);
            _ = proxy.ServiceProvider.GetRequiredService<DisposableResource>();

            Assert.That(proxy, Is.InstanceOf<IDisposable>());
            ((IDisposable)proxy).Dispose();
            ((IDisposable)proxy).Dispose();

            Assert.That(resource.DisposeCount, Is.EqualTo(1));
        }

        private sealed class DisposableResource : IDisposable
        {
            internal int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
