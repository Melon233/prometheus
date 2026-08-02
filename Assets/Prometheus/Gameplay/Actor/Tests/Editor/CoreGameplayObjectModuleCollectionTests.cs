using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>
    /// Verifies deterministic registration, initialization, rollback, and disposal for gameplay object modules.
    /// </summary>
    [TestFixture]
    public sealed class CoreGameplayObjectModuleCollectionTests
    {
        /// <summary>
        /// Verifies that modules initialize in registration order and dispose exactly once in reverse order.
        /// </summary>
        [Test]
        public void InitializeAndDisposeUseDeterministicOppositeOrders()
        {
            var events = new List<string>();
            var collection = new GameplayObjectModuleCollection();
            collection.Add(new RecordingModule("movement", events));
            collection.Add(new RecordingModule("animation", events));
            collection.Add(new RecordingModule("camera", events));

            collection.Initialize(new GameplayObjectModuleContext(new object()));

            Assert.That(collection.IsInitialized, Is.True);
            CollectionAssert.AreEqual(new[] { "initialize:movement", "initialize:animation", "initialize:camera" }, events);
            collection.Dispose();
            collection.Dispose();
            CollectionAssert.AreEqual(new[] { "initialize:movement", "initialize:animation", "initialize:camera", "dispose:camera", "dispose:animation", "dispose:movement" }, events);
        }

        /// <summary>
        /// Verifies that duplicate identifiers are rejected with ordinal identifier semantics.
        /// </summary>
        [Test]
        public void AddRejectsDuplicateModuleIdentifiers()
        {
            var events = new List<string>();
            var collection = new GameplayObjectModuleCollection();
            collection.Add(new RecordingModule("motion", events));

            Assert.Throws<ArgumentException>(() => collection.Add(new RecordingModule("motion", events)));
            Assert.DoesNotThrow(() => collection.Add(new RecordingModule("Motion", events)));
            Assert.That(collection.Count, Is.EqualTo(2));
            collection.Dispose();
        }

        /// <summary>
        /// Verifies that an initialization failure disposes the failing and previously initialized modules in reverse order without touching later modules.
        /// </summary>
        [Test]
        public void InitializeFailureRollsBackEveryStartedModuleInReverseOrder()
        {
            var events = new List<string>();
            var collection = new GameplayObjectModuleCollection();
            collection.Add(new RecordingModule("movement", events));
            collection.Add(new RecordingModule("animation", events, throwOnInitialize: true));
            collection.Add(new RecordingModule("camera", events));

            Assert.Throws<InvalidOperationException>(() => collection.Initialize(new GameplayObjectModuleContext(new object())));

            Assert.That(collection.IsDisposed, Is.True);
            Assert.That(collection.IsInitialized, Is.False);
            CollectionAssert.AreEqual(new[] { "initialize:movement", "initialize:animation", "dispose:animation", "dispose:movement" }, events);
            collection.Dispose();
            CollectionAssert.AreEqual(new[] { "initialize:movement", "initialize:animation", "dispose:animation", "dispose:movement" }, events);
        }

        /// <summary>
        /// Verifies that rollback visits all started modules and reports both initialization and cleanup failures.
        /// </summary>
        [Test]
        public void InitializeFailureAggregatesRollbackErrorsAfterCompletingCleanup()
        {
            var events = new List<string>();
            var collection = new GameplayObjectModuleCollection();
            collection.Add(new RecordingModule("movement", events));
            collection.Add(new RecordingModule("animation", events, throwOnInitialize: true, throwOnDispose: true));

            var exception = Assert.Throws<AggregateException>(() => collection.Initialize(new GameplayObjectModuleContext(new object())));

            Assert.That(exception.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(exception.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
            Assert.That(exception.InnerExceptions[1], Is.TypeOf<ApplicationException>());
            CollectionAssert.AreEqual(new[] { "initialize:movement", "initialize:animation", "dispose:animation", "dispose:movement" }, events);
        }

        /// <summary>
        /// Verifies that disposal visits every module even when one module throws and still preserves reverse order.
        /// </summary>
        [Test]
        public void DisposeContinuesAfterModuleFailureAndAggregatesErrors()
        {
            var events = new List<string>();
            var collection = new GameplayObjectModuleCollection();
            collection.Add(new RecordingModule("movement", events));
            collection.Add(new RecordingModule("animation", events, throwOnDispose: true));
            collection.Add(new RecordingModule("camera", events));
            collection.Initialize(new GameplayObjectModuleContext(new object()));

            var exception = Assert.Throws<AggregateException>(() => collection.Dispose());

            Assert.That(exception.InnerExceptions.Count, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { "initialize:movement", "initialize:animation", "initialize:camera", "dispose:camera", "dispose:animation", "dispose:movement" }, events);
            Assert.DoesNotThrow(() => collection.Dispose());
        }

        /// <summary>
        /// Verifies that a registered identifier cannot change before module initialization.
        /// </summary>
        [Test]
        public void InitializeRejectsModuleIdentifierChangesAfterRegistration()
        {
            var events = new List<string>();
            var module = new RecordingModule("movement", events);
            var collection = new GameplayObjectModuleCollection();
            collection.Add(module);
            module.ModuleId = "changed";

            Assert.Throws<InvalidOperationException>(() => collection.Initialize(new GameplayObjectModuleContext(new object())));
            Assert.That(events, Is.Empty);
            module.ModuleId = "movement";
            collection.Dispose();
        }

        /// <summary>
        /// Records lifecycle events and can inject deterministic initialization or disposal failures.
        /// </summary>
        private sealed class RecordingModule : IGameplayObjectModule
        {
            private readonly IList<string> events;
            private readonly bool throwOnInitialize;
            private readonly bool throwOnDispose;

            internal RecordingModule(string moduleId, IList<string> events, bool throwOnInitialize = false, bool throwOnDispose = false)
            {
                ModuleId = moduleId;
                this.events = events;
                this.throwOnInitialize = throwOnInitialize;
                this.throwOnDispose = throwOnDispose;
            }

            public string ModuleId { get; set; }

            public void Initialize(GameplayObjectModuleContext context)
            {
                events.Add($"initialize:{ModuleId}");
                if (throwOnInitialize)
                {
                    throw new InvalidOperationException($"Module '{ModuleId}' failed initialization.");
                }
            }

            public void Dispose()
            {
                events.Add($"dispose:{ModuleId}");
                if (throwOnDispose)
                {
                    throw new ApplicationException($"Module '{ModuleId}' failed disposal.");
                }
            }
        }
    }
}
