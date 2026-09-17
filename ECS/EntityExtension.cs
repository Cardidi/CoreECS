using CoreECS.Defines;

namespace CoreECS
{
    /// <summary>
    /// Entity component access helpers built on the public Entity API.
    /// Tag components carry no refs: TryGetComponent returns true with a default ref
    /// for a present tag, and GetOrCreateComponent adds the tag then returns a default ref.
    /// </summary>
    public static class EntityExtension
    {
        /// <summary>Tries to get the component of type <typeparamref name="TComp"/>.</summary>
        /// <param name="entity">Entity to get the component from.</param>
        /// <param name="componentRef">Component reference; default when absent.</param>
        /// <returns>True when the component exists.</returns>
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

        /// <summary>Gets the component of type <typeparamref name="TComp"/>, creating a default one when absent.</summary>
        /// <param name="entity">Entity to get the component from.</param>
        /// <param name="componentRef">Component reference.</param>
        /// <returns>True when the component already existed.</returns>
        public static bool GetOrCreateComponent<TComp>(this Entity entity, out ComponentRef<TComp> componentRef)
            where TComp : struct, IComponent<TComp>
        {
            if (entity.TryGetComponent(out componentRef)) return true;

            componentRef = entity.CreateComponent<TComp>();
            return false;
        }

        /// <summary>Gets the component of type <typeparamref name="TComp"/>, creating one with <paramref name="initialValue"/> when absent.</summary>
        /// <param name="entity">Entity to get the component from.</param>
        /// <param name="componentRef">Component reference.</param>
        /// <param name="initialValue">Initial value used when the component is created.</param>
        /// <returns>True when the component already existed.</returns>
        public static bool GetOrCreateComponent<TComp>(
            this Entity entity, out ComponentRef<TComp> componentRef, in TComp initialValue)
            where TComp : struct, IComponent<TComp>
        {
            if (entity.TryGetComponent(out componentRef)) return true;

            componentRef = entity.CreateComponent(initialValue);
            return false;
        }
    }
}
