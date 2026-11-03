using System;
using System.Collections.Generic;

namespace CoreECS.Utils
{
    /// <summary>
    /// A readonly struct that represents an object with an associated order value for sorting purposes.
    /// This struct is used to maintain priority-based ordering in collections, particularly for signal handlers.
    /// </summary>
    /// <typeparam name="T">The type of the element being wrapped</typeparam>
    public readonly struct SortedObject<T> : IComparable<SortedObject<T>>, IEquatable<SortedObject<T>>
    {
        /// <summary>
        /// Finds the index of a specific element in a list of SortedObject items.
        /// </summary>
        /// <param name="c">The list to search in</param>
        /// <param name="ele">The element to find</param>
        /// <returns>The index of the element if found, otherwise -1</returns>
        public static int IndexOfElement(IReadOnlyList<SortedObject<T>> c, in T ele)
        {
            for (var i = 0; i < c.Count; i++)
            {
                if (c[i].Element.Equals(ele)) return i;
            }

            return -1;
        }
        
        /// <summary>
        /// The order value used for sorting. Lower values have higher priority.
        /// </summary>
        public readonly int Order;

        /// <summary>
        /// The element being wrapped.
        /// </summary>
        public readonly T Element;

        /// <summary>
        /// Initializes a new instance of the SortedObject struct.
        /// </summary>
        /// <param name="order">The order value for sorting</param>
        /// <param name="element">The element to wrap</param>
        public SortedObject(int order, T element)
        {
            Order = order;
            Element = element;
        }

        /// <summary>
        /// Compares this instance with another SortedObject based on their order values.
        /// </summary>
        /// <param name="other">The other SortedObject to compare with</param>
        /// <returns>A negative value if this instance has lower order (higher priority),
        /// zero if both have the same order, or a positive value if this instance has higher order</returns>
        public int CompareTo(SortedObject<T> other)
        {
            return Order.CompareTo(other.Order);
        }

        /// <summary>
        /// Determines whether this instance is equal to another SortedObject.
        /// </summary>
        /// <param name="other">The other SortedObject to compare with</param>
        /// <returns>True if both instances have the same order and element, otherwise false</returns>
        public bool Equals(SortedObject<T> other)
        {
            return Order == other.Order && EqualityComparer<T>.Default.Equals(Element, other.Element);
        }

        /// <summary>
        /// Determines whether this instance is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare with</param>
        /// <returns>True if the object is a SortedObject with the same order and element, otherwise false</returns>
        public override bool Equals(object obj)
        {
            return obj is SortedObject<T> other && Equals(other);
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>A hash code based on both the order and element values</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Order * 397) ^ EqualityComparer<T>.Default.GetHashCode(Element);
            }
        }
    }
}