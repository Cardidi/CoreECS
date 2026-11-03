using System;
using CoreECS.Defines;
using Microsoft.Extensions.DependencyInjection;

namespace CoreECS.Utils
{
    internal class BuiltinInjectionProxy : IInjectionProxy, IDisposable
    {
        private bool m_disposed;

        public IServiceProvider ServiceProvider { get; }

        public BuiltinInjectionProxy(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public object CreateObject(Type objectType)
        {
            return ActivatorUtilities.CreateInstance(ServiceProvider, objectType);
        }

        public T CreateObject<T>()
        {
            return ActivatorUtilities.CreateInstance<T>(ServiceProvider);
        }

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            if (ServiceProvider is IDisposable disposable)
                disposable.Dispose();
        }
    }

    internal class BuiltinInjectionProxyFactory : IInjectionProxyFactory
    {
        public static IInjectionProxyFactory Instance = new BuiltinInjectionProxyFactory();
        
        private sealed class ProxyWarper
        {
            public BuiltinInjectionProxy Proxy;
        }
        
        public IServiceCollection CreateServiceCollection()
        {
            return new ServiceCollection();
        }

        public IInjectionProxy CreateProxy(IServiceCollection collection)
        {
            var warper = new ProxyWarper();
            collection.AddSingleton(warper);
            collection.AddSingleton<IInjectionProxy>(p => p.GetService<ProxyWarper>()!.Proxy);
            
            return warper.Proxy = new BuiltinInjectionProxy(collection.BuildServiceProvider());
        }
    }
}
