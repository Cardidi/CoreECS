using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS
{
    /// <summary>
    /// Provides extension methods for composing entity matchers with multiple component types and creating collectors.
    /// </summary>
    public static class EntityMatcherExtension
    {
        #region OfAll

        /// <summary>
        /// Requires entities to have all of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First required component type.</typeparam>
        /// <typeparam name="T2">Second required component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAllOfEntityMatcher OfAll<T1, T2>(this IAllOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
        {
            return matcher.OfAll<T1>().OfAll<T2>();
        }
        
        /// <summary>
        /// Requires entities to have all of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First required component type.</typeparam>
        /// <typeparam name="T2">Second required component type.</typeparam>
        /// <typeparam name="T3">Third required component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAllOfEntityMatcher OfAll<T1, T2, T3>(this IAllOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
        {
            return matcher.OfAll<T1>().OfAll<T2>().OfAll<T3>();
        }
        
        /// <summary>
        /// Requires entities to have all of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First required component type.</typeparam>
        /// <typeparam name="T2">Second required component type.</typeparam>
        /// <typeparam name="T3">Third required component type.</typeparam>
        /// <typeparam name="T4">Fourth required component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAllOfEntityMatcher OfAll<T1, T2, T3, T4>(this IAllOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
        {
            return matcher.OfAll<T1>().OfAll<T2>().OfAll<T3>().OfAll<T4>();
        }
        
        /// <summary>
        /// Requires entities to have all of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First required component type.</typeparam>
        /// <typeparam name="T2">Second required component type.</typeparam>
        /// <typeparam name="T3">Third required component type.</typeparam>
        /// <typeparam name="T4">Fourth required component type.</typeparam>
        /// <typeparam name="T5">Fifth required component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAllOfEntityMatcher OfAll<T1, T2, T3, T4, T5>(this IAllOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
        {
            return matcher.OfAll<T1>().OfAll<T2>().OfAll<T3>().OfAll<T4>().OfAll<T5>();
        }
        
        /// <summary>
        /// Requires entities to have all of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First required component type.</typeparam>
        /// <typeparam name="T2">Second required component type.</typeparam>
        /// <typeparam name="T3">Third required component type.</typeparam>
        /// <typeparam name="T4">Fourth required component type.</typeparam>
        /// <typeparam name="T5">Fifth required component type.</typeparam>
        /// <typeparam name="T6">Sixth required component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAllOfEntityMatcher OfAll<T1, T2, T3, T4, T5, T6>(this IAllOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
            where T6 : struct, IComponent<T6>
        {
            return matcher.OfAll<T1>().OfAll<T2>().OfAll<T3>().OfAll<T4>().OfAll<T5>().OfAll<T6>();
        }
        
        /// <summary>
        /// Requires entities to have all of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First required component type.</typeparam>
        /// <typeparam name="T2">Second required component type.</typeparam>
        /// <typeparam name="T3">Third required component type.</typeparam>
        /// <typeparam name="T4">Fourth required component type.</typeparam>
        /// <typeparam name="T5">Fifth required component type.</typeparam>
        /// <typeparam name="T6">Sixth required component type.</typeparam>
        /// <typeparam name="T7">Seventh required component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAllOfEntityMatcher OfAll<T1, T2, T3, T4, T5, T6, T7>(this IAllOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
            where T6 : struct, IComponent<T6>
            where T7 : struct, IComponent<T7>
        {
            return matcher.OfAll<T1>().OfAll<T2>().OfAll<T3>().OfAll<T4>().OfAll<T5>().OfAll<T6>().OfAll<T7>();
        }
        
        /// <summary>
        /// Requires entities to have all of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First required component type.</typeparam>
        /// <typeparam name="T2">Second required component type.</typeparam>
        /// <typeparam name="T3">Third required component type.</typeparam>
        /// <typeparam name="T4">Fourth required component type.</typeparam>
        /// <typeparam name="T5">Fifth required component type.</typeparam>
        /// <typeparam name="T6">Sixth required component type.</typeparam>
        /// <typeparam name="T7">Seventh required component type.</typeparam>
        /// <typeparam name="T8">Eighth required component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAllOfEntityMatcher OfAll<T1, T2, T3, T4, T5, T6, T7, T8>(this IAllOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
            where T6 : struct, IComponent<T6>
            where T7 : struct, IComponent<T7>
            where T8 : struct, IComponent<T8>
        {
            return matcher.OfAll<T1>().OfAll<T2>().OfAll<T3>().OfAll<T4>().OfAll<T5>().OfAll<T6>().OfAll<T7>().OfAll<T8>();
        }

        #endregion

        #region OfAny

        /// <summary>
        /// Includes entities that have at least one of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First accepted component type.</typeparam>
        /// <typeparam name="T2">Second accepted component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAnyOfEntityMatcher OfAny<T1, T2>(this IAnyOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
        {
            return matcher.OfAny<T1>().OfAny<T2>();
        }
        
        /// <summary>
        /// Includes entities that have at least one of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First accepted component type.</typeparam>
        /// <typeparam name="T2">Second accepted component type.</typeparam>
        /// <typeparam name="T3">Third accepted component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAnyOfEntityMatcher OfAny<T1, T2, T3>(this IAnyOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
        {
            return matcher.OfAny<T1>().OfAny<T2>().OfAny<T3>();
        }
        
        /// <summary>
        /// Includes entities that have at least one of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First accepted component type.</typeparam>
        /// <typeparam name="T2">Second accepted component type.</typeparam>
        /// <typeparam name="T3">Third accepted component type.</typeparam>
        /// <typeparam name="T4">Fourth accepted component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAnyOfEntityMatcher OfAny<T1, T2, T3, T4>(this IAnyOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
        {
            return matcher.OfAny<T1>().OfAny<T2>().OfAny<T3>().OfAny<T4>();
        }
        
        /// <summary>
        /// Includes entities that have at least one of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First accepted component type.</typeparam>
        /// <typeparam name="T2">Second accepted component type.</typeparam>
        /// <typeparam name="T3">Third accepted component type.</typeparam>
        /// <typeparam name="T4">Fourth accepted component type.</typeparam>
        /// <typeparam name="T5">Fifth accepted component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAnyOfEntityMatcher OfAny<T1, T2, T3, T4, T5>(this IAnyOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
        {
            return matcher.OfAny<T1>().OfAny<T2>().OfAny<T3>().OfAny<T4>().OfAny<T5>();
        }
        
        /// <summary>
        /// Includes entities that have at least one of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First accepted component type.</typeparam>
        /// <typeparam name="T2">Second accepted component type.</typeparam>
        /// <typeparam name="T3">Third accepted component type.</typeparam>
        /// <typeparam name="T4">Fourth accepted component type.</typeparam>
        /// <typeparam name="T5">Fifth accepted component type.</typeparam>
        /// <typeparam name="T6">Sixth accepted component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAnyOfEntityMatcher OfAny<T1, T2, T3, T4, T5, T6>(this IAnyOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
            where T6 : struct, IComponent<T6>
        {
            return matcher.OfAny<T1>().OfAny<T2>().OfAny<T3>().OfAny<T4>().OfAny<T5>().OfAny<T6>();
        }
        
        /// <summary>
        /// Includes entities that have at least one of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First accepted component type.</typeparam>
        /// <typeparam name="T2">Second accepted component type.</typeparam>
        /// <typeparam name="T3">Third accepted component type.</typeparam>
        /// <typeparam name="T4">Fourth accepted component type.</typeparam>
        /// <typeparam name="T5">Fifth accepted component type.</typeparam>
        /// <typeparam name="T6">Sixth accepted component type.</typeparam>
        /// <typeparam name="T7">Seventh accepted component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAnyOfEntityMatcher OfAny<T1, T2, T3, T4, T5, T6, T7>(this IAnyOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
            where T6 : struct, IComponent<T6>
            where T7 : struct, IComponent<T7>
        {
            return matcher.OfAny<T1>().OfAny<T2>().OfAny<T3>().OfAny<T4>().OfAny<T5>().OfAny<T6>().OfAny<T7>();
        }
        
        /// <summary>
        /// Includes entities that have at least one of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First accepted component type.</typeparam>
        /// <typeparam name="T2">Second accepted component type.</typeparam>
        /// <typeparam name="T3">Third accepted component type.</typeparam>
        /// <typeparam name="T4">Fourth accepted component type.</typeparam>
        /// <typeparam name="T5">Fifth accepted component type.</typeparam>
        /// <typeparam name="T6">Sixth accepted component type.</typeparam>
        /// <typeparam name="T7">Seventh accepted component type.</typeparam>
        /// <typeparam name="T8">Eighth accepted component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static IAnyOfEntityMatcher OfAny<T1, T2, T3, T4, T5, T6, T7, T8>(this IAnyOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
            where T6 : struct, IComponent<T6>
            where T7 : struct, IComponent<T7>
            where T8 : struct, IComponent<T8>
        {
            return matcher.OfAny<T1>().OfAny<T2>().OfAny<T3>().OfAny<T4>().OfAny<T5>().OfAny<T6>().OfAny<T7>().OfAny<T8>();
        }

        #endregion

        #region OfNone

        /// <summary>
        /// Excludes entities that have any of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First excluded component type.</typeparam>
        /// <typeparam name="T2">Second excluded component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static INoneOfEntityMatcher OfNone<T1, T2>(this INoneOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
        {
            return matcher.OfNone<T1>().OfNone<T2>();
        }
        
        /// <summary>
        /// Excludes entities that have any of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First excluded component type.</typeparam>
        /// <typeparam name="T2">Second excluded component type.</typeparam>
        /// <typeparam name="T3">Third excluded component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static INoneOfEntityMatcher OfNone<T1, T2, T3>(this INoneOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
        {
            return matcher.OfNone<T1>().OfNone<T2>().OfNone<T3>();
        }
        
        /// <summary>
        /// Excludes entities that have any of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First excluded component type.</typeparam>
        /// <typeparam name="T2">Second excluded component type.</typeparam>
        /// <typeparam name="T3">Third excluded component type.</typeparam>
        /// <typeparam name="T4">Fourth excluded component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static INoneOfEntityMatcher OfNone<T1, T2, T3, T4>(this INoneOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
        {
            return matcher.OfNone<T1>().OfNone<T2>().OfNone<T3>().OfNone<T4>();
        }
        
        /// <summary>
        /// Excludes entities that have any of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First excluded component type.</typeparam>
        /// <typeparam name="T2">Second excluded component type.</typeparam>
        /// <typeparam name="T3">Third excluded component type.</typeparam>
        /// <typeparam name="T4">Fourth excluded component type.</typeparam>
        /// <typeparam name="T5">Fifth excluded component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static INoneOfEntityMatcher OfNone<T1, T2, T3, T4, T5>(this INoneOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
        {
            return matcher.OfNone<T1>().OfNone<T2>().OfNone<T3>().OfNone<T4>().OfNone<T5>();
        }
        
        /// <summary>
        /// Excludes entities that have any of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First excluded component type.</typeparam>
        /// <typeparam name="T2">Second excluded component type.</typeparam>
        /// <typeparam name="T3">Third excluded component type.</typeparam>
        /// <typeparam name="T4">Fourth excluded component type.</typeparam>
        /// <typeparam name="T5">Fifth excluded component type.</typeparam>
        /// <typeparam name="T6">Sixth excluded component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static INoneOfEntityMatcher OfNone<T1, T2, T3, T4, T5, T6>(this INoneOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
            where T6 : struct, IComponent<T6>
        {
            return matcher.OfNone<T1>().OfNone<T2>().OfNone<T3>().OfNone<T4>().OfNone<T5>().OfNone<T6>();
        }
        
        /// <summary>
        /// Excludes entities that have any of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First excluded component type.</typeparam>
        /// <typeparam name="T2">Second excluded component type.</typeparam>
        /// <typeparam name="T3">Third excluded component type.</typeparam>
        /// <typeparam name="T4">Fourth excluded component type.</typeparam>
        /// <typeparam name="T5">Fifth excluded component type.</typeparam>
        /// <typeparam name="T6">Sixth excluded component type.</typeparam>
        /// <typeparam name="T7">Seventh excluded component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static INoneOfEntityMatcher OfNone<T1, T2, T3, T4, T5, T6, T7>(this INoneOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
            where T6 : struct, IComponent<T6>
            where T7 : struct, IComponent<T7>
        {
            return matcher.OfNone<T1>().OfNone<T2>().OfNone<T3>().OfNone<T4>().OfNone<T5>().OfNone<T6>().OfNone<T7>();
        }
        
        /// <summary>
        /// Excludes entities that have any of the specified component types.
        /// </summary>
        /// <param name="matcher">Matcher to extend.</param>
        /// <typeparam name="T1">First excluded component type.</typeparam>
        /// <typeparam name="T2">Second excluded component type.</typeparam>
        /// <typeparam name="T3">Third excluded component type.</typeparam>
        /// <typeparam name="T4">Fourth excluded component type.</typeparam>
        /// <typeparam name="T5">Fifth excluded component type.</typeparam>
        /// <typeparam name="T6">Sixth excluded component type.</typeparam>
        /// <typeparam name="T7">Seventh excluded component type.</typeparam>
        /// <typeparam name="T8">Eighth excluded component type.</typeparam>
        /// <returns>This matcher instance for method chaining.</returns>
        public static INoneOfEntityMatcher OfNone<T1, T2, T3, T4, T5, T6, T7, T8>(this INoneOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
            where T6 : struct, IComponent<T6>
            where T7 : struct, IComponent<T7>
            where T8 : struct, IComponent<T8>
        {
            return matcher.OfNone<T1>().OfNone<T2>().OfNone<T3>().OfNone<T4>().OfNone<T5>().OfNone<T6>().OfNone<T7>().OfNone<T8>();
        }

        #endregion
        
        /// <summary>
        /// Creates an entity collector from the matcher in the specified world.
        /// </summary>
        /// <param name="matcher">Matcher used to filter collected entities.</param>
        /// <param name="world">World that owns the collector and entity managers.</param>
        /// <param name="flag">Flags controlling which entity changes are tracked by the collector.</param>
        /// <returns>A collector that tracks entities matching the matcher.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when the world is not ready or its entity match manager is unavailable.</exception>
        public static IEntityCollector Build(this IEntityMatcher matcher,
            World world, EntityCollectorFlag flag = EntityCollectorFlag.Default)
        {
            return world.CreateCollector(matcher, flag);
        }

        /// <summary>
        /// Appends IDs of entities that match <paramref name="matcher"/> to <paramref name="result"/>.
        /// Existing items in <paramref name="result"/> are preserved.
        /// </summary>
        /// <param name="matcher">Matcher used to filter entities.</param>
        /// <param name="world">World that owns queried entities.</param>
        /// <param name="result">Target non-alloc output collection.</param>
        /// <returns>The number of IDs appended.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when the world is not ready or managers are unavailable.</exception>
        /// <exception cref="System.ArgumentNullException">Thrown when matcher, world, or result is null.</exception>
        public static int Query(this IEntityMatcher matcher, World world, ICollection<ulong> result)
        {
            CoreECS.Utils.Assertion.ArgumentNotNull(world, nameof(world));
            return world.Query(matcher, result);
        }

        /// <summary>
        /// Appends entities that match <paramref name="matcher"/> to <paramref name="result"/>.
        /// Existing items in <paramref name="result"/> are preserved.
        /// </summary>
        /// <param name="matcher">Matcher used to filter entities.</param>
        /// <param name="world">World that owns queried entities.</param>
        /// <param name="result">Target non-alloc output collection.</param>
        /// <returns>The number of entities appended.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when the world is not ready or managers are unavailable.</exception>
        /// <exception cref="System.ArgumentNullException">Thrown when matcher, world, or result is null.</exception>
        public static int Query(this IEntityMatcher matcher, World world, ICollection<Entity> result)
        {
            CoreECS.Utils.Assertion.ArgumentNotNull(world, nameof(world));
            return world.Query(matcher, result);
        }
    }
}