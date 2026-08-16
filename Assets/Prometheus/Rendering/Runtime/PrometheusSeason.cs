namespace Xuan.Prometheus.Rendering
{
    /// <summary>
    /// Identifies the four ordered seasons used by environment interpolation and project shaders.
    /// </summary>
    public enum PrometheusSeason
    {
        /// <summary>
        /// Represents spring environment values and transitions toward summer.
        /// </summary>
        Spring = 0,

        /// <summary>
        /// Represents summer environment values and transitions toward autumn.
        /// </summary>
        Summer = 1,

        /// <summary>
        /// Represents autumn environment values and transitions toward winter.
        /// </summary>
        Autumn = 2,

        /// <summary>
        /// Represents winter environment values and transitions toward spring.
        /// </summary>
        Winter = 3
    }
}
