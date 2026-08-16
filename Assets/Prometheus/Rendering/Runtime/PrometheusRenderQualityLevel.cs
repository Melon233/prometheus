namespace Xuan.Prometheus.Rendering
{
    /// <summary>
    /// Identifies the project-owned rendering quality levels without depending on Unity's built-in quality level indices or names.
    /// </summary>
    public enum PrometheusRenderQualityLevel
    {
        /// <summary>
        /// Reduces resolution, shadow cost, texture detail, and additional-light cost for constrained hardware.
        /// </summary>
        Low = 0,

        /// <summary>
        /// Balances image quality and GPU cost for mainstream hardware.
        /// </summary>
        Medium = 1,

        /// <summary>
        /// Preserves high-quality shadows, lighting, textures, and anti-aliasing for the intended desktop presentation.
        /// </summary>
        High = 2,

        /// <summary>
        /// Enables the longest shadow range and highest lighting budgets for high-end hardware.
        /// </summary>
        Ultra = 3
    }
}
