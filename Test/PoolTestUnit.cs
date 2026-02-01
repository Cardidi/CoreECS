using System;
using System.Collections.Generic;
using CoreECS.Utils;
using NUnit.Framework;

namespace TinyECS.Test
{
    [TestFixture]
    public class PoolTestUnit
    {
        [Test]
        public void Pool_Get_ReturnsNewInstanceWhenEmpty()
        {
            // Arrange
            var pool = new Pool<TestObject>(() => new TestObject());
            
            // Act
            var obj = pool.Get();
            
            // Assert
            Assert.IsNotNull(obj);
            Assert.AreEqual(0, pool.Count);
        }
        
        [Test]
        public void Pool_Get_ReturnsPooledInstanceWhenAvailable()
        {
            // Arrange
            var pool = new Pool<TestObject>(() => new TestObject());
            var originalObj = new TestObject();
            pool.Release(originalObj);
            
            // Act
            var obj = pool.Get();
            
            // Assert
            Assert.AreSame(originalObj, obj);
            Assert.AreEqual(0, pool.Count);
        }
        
        [Test]
        public void Pool_Release_AddsObjectToPool()
        {
            // Arrange
            var pool = new Pool<TestObject>(() => new TestObject());
            var obj = new TestObject();
            
            // Act
            pool.Release(obj);
            
            // Assert
            Assert.AreEqual(1, pool.Count);
        }
        
        [Test]
        public void Pool_Release_NullObject_ThrowsException()
        {
            // Arrange
            var pool = new Pool<TestObject>(() => new TestObject());
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => pool.Release(null));
        }
        
        [Test]
        public void Pool_GetWithOut_ReturnsDisposable()
        {
            // Arrange
            var pool = new Pool<TestObject>(() => new TestObject());
            
            // Act
            using (var disposable = pool.Get(out var obj))
            {
                // Assert
                Assert.IsNotNull(obj);
                Assert.AreEqual(0, pool.Count);
            }
            
            // After disposal, object should be back in pool
            Assert.AreEqual(1, pool.Count);
        }
        
        [Test]
        public void Pool_WithGetAction_CallsActionOnGet()
        {
            // Arrange
            var pool = new Pool<TestObject>(
                createFunc: () => new TestObject(),
                getAction: obj => obj.Value = "retrieved");
            
            // Act
            var obj = pool.Get();
            
            // Assert
            Assert.AreEqual("retrieved", obj.Value);
        }
        
        [Test]
        public void Pool_WithReturnAction_CallsActionOnReturn()
        {
            // Arrange
            var pool = new Pool<TestObject>(
                createFunc: () => new TestObject(),
                returnAction: obj => obj.Value = "returned");
            var obj = new TestObject();
            
            // Act
            pool.Release(obj);
            
            // Assert
            Assert.AreEqual("returned", obj.Value);
        }
        
        [Test]
        public void Pool_WithInitialCapacity_PrePopulatesPool()
        {
            // Arrange & Act
            var pool = new Pool<TestObject>(() => new TestObject(), initialCapacity: 5);
            
            // Assert
            Assert.AreEqual(5, pool.Count);
        }
        
        [Test]
        public void Pool_Clear_RemovesAllObjects()
        {
            // Arrange
            var pool = new Pool<TestObject>(() => new TestObject());
            pool.Release(new TestObject());
            pool.Release(new TestObject());
            pool.Release(new TestObject());
            
            // Act
            pool.Clear();
            
            // Assert
            Assert.AreEqual(0, pool.Count);
        }
        
        [Test]
        public void ListPool_Get_ReturnsClearedList()
        {
            // Arrange
            List<string> list;
            using (var disposable = ListPool<string>.Get(out list))
            {
                list.Add("item1");
                list.Add("item2");
            }
            
            // Act
            using (var disposable = ListPool<string>.Get(out list))
            {
                // Assert
                Assert.AreEqual(0, list.Count);
            }
        }
        
        [Test]
        public void ListPool_Get_ReturnsSameInstance()
        {
            // Arrange
            List<string> list1;
            using (var disposable1 = ListPool<string>.Get(out list1))
            {
                list1.Add("item1");
            }
            
            // Act
            List<string> list2;
            using (var disposable2 = ListPool<string>.Get(out list2))
            {
                // Assert
                Assert.AreSame(list1, list2);
            }
        }
        
        [Test]
        public void ListPool_Return_ManuallyReturnsList()
        {
            // Arrange
            var list1 = ListPool<string>.Get();
            list1.Add("item1");
            
            // Act
            ListPool<string>.Return(list1);
            
            // Assert
            var list2 = ListPool<string>.Get();
            Assert.AreSame(list1, list2);
            Assert.AreEqual(0, list2.Count);
        }
        
        private class TestObject
        {
            public string Value { get; set; } = "";
        }
    }
}