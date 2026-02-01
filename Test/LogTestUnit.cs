using System;
using CoreECS.Utils;
using NUnit.Framework;

namespace TinyECS.Test
{
    [TestFixture]
    public class LogTestUnit
    {
        private TestLogger _testLogger;
        
        [SetUp]
        public void Setup()
        {
            _testLogger = new TestLogger();
            Log.Logger = _testLogger;
        }
        
        [TearDown]
        public void TearDown()
        {
            Log.Logger = null;
        }
        
        [Test]
        public void Debug_LogsCorrectMessage()
        {
            // Arrange
            const string message = "Debug message";
            
            // Act
            Log.Debug(message);
            
            // Assert
            Assert.AreEqual(1, _testLogger.DebugMessages.Count);
            Assert.AreEqual(message, _testLogger.DebugMessages[0]);
        }
        
        [Test]
        public void Info_LogsCorrectMessage()
        {
            // Arrange
            const string message = "Info message";
            
            // Act
            Log.Info(message);
            
            // Assert
            Assert.AreEqual(1, _testLogger.InfoMessages.Count);
            Assert.AreEqual(message, _testLogger.InfoMessages[0]);
        }
        
        [Test]
        public void Warn_LogsCorrectMessage()
        {
            // Arrange
            const string message = "Warning message";
            
            // Act
            Log.Warn(message);
            
            // Assert
            Assert.AreEqual(1, _testLogger.WarningMessages.Count);
            Assert.AreEqual(message, _testLogger.WarningMessages[0]);
        }
        
        [Test]
        public void Err_LogsCorrectMessage()
        {
            // Arrange
            const string message = "Error message";
            
            // Act
            Log.Err(message);
            
            // Assert
            Assert.AreEqual(1, _testLogger.ErrorMessages.Count);
            Assert.AreEqual(message, _testLogger.ErrorMessages[0]);
        }
        
        [Test]
        public void Exp_LogsCorrectException()
        {
            // Arrange
            var exception = new InvalidOperationException("Test exception");
            
            // Act
            Log.Exp(exception);
            
            // Assert
            Assert.AreEqual(1, _testLogger.ExceptionMessages.Count);
            Assert.AreEqual(exception, _testLogger.ExceptionMessages[0]);
        }
        
        [Test]
        public void LogWithoutLogger_DoesNotThrow()
        {
            // Arrange
            Log.Logger = null;
            
            // Act & Assert
            Assert.DoesNotThrow(() => Log.Debug("Test"));
            Assert.DoesNotThrow(() => Log.Info("Test"));
            Assert.DoesNotThrow(() => Log.Warn("Test"));
            Assert.DoesNotThrow(() => Log.Err("Test"));
            Assert.DoesNotThrow(() => Log.Exp(new Exception("Test")));
        }
        
        private class TestLogger : ILogger
        {
            public readonly List<string> DebugMessages = new();
            public readonly List<string> InfoMessages = new();
            public readonly List<string> WarningMessages = new();
            public readonly List<string> ErrorMessages = new();
            public readonly List<Exception> ExceptionMessages = new();
            
            public void Debug(string msg, object context = null)
            {
                DebugMessages.Add(msg);
            }
            
            public void Info(string msg, object context = null)
            {
                InfoMessages.Add(msg);
            }
            
            public void Warn(string msg, object context = null)
            {
                WarningMessages.Add(msg);
            }
            
            public void Err(string msg, object context = null)
            {
                ErrorMessages.Add(msg);
            }
            
            public void Exp(Exception err, object context = null)
            {
                ExceptionMessages.Add(err);
            }
        }
    }
}