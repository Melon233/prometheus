namespace Xuan.Prometheus.Component
{
    public class Singleton<T> where T : Singleton<T>, new()
    {
        protected Singleton()
        {
        }

        public static T Ins => Nested.Instance;

        private class Nested
        {
            internal static readonly T Instance = new T();
        }
    }
}