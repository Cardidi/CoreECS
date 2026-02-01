using System;
using System.Collections.Generic;
using System.Reflection;

namespace CoreECS.Utils
{
    /// <summary>
    /// Simple constructor-based dependency injector.
    /// This class provides basic dependency injection functionality by resolving constructor parameters
    /// from registered instances.
    /// </summary>
    public sealed class Injector
    {
        /// <summary>
        /// List of registered instances available for injection.
        /// </summary>
        private readonly List<object> m_instances = new();
        
        /// <summary>
        /// Gets a read-only view of the registered instances.
        /// </summary>
        public IReadOnlyList<object> Instances => m_instances;
        
        /// <summary>
        /// Registers an instance for dependency injection by type.
        /// </summary>
        /// <param name="instance">The instance to register</param>
        public void Register(object instance)
        {
            Assertion.ArgumentNotNull(instance);
            m_instances.Add(instance);
        }
        
        /// <summary>
        /// Injects dependencies into the provided instance by calling its constructor.
        /// </summary>
        /// <param name="instance">The instance to inject dependencies into</param>
        public void InjectConstructor(object instance)
        {
            Assertion.ArgumentNotNull(instance);
            
            Type type = instance.GetType();
            
            // Get all public constructors
            ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            
            if (constructors.Length == 0)
            {
                throw new InvalidOperationException($"No public constructors found for type {type.Name}.");
            }
            
            // Sort constructors by parameter count from most to least
            Array.Sort(constructors, (a, b) => b.GetParameters().Length - a.GetParameters().Length);

            using (ListPool<object>.Get(out var resolvedParameters))
            {
                ConstructorInfo injectConstructor = null;
                
                // Try each constructor in order of parameter count (most to least)
                foreach (var constructor in constructors)
                {
                    try
                    {
                        ParameterInfo[] parameters = constructor.GetParameters();
                        resolvedParameters.Clear();

                        // Try to resolve all parameters
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var p = parameters[i];
                            if (!_resolveParameter(parameters[i], out var resolved))
                            {
                                if (!p.HasDefaultValue) break;
                                resolved = p.DefaultValue;
                            }
                            
                            resolvedParameters.Add(resolved);
                        }
                        
                        if (parameters.Length != resolvedParameters.Count) continue;

                        // If we got here, all parameters can be resolved
                        injectConstructor = constructor;
                        break;
                    }
                    catch
                    {
                        // Ignore constructors that we can't resolve parameters for
                    }
                }

                if (injectConstructor == null)
                {
                    throw new InvalidOperationException(
                        $"No suitable constructor found for type {type.Name} with resolvable parameters.");
                }

                // Call the constructor to inject dependencies
                injectConstructor.Invoke(instance, resolvedParameters.ToArray());
            }
        }
        
        /// <summary>
        /// Resolves a parameter by its type from the registered instances.
        /// </summary>
        /// <param name="parameter">The parameter information to resolve</param>
        /// <param name="resolved">The resolved instance</param>
        /// <returns>True if the parameter was successfully resolved, false otherwise</returns>
        private bool _resolveParameter(ParameterInfo parameter, out object resolved)
        {
            Type parameterType = parameter.ParameterType;
            
            // Try to find the best matching instance
            foreach (var instance in m_instances)
            {
                Type instanceType = instance.GetType();
                
                // Exact type match is the best
                if (instanceType == parameterType)
                {
                    resolved = instance;
                    return true;
                }
                
                // Check if the instance is assignable to the parameter type
                if (parameterType.IsAssignableFrom(instanceType))
                {
                    resolved = instance;
                    return true;
                }
            }
            
            resolved = null;
            return false;
        }
    }
}