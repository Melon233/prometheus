using System;

namespace Xuan.Prometheus.Actor
{
    /// <summary>
    /// Supplies immutable construction-time dependencies to gameplay object modules.
    /// </summary>
    public sealed class GameplayObjectModuleContext
    {
        /// <summary>
        /// Initializes a module context for one gameplay object.
        /// </summary>
        /// <param name="owner">The gameplay object that owns every module initialized with this context.</param>
        /// <param name="services">An optional service provider for shared runtime services.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner"/> is <see langword="null"/>.</exception>
        public GameplayObjectModuleContext(object owner, IServiceProvider services = null)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Services = services;
        }

        /// <summary>
        /// Gets the gameplay object that owns the initialized modules.
        /// </summary>
        public object Owner { get; }

        /// <summary>
        /// Gets the optional provider used to resolve shared runtime services.
        /// </summary>
        public IServiceProvider Services { get; }
    }

    /// <summary>
    /// Defines one independently authored and lifecycle-managed gameplay object module.
    /// </summary>
    public interface IGameplayObjectModule : IDisposable
    {
        /// <summary>
        /// Gets the stable, globally meaningful identifier used to register and retrieve this module within its gameplay object.
        /// </summary>
        string ModuleId { get; }

        /// <summary>
        /// Initializes this module after all modules have been registered and validated.
        /// </summary>
        /// <param name="context">The immutable context shared by modules belonging to the same gameplay object.</param>
        void Initialize(GameplayObjectModuleContext context);
    }
}
