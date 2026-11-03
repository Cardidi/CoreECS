using System;
using System.Runtime.CompilerServices;

namespace CoreECS.Utils
{
    /// <summary>
    /// Provides assertion methods for validating conditions and parameters.
    /// </summary>
    public static class Assertion
    {
        /// <summary>
        /// Asserts that a condition is true.
        /// </summary>
        /// <param name="condition">The condition to evaluate</param>
        /// <param name="message">Optional error message</param>
        /// <param name="memberName">The name of the calling member (automatically populated)</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is false</exception>
        public static void IsTrue(bool condition, string message = null, [CallerMemberName] string memberName = null)
        {
            if (!condition)
            {
                string errorMessage = message ?? memberName;
                throw new InvalidOperationException(errorMessage);
            }
        }

        /// <summary>
        /// Asserts that a condition is false.
        /// </summary>
        /// <param name="condition">The condition to evaluate</param>
        /// <param name="message">Optional error message</param>
        /// <param name="memberName">The name of the calling member (automatically populated)</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is true</exception>
        public static void IsFalse(bool condition, string message = null, [CallerMemberName] string memberName = null)
        {
            if (condition)
            {
                string errorMessage = message ?? memberName;
                throw new InvalidOperationException(errorMessage);
            }
        }

        /// <summary>
        /// Asserts that an object is null.
        /// </summary>
        /// <param name="obj">The object to check</param>
        /// <param name="message">Optional error message</param>
        /// <param name="memberName">The name of the calling member (automatically populated)</param>
        /// <exception cref="InvalidOperationException">Thrown when the object is not null</exception>
        public static void IsNull(object obj, string message = null, [CallerMemberName] string memberName = null)
        {
            if (obj != null)
            {
                string errorMessage = message ?? memberName;
                throw new InvalidOperationException(errorMessage);
            }
        }

        /// <summary>
        /// Asserts that an object is not null.
        /// </summary>
        /// <param name="obj">The object to check</param>
        /// <param name="message">Optional error message</param>
        /// <param name="memberName">The name of the calling member (automatically populated)</param>
        /// <exception cref="InvalidOperationException">Thrown when the object is null</exception>
        public static void IsNotNull(object obj, string message = null, [CallerMemberName] string memberName = null)
        {
            if (obj == null)
            {
                string errorMessage = message ?? memberName;
                throw new InvalidOperationException(errorMessage);
            }
        }
        
        /// <summary>
        /// Asserts that an argument is not null.
        /// </summary>
        /// <param name="obj">The argument to check</param>
        /// <param name="message">Optional error message</param>
        /// <param name="memberName">The name of the calling member (automatically populated)</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null</exception>
        public static void ArgumentNotNull(object obj, string message = null, [CallerMemberName] string memberName = null)
        {
            if (obj == null)
            {
                string errorMessage = message?? memberName;
                throw new ArgumentNullException(errorMessage);
            }
        }

        /// <summary>
        /// Asserts that a type is assignable to a parent type.
        /// </summary>
        /// <typeparam name="TParent">The parent type</typeparam>
        /// <param name="children">The child type to check</param>
        /// <param name="message">Optional error message</param>
        /// <param name="memberName">The name of the calling member (automatically populated)</param>
        /// <exception cref="InvalidOperationException">Thrown when the child type is not assignable to the parent type</exception>
        public static void IsParentTypeTo<TParent>(Type children, string message = null, [CallerMemberName] string memberName = null)
        {
            IsTrue(typeof(TParent).IsAssignableFrom(children), message, memberName);
        }
        
        /// <summary>
        /// Asserts that a type is not assignable to a parent type.
        /// </summary>
        /// <typeparam name="TParent">The parent type</typeparam>
        /// <param name="children">The child type to check</param>
        /// <param name="message">Optional error message</param>
        /// <param name="memberName">The name of the calling member (automatically populated)</param>
        /// <exception cref="InvalidOperationException">Thrown when the child type is assignable to the parent type</exception>
        public static void IsNotParentTypeTo<TParent>(Type children, string message = null, [CallerMemberName] string memberName = null)
        {
            IsTrue(!typeof(TParent).IsAssignableFrom(children), message, memberName);
        }
        
        /// <summary>
        /// Asserts that two values are equal.
        /// </summary>
        /// <typeparam name="T">The type of the values to compare</typeparam>
        /// <param name="expected">The expected value</param>
        /// <param name="actual">The actual value</param>
        /// <param name="message">Optional error message</param>
        /// <param name="memberName">The name of the calling member (automatically populated)</param>
        /// <exception cref="InvalidOperationException">Thrown when the values are not equal</exception>
        public static void AreEqual<T>(T expected, T actual, string message = null, [CallerMemberName] string memberName = null)
        {
            IsTrue(expected.Equals(actual), message, memberName);
        }
        
        /// <summary>
        /// Asserts that two values are not equal.
        /// </summary>
        /// <typeparam name="T">The type of the values to compare</typeparam>
        /// <param name="expected">The expected value</param>
        /// <param name="actual">The actual value</param>
        /// <param name="message">Optional error message</param>
        /// <param name="memberName">The name of the calling member (automatically populated)</param>
        /// <exception cref="InvalidOperationException">Thrown when the values are equal</exception>
        public static void AreNotEqual<T>(T expected, T actual, string message = null, [CallerMemberName] string memberName = null)
        {
            IsTrue(!expected.Equals(actual), message, memberName);
        }
    }
}