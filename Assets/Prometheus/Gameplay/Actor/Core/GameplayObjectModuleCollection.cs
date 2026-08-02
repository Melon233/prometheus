using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.Actor
{
    /// <summary>
    /// Owns a uniquely identified set of gameplay object modules and coordinates their transactional lifecycle.
    /// </summary>
    public sealed class GameplayObjectModuleCollection : IDisposable
    {
        private readonly List<ModuleEntry> modules = new List<ModuleEntry>();
        private readonly Dictionary<string, ModuleEntry> modulesById = new Dictionary<string, ModuleEntry>(StringComparer.Ordinal);
        private CollectionState state;
        private int initializationStartedCount;

        /// <summary>
        /// Initializes an empty module collection that accepts registrations until initialization starts.
        /// </summary>
        public GameplayObjectModuleCollection()
        {
        }

        /// <summary>
        /// Initializes a module collection and registers the supplied modules in enumeration order.
        /// </summary>
        /// <param name="modules">The modules to register.</param>
        /// <exception cref="ArgumentNullException">Thrown when the sequence or one of its modules is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when a module identifier is empty or duplicates an existing identifier.</exception>
        public GameplayObjectModuleCollection(IEnumerable<IGameplayObjectModule> modules)
        {
            if (modules == null)
            {
                throw new ArgumentNullException(nameof(modules));
            }

            foreach (var module in modules)
            {
                Add(module);
            }
        }

        /// <summary>
        /// Gets the number of registered modules.
        /// </summary>
        public int Count => modules.Count;

        /// <summary>
        /// Gets whether every registered module completed initialization successfully.
        /// </summary>
        public bool IsInitialized => state == CollectionState.Initialized;

        /// <summary>
        /// Gets whether the collection has completed disposal or initialization rollback.
        /// </summary>
        public bool IsDisposed => state == CollectionState.Disposed;

        /// <summary>
        /// Registers a module while preserving registration order for initialization and reverse disposal.
        /// </summary>
        /// <param name="module">The module to register.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="module"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when the module identifier is empty or duplicates an existing identifier.</exception>
        /// <exception cref="InvalidOperationException">Thrown when initialization has started or the collection has been disposed.</exception>
        public void Add(IGameplayObjectModule module)
        {
            if (state != CollectionState.Building)
            {
                throw new InvalidOperationException("Modules can only be registered before collection initialization starts.");
            }

            if (module == null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            var moduleId = module.ModuleId;
            ValidateModuleId(moduleId, nameof(module));
            if (modulesById.ContainsKey(moduleId))
            {
                throw new ArgumentException($"A gameplay object module with identifier '{moduleId}' is already registered.", nameof(module));
            }

            var entry = new ModuleEntry(moduleId, module);
            modules.Add(entry);
            modulesById.Add(moduleId, entry);
        }

        /// <summary>
        /// Retrieves a registered module by its exact ordinal identifier.
        /// </summary>
        /// <param name="moduleId">The stable module identifier.</param>
        /// <returns>The registered module.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="moduleId"/> is empty.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the identifier has not been registered.</exception>
        public IGameplayObjectModule GetModule(string moduleId)
        {
            ValidateModuleId(moduleId, nameof(moduleId));
            if (!modulesById.TryGetValue(moduleId, out var entry))
            {
                throw new KeyNotFoundException($"No gameplay object module with identifier '{moduleId}' is registered.");
            }

            return entry.Module;
        }

        /// <summary>
        /// Attempts to retrieve a registered module by its exact ordinal identifier.
        /// </summary>
        /// <param name="moduleId">The stable module identifier.</param>
        /// <param name="module">Receives the registered module when found; otherwise, <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the identifier is registered; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="moduleId"/> is empty.</exception>
        public bool TryGetModule(string moduleId, out IGameplayObjectModule module)
        {
            ValidateModuleId(moduleId, nameof(moduleId));
            if (modulesById.TryGetValue(moduleId, out var entry))
            {
                module = entry.Module;
                return true;
            }

            module = null;
            return false;
        }

        /// <summary>
        /// Initializes registered modules in registration order and rolls back every module whose initialization started when any initialization fails.
        /// </summary>
        /// <param name="context">The shared gameplay object module context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when initialization has already started, completed, or the collection has been disposed.</exception>
        /// <exception cref="AggregateException">Thrown when initialization fails and one or more rollback disposals also fail; the initialization exception is the first inner exception.</exception>
        public void Initialize(GameplayObjectModuleContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (state != CollectionState.Building)
            {
                throw new InvalidOperationException("A gameplay object module collection can only be initialized once.");
            }

            ValidateStableModuleIds();
            state = CollectionState.Initializing;
            initializationStartedCount = 0;
            try
            {
                for (var index = 0; index < modules.Count; index++)
                {
                    var entry = modules[index];
                    EnsureStableModuleId(entry);
                    initializationStartedCount = index + 1;
                    entry.Module.Initialize(context);
                    EnsureStableModuleId(entry);
                    if (state != CollectionState.Initializing)
                    {
                        throw new ObjectDisposedException(nameof(GameplayObjectModuleCollection), "The module collection was disposed during initialization.");
                    }
                }

                state = CollectionState.Initialized;
            }
            catch (Exception initializationException)
            {
                if (state == CollectionState.Disposed)
                {
                    throw;
                }

                state = CollectionState.Disposed;
                var rollbackExceptions = new List<Exception>();
                DisposeModulesReverse(initializationStartedCount, rollbackExceptions);
                if (rollbackExceptions.Count == 0)
                {
                    throw;
                }

                var lifecycleExceptions = new List<Exception>(rollbackExceptions.Count + 1) { initializationException };
                lifecycleExceptions.AddRange(rollbackExceptions);
                throw new AggregateException("Gameplay object module initialization failed and rollback produced one or more additional errors.", lifecycleExceptions);
            }
        }

        /// <summary>
        /// Disposes owned modules in reverse registration order and makes repeated disposal a no-op.
        /// </summary>
        /// <exception cref="AggregateException">Thrown after every eligible module has been visited when one or more module disposals fail.</exception>
        public void Dispose()
        {
            if (state == CollectionState.Disposed)
            {
                return;
            }

            var disposeCount = state == CollectionState.Initializing ? initializationStartedCount : modules.Count;
            state = CollectionState.Disposed;
            var disposalExceptions = new List<Exception>();
            DisposeModulesReverse(disposeCount, disposalExceptions);
            if (disposalExceptions.Count > 0)
            {
                throw new AggregateException("One or more gameplay object modules failed during disposal.", disposalExceptions);
            }
        }

        private static void ValidateModuleId(string moduleId, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
            {
                throw new ArgumentException("A gameplay object module identifier cannot be null, empty, or whitespace.", parameterName);
            }
        }

        private static void EnsureStableModuleId(ModuleEntry entry)
        {
            if (!string.Equals(entry.ModuleId, entry.Module.ModuleId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Gameplay object module '{entry.ModuleId}' changed its stable ModuleId after registration.");
            }
        }

        private void ValidateStableModuleIds()
        {
            for (var index = 0; index < modules.Count; index++)
            {
                EnsureStableModuleId(modules[index]);
            }
        }

        private void DisposeModulesReverse(int count, ICollection<Exception> exceptions)
        {
            for (var index = count - 1; index >= 0; index--)
            {
                try
                {
                    modules[index].Module.Dispose();
                }
                catch (Exception exception)
                {
                    exceptions.Add(exception);
                }
            }
        }

        private sealed class ModuleEntry
        {
            internal ModuleEntry(string moduleId, IGameplayObjectModule module)
            {
                ModuleId = moduleId;
                Module = module;
            }

            internal string ModuleId { get; }

            internal IGameplayObjectModule Module { get; }
        }

        private enum CollectionState
        {
            Building,
            Initializing,
            Initialized,
            Disposed
        }
    }
}
