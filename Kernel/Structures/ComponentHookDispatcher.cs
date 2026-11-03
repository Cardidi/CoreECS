using System;
using CoreECS.Defines;
using CoreECS.Utils;

namespace CoreECS.Structures
{
    internal static class ComponentHookDispatcher<TComponent>
        where TComponent : struct, IComponent<TComponent>
    {
        private static readonly ComponentKind s_kind =
            ComponentTypeRegistry.GetOrRegister<TComponent>().Kind;

        internal static void InvokeCreate(ref TComponent component, EntityId entityId)
        {
            if (s_kind == ComponentKind.Tag)
            {
                return;
            }

            try
            {
                component.OnCreate(entityId);
            }
            catch (Exception exception)
            {
                Log.Exp(exception);
            }
        }

        internal static void InvokeDestroy(ref TComponent component, EntityId entityId)
        {
            if (s_kind == ComponentKind.Tag)
            {
                return;
            }

            try
            {
                component.OnDestroy(entityId);
            }
            catch (Exception exception)
            {
                Log.Exp(exception);
            }
        }
    }
}
