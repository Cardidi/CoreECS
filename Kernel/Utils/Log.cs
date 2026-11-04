using System;
using System.Runtime.CompilerServices;

namespace CoreECS.Utils
{
    /// <summary>
    /// Interface for logging implementations.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Logs a debug message.
        /// </summary>
        /// <param name="msg">The message to log</param>
        /// <param name="context">The calling context (automatically populated)</param>
        void Debug(string msg, [CallerMemberName] object? context = null);
        
        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <param name="msg">The message to log</param>
        /// <param name="context">The calling context (automatically populated)</param>
        void Info(string msg, [CallerMemberName] object? context = null);
        
        /// <summary>
        /// Logs a warning message.
        /// </summary>
        /// <param name="msg">The message to log</param>
        /// <param name="context">The calling context (automatically populated)</param>
        void Warn(string msg, [CallerMemberName] object? context = null);
        
        /// <summary>
        /// Logs an error message.
        /// </summary>
        /// <param name="msg">The message to log</param>
        /// <param name="context">The calling context (automatically populated)</param>
        void Err(string msg, [CallerMemberName] object? context = null);
        
        /// <summary>
        /// Logs an exception.
        /// </summary>
        /// <param name="err">The exception to log</param>
        /// <param name="context">The calling context (automatically populated)</param>
        void Exp(Exception err, [CallerMemberName] object? context = null);
    }
    
    /// <summary>
    /// Static class providing logging functionality.
    /// </summary>
    public static class Log
    {
        /// <summary>
        /// Gets or sets the logger implementation.
        /// </summary>
        public static ILogger? Logger { get; set; }
        
        /// <summary>
        /// Logs a debug message.
        /// </summary>
        /// <param name="msg">The message to log</param>
        /// <param name="context">The calling context (automatically populated)</param>
        public static void Debug(string msg, [CallerMemberName] object? context = null)
        {
            SafeLog(logger => logger.Debug(msg, context));
        }

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <param name="msg">The message to log</param>
        /// <param name="context">The calling context (automatically populated)</param>
        public static void Info(string msg, [CallerMemberName] object? context = null)
        {
            SafeLog(logger => logger.Info(msg, context));
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        /// <param name="msg">The message to log</param>
        /// <param name="context">The calling context (automatically populated)</param>
        public static void Warn(string msg, [CallerMemberName] object? context = null)
        {
            SafeLog(logger => logger.Warn(msg, context));
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
        /// <param name="msg">The message to log</param>
        /// <param name="context">The calling context (automatically populated)</param>
        public static void Err(string msg, [CallerMemberName] object? context = null)
        {
            SafeLog(logger => logger.Err(msg, context));
        }

        /// <summary>
        /// Logs an exception.
        /// </summary>
        /// <param name="err">The exception to log</param>
        /// <param name="context">The calling context (automatically populated)</param>
        public static void Exp(Exception err, [CallerMemberName] object? context = null)
        {
            SafeLog(logger => logger.Exp(err, context));
        }

        private static void SafeLog(Action<ILogger> write)
        {
            ILogger? logger = Logger;
            if (logger == null) return;

            try
            {
                write(logger);
            }
            catch
            {
                // A logging failure must never interrupt ECS control flow.
            }
        }
    }
}