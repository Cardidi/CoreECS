using CoreECS.Defines;

namespace CoreECS
{
    public static class EntityExtension
    {
        /// <summary>
        /// Tries to get component of type <typeparamref name="TComp"/> from entity.
        /// </summary>
        /// <param name="entity">Entity to get component from.</param>
        /// <param name="componentRef">Component reference.</param>
        /// <typeparam name="TComp">Component type.</typeparam>
        /// <returns>True if component exists, false otherwise.</returns>
        public static bool TryGetComponent<TComp>(this Entity entity, out ComponentRef<TComp> componentRef)
            where TComp : struct, IComponent<TComp>
        {
            if (entity.HasComponent<TComp>())
            {
                componentRef = entity.GetComponent<TComp>();
                return true;
            }
            
            componentRef = default;
            return false;
        }
        
        /// <summary>
        /// Tries to get component of type <typeparamref name="TComp"/> from entity.
        /// If component does not exist, it will be added to entity.
        /// </summary>
        /// <param name="entity">Entity to get component from.</param>
        /// <param name="componentRef">Component reference.</param>
        /// <typeparam name="TComp">Component type.</typeparam>
        /// <returns>True if component exists, false otherwise.</returns>
        public static bool GetOrCreateComponent<TComp>(this Entity entity, out ComponentRef<TComp> componentRef)
            where TComp : struct, IComponent<TComp>
        {
            if (entity.HasComponent<TComp>())
            {
                componentRef = entity.GetComponent<TComp>();
                return true;
            }
            
            componentRef = entity.CreateComponent<TComp>();
            return false;
        }
        
        /// <summary>
        /// Tries to get component of type <typeparamref name="TComp"/> from entity.
        /// If component does not exist, it will be added to entity with <paramref name="initialValue"/>.
        /// </summary>
        /// <param name="entity">Entity to get component from.</param>
        /// <param name="componentRef">Component reference.</param>
        /// <param name="initialValue">Initial value for component. (If possible)</param>
        /// <typeparam name="TComp">Component type.</typeparam>
        /// <returns>True if component exists, false otherwise.</returns>
        public static bool GetOrCreateComponent<TComp>(this Entity entity, out ComponentRef<TComp> componentRef, in TComp initialValue)
            where TComp : struct, IComponent<TComp>
        {
            if (entity.HasComponent<TComp>())
            {
                componentRef = entity.GetComponent<TComp>();
                return true;
            }
            
            componentRef = entity.CreateComponent(initialValue);
            return false;
        }
    }
}