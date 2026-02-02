using CoreECS.Defines;

namespace CoreECS
{
    public static class EntityMatcherExtension
    {
        #region OfAll

        public static IAllOfEntityMatcher OfAll<T1, T2>(this IAllOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
        {
            return matcher.OfAll<T1>().OfAll<T2>();
        }
        
        public static IAllOfEntityMatcher OfAll<T1, T2, T3>(this IAllOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
        {
            return matcher.OfAll<T1>().OfAll<T2>().OfAll<T3>();
        }
        
        public static IAllOfEntityMatcher OfAll<T1, T2, T3, T4>(this IAllOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
        {
            return matcher.OfAll<T1>().OfAll<T2>().OfAll<T3>().OfAll<T4>();
        }
        
        public static IAllOfEntityMatcher OfAll<T1, T2, T3, T4, T5>(this IAllOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
        {
            return matcher.OfAll<T1>().OfAll<T2>().OfAll<T3>().OfAll<T4>().OfAll<T5>();
        }
        
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

        public static IAnyOfEntityMatcher OfAny<T1, T2>(this IAnyOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
        {
            return matcher.OfAny<T1>().OfAny<T2>();
        }
        
        public static IAnyOfEntityMatcher OfAny<T1, T2, T3>(this IAnyOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
        {
            return matcher.OfAny<T1>().OfAny<T2>().OfAny<T3>();
        }
        
        public static IAnyOfEntityMatcher OfAny<T1, T2, T3, T4>(this IAnyOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
        {
            return matcher.OfAny<T1>().OfAny<T2>().OfAny<T3>().OfAny<T4>();
        }
        
        public static IAnyOfEntityMatcher OfAny<T1, T2, T3, T4, T5>(this IAnyOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
        {
            return matcher.OfAny<T1>().OfAny<T2>().OfAny<T3>().OfAny<T4>().OfAny<T5>();
        }
        
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

        public static INoneOfEntityMatcher OfNone<T1, T2>(this INoneOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
        {
            return matcher.OfNone<T1>().OfNone<T2>();
        }
        
        public static INoneOfEntityMatcher OfNone<T1, T2, T3>(this INoneOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
        {
            return matcher.OfNone<T1>().OfNone<T2>().OfNone<T3>();
        }
        
        public static INoneOfEntityMatcher OfNone<T1, T2, T3, T4>(this INoneOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
        {
            return matcher.OfNone<T1>().OfNone<T2>().OfNone<T3>().OfNone<T4>();
        }
        
        public static INoneOfEntityMatcher OfNone<T1, T2, T3, T4, T5>(this INoneOfEntityMatcher matcher)
            where T1 : struct, IComponent<T1>
            where T2 : struct, IComponent<T2>
            where T3 : struct, IComponent<T3>
            where T4 : struct, IComponent<T4>
            where T5 : struct, IComponent<T5>
        {
            return matcher.OfNone<T1>().OfNone<T2>().OfNone<T3>().OfNone<T4>().OfNone<T5>();
        }
        
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
        
        public static IEntityCollector Build(this IEntityMatcher matcher,
            World world, EntityCollectorFlag flag = EntityCollectorFlag.Lazy)
        {
            return world.CreateCollector(matcher, flag);
        }
    }
}