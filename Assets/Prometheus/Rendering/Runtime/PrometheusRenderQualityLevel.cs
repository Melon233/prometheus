namespace Xuan.Prometheus.Rendering
{
    /// <summary>
    /// Identifies the project-owned rendering quality levels without depending on Unity's built-in quality level indices or names.
    /// </summary>
    public enum PrometheusRenderQualityLevel
    {
        /// <summary>
        /// Uses the minimum rendering cost and disables realtime lighting, realtime shadows, HDR, anti-aliasing, post-processing, and optional screen-space effects.
        /// </summary>
        Low = 0,

        /// <summary>
        /// Balances image quality and GPU cost with a platform-specific rendering budget.
        /// </summary>
        Mid = 1
    }

    /// <summary>
    /// Identifies the hardware family whose pipeline and quality profile must be selected.
    /// </summary>
    public enum PrometheusRenderPlatform
    {
        /// <summary>
        /// Uses the desktop pipeline family and allows Forward or Deferred rendering.
        /// </summary>
        Pc = 0,

        /// <summary>
        /// Uses the mobile-only Forward pipeline family.
        /// </summary>
        Mobile = 1
    }

    /// <summary>
    /// Identifies the renderer path independently from the user-facing quality level.
    /// </summary>
    public enum PrometheusRenderPath
    {
        /// <summary>
        /// Uses the Forward renderer and supports both desktop and mobile hardware.
        /// </summary>
        Forward = 0,

        /// <summary>
        /// Uses the desktop-only Deferred renderer and its screen-space feature chain when the Mid quality level is active.
        /// </summary>
        Deferred = 1
    }
}
