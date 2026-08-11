using UnityEngine;
namespace Xuan.Prometheus
{

    /// <summary>
    /// 泛型单例基类（继承 MonoBehaviour）
    /// </summary>
    /// <typeparam name="T">子类类型</typeparam>
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        // 静态实例
        private static T _instance;

        // 锁对象，用于线程安全（虽然Unity主线程用不上，但保留良好习惯）
        private static readonly object _lock = new object();

        // 应用程序是否正在退出
        private static bool _applicationIsQuitting = false;

        /// <summary>
        /// 公开的实例访问器
        /// </summary>
        public static T Ins
        {
            get
            {
                // 如果应用正在退出，返回 null 避免重新创建
                if (_applicationIsQuitting)
                {
                    Debug.LogWarning($"[{typeof(T)}] 应用正在退出，返回 null");
                    return null;
                }

                lock (_lock)
                {
                    // 如果实例为空，尝试查找或创建
                    if (_instance == null)
                    {
                        _instance = FindFirstObjectByType<T>();

                        // 如果场景中不存在，则创建新 GameObject 挂载
                        if (_instance == null)
                        {
                            GameObject singletonGO = new GameObject();
                            singletonGO.name = typeof(T).ToString() + " (Singleton)";
                            _instance = singletonGO.AddComponent<T>();

                            // 标记为 DontDestroyOnLoad，跨场景持久化
                            DontDestroyOnLoad(singletonGO);

                            // Debug.Log($"[{typeof(T)}] 自动创建单例实例");
                        }
                        else
                        {
                            Debug.Log($"[{typeof(T)}] 找到场景中已存在的实例");
                        }
                    }

                    return _instance;
                }
            }
        }

        /// <summary>
        /// 虚方法，子类可重写 Awake 进行初始化
        /// </summary>
        protected virtual void Awake()
        {
            // 如果场景中存在多个实例，销毁多余的
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"[{typeof(T)}] 检测到重复实例，销毁: {gameObject.name}");
                Destroy(gameObject);
                return;
            }

            // 如果实例为空，将当前对象设为实例
            if (_instance == null)
            {
                _instance = this as T;

                // 可以选择是否跨场景持久化（子类可以通过重写控制）
                if (ShouldPersistAcrossScenes())
                {
                    DontDestroyOnLoad(gameObject);
                }
            }

            // 调用子类的初始化方法
            OnAwake();
        }

        /// <summary>
        /// 子类可重写的 Awake 扩展方法
        /// </summary>
        protected virtual void OnAwake() { }

        /// <summary>
        /// 是否跨场景持久化（子类可重写）
        /// </summary>
        protected virtual bool ShouldPersistAcrossScenes()
        {
            return true; // 默认跨场景持久化
        }

        /// <summary>
        /// 应用退出时清理
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            _applicationIsQuitting = true;
        }

        /// <summary>
        /// 手动销毁单例（用于场景切换时清理）
        /// </summary>
        public static void DestroyInstance()
        {
            if (_instance != null)
            {
                Debug.Log($"[{typeof(T)}] 手动销毁单例实例");
                Destroy(_instance.gameObject);
                _instance = null;
                _applicationIsQuitting = false;
            }
        }
    }
}