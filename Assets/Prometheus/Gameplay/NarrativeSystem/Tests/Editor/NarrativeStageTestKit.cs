using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative.Tests
{
    /// <summary>
    /// 舞台测试使用的一组伪端口。
    /// 所有等待立即完成，所有操作按调用顺序写入共享日志，因此接管与还原的顺序可以被精确断言。
    /// </summary>
    public sealed class FakeStageEnvironment : IDisposable
    {
        /// <summary>按调用顺序记录全部端口操作。</summary>
        public List<string> Log { get; } = new List<string>();

        /// <summary>保存测试期间创建的场景对象，用于统一清理。</summary>
        private readonly List<GameObject> spawned = new List<GameObject>();

        /// <summary>创建一组互相共享日志的伪端口。</summary>
        public FakeStageEnvironment()
        {
            Screen = new FakeScreen(Log);
            Actors = new FakeActorResolver(Log, spawned);
            Camera = new FakeCameraPort(Log);
            World = new FakeWorldPort(Log);
            Vfx = new FakeVfxPort(Log);
            Audio = new FakeAudioPort(Log);
            Assets = new FakeAssetPort(Log);
            Services = new StageServices(Screen, Actors)
            {
                Camera = Camera,
                World = World,
                Vfx = Vfx,
                Audio = Audio,
                Assets = Assets
            };
        }

        /// <summary>获取伪屏幕端口。</summary>
        public FakeScreen Screen { get; }

        /// <summary>获取伪角色解析器。</summary>
        public FakeActorResolver Actors { get; }

        /// <summary>获取伪镜头端口。</summary>
        public FakeCameraPort Camera { get; }

        /// <summary>获取伪世界端口。</summary>
        public FakeWorldPort World { get; }

        /// <summary>获取伪特效端口。</summary>
        public FakeVfxPort Vfx { get; }

        /// <summary>获取伪音频端口。</summary>
        public FakeAudioPort Audio { get; }

        /// <summary>获取伪资源端口。</summary>
        public FakeAssetPort Assets { get; }

        /// <summary>获取组装好的能力端口集合。</summary>
        public StageServices Services { get; }

        /// <summary>创建一个受本环境管理的场景对象。</summary>
        public GameObject CreateObject(string name)
        {
            GameObject created = new GameObject(name);
            spawned.Add(created);
            return created;
        }

        /// <summary>销毁测试期间创建的全部场景对象。</summary>
        public void Dispose()
        {
            for (int index = 0; index < spawned.Count; index++)
            {
                if (spawned[index] != null) UnityEngine.Object.DestroyImmediate(spawned[index]);
            }
            spawned.Clear();
        }

        /// <summary>记录调用顺序的伪屏幕端口。</summary>
        public sealed class FakeScreen : INarrativeScreen
        {
            private readonly List<string> log;

            /// <summary>创建一个伪屏幕端口。</summary>
            public FakeScreen(List<string> log)
            {
                this.log = log;
            }

            /// <inheritdoc />
            public float FadeAlpha { get; set; }

            /// <inheritdoc />
            public float LetterboxRatio { get; set; }

            /// <inheritdoc />
            public bool HudVisible { get; set; } = true;

            /// <inheritdoc />
            public UniTask FadeAsync(float target, float duration, CancellationToken cancellationToken)
            {
                FadeAlpha = target;
                log.Add($"fade:{target}");
                return UniTask.CompletedTask;
            }

            /// <inheritdoc />
            public UniTask LetterboxAsync(float target, float duration, CancellationToken cancellationToken)
            {
                LetterboxRatio = target;
                log.Add($"letterbox:{target}");
                return UniTask.CompletedTask;
            }
        }

        /// <summary>按需创建场景对象的伪角色解析器。</summary>
        public sealed class FakeActorResolver : IActorResolver
        {
            private readonly List<string> log;
            private readonly List<GameObject> spawned;
            private readonly Dictionary<ActorRef, ActorHandle> resolved = new Dictionary<ActorRef, ActorHandle>();
            private readonly Dictionary<string, Transform> anchors = new Dictionary<string, Transform>(StringComparer.Ordinal);

            /// <summary>创建一个伪角色解析器。</summary>
            public FakeActorResolver(List<string> log, List<GameObject> spawned)
            {
                this.log = log;
                this.spawned = spawned;
            }

            /// <summary>获取或设置解析时是否直接抛出异常，用于测试进入失败的回滚路径。</summary>
            public bool ThrowOnResolve { get; set; }

            /// <summary>登记一个测试锚点。</summary>
            public Transform AddAnchor(string anchorId, Vector3 position)
            {
                GameObject anchorObject = new GameObject($"Anchor_{anchorId}");
                anchorObject.transform.position = position;
                spawned.Add(anchorObject);
                anchors[anchorId] = anchorObject.transform;
                return anchorObject.transform;
            }

            /// <inheritdoc />
            public UniTask<ActorHandle> ResolveAsync(ActorRef actor, CancellationToken cancellationToken)
            {
                if (ThrowOnResolve) throw new InvalidOperationException($"Fake resolver refuses to resolve '{actor}'.");
                if (resolved.TryGetValue(actor, out ActorHandle cached)) return UniTask.FromResult(cached);
                GameObject target = new GameObject($"Actor_{actor.Id}");
                spawned.Add(target);
                ActorHandle handle = new ActorHandle(actor, target);
                resolved[actor] = handle;
                log.Add($"resolve:{actor.Id}");
                return UniTask.FromResult(handle);
            }

            /// <inheritdoc />
            public bool TryGetResolved(ActorRef actor, out ActorHandle handle)
            {
                return resolved.TryGetValue(actor, out handle);
            }

            /// <inheritdoc />
            public void Release(ActorHandle handle)
            {
                if (handle == null) return;
                resolved.Remove(handle.Actor);
                log.Add($"release:{handle.Actor.Id}");
            }

            /// <inheritdoc />
            public void ReleaseAll()
            {
                resolved.Clear();
            }

            /// <inheritdoc />
            public bool TryGetAnchor(string anchorId, out Transform anchor)
            {
                return anchors.TryGetValue(anchorId, out anchor);
            }
        }

        /// <summary>记录调用顺序的伪镜头端口。</summary>
        public sealed class FakeCameraPort : INarrativeCameraPort
        {
            private readonly List<string> log;

            /// <summary>创建一个伪镜头端口。</summary>
            public FakeCameraPort(List<string> log)
            {
                this.log = log;
            }

            /// <summary>获取最近一次被切到的机位名。</summary>
            public string CurrentCamera { get; private set; }

            /// <inheritdoc />
            public IDisposable AcquireControl(int priority)
            {
                log.Add("camera-acquire");
                return new LoggingLease(log, "camera-release");
            }

            /// <inheritdoc />
            public void SnapTo(string cameraId)
            {
                CurrentCamera = cameraId;
                log.Add($"camera-snap:{cameraId}");
            }

            /// <inheritdoc />
            public UniTask BlendToAsync(string cameraId, float duration, CancellationToken cancellationToken)
            {
                CurrentCamera = cameraId;
                log.Add($"camera-blend:{cameraId}");
                return UniTask.CompletedTask;
            }

            /// <inheritdoc />
            public UniTask ShakeAsync(float amplitude, float duration, CancellationToken cancellationToken)
            {
                log.Add("camera-shake");
                return UniTask.CompletedTask;
            }

            /// <inheritdoc />
            public bool HasCamera(string cameraId)
            {
                return true;
            }
        }

        /// <summary>记录调用顺序的伪世界端口。</summary>
        public sealed class FakeWorldPort : INarrativeWorldPort
        {
            private readonly List<string> log;

            /// <summary>创建一个伪世界端口。</summary>
            public FakeWorldPort(List<string> log)
            {
                this.log = log;
            }

            /// <inheritdoc />
            public IDisposable LockGameplayInput()
            {
                log.Add("input-lock");
                return new LoggingLease(log, "input-unlock");
            }

            /// <inheritdoc />
            public IDisposable FreezeAi(Vector3 center, float radius)
            {
                log.Add("ai-freeze");
                return new LoggingLease(log, "ai-unfreeze");
            }
        }

        /// <summary>记录调用顺序的伪特效端口。</summary>
        public sealed class FakeVfxPort : INarrativeVfxPort
        {
            private readonly List<string> log;

            /// <summary>创建一个伪特效端口。</summary>
            public FakeVfxPort(List<string> log)
            {
                this.log = log;
            }

            /// <summary>获取当前存活的特效数量。</summary>
            public int AliveCount { get; private set; }

            /// <inheritdoc />
            public UniTask<INarrativeVfxHandle> SpawnAsync(string location, Vector3 position, Quaternion rotation, Transform parent, CancellationToken cancellationToken)
            {
                AliveCount++;
                log.Add($"vfx-spawn:{location}");
                return UniTask.FromResult<INarrativeVfxHandle>(new FakeVfxHandle(location));
            }

            /// <inheritdoc />
            public void Stop(INarrativeVfxHandle handle)
            {
                if (!(handle is FakeVfxHandle fake) || !fake.IsAlive) return;
                fake.Kill();
                AliveCount--;
                log.Add($"vfx-stop:{handle.Location}");
            }

            /// <inheritdoc />
            public void StopAll()
            {
                AliveCount = 0;
                log.Add("vfx-stop-all");
            }

            /// <summary>伪特效句柄。</summary>
            private sealed class FakeVfxHandle : INarrativeVfxHandle
            {
                /// <summary>创建一个伪特效句柄。</summary>
                internal FakeVfxHandle(string location)
                {
                    Location = location;
                    IsAlive = true;
                }

                /// <inheritdoc />
                public bool IsAlive { get; private set; }

                /// <inheritdoc />
                public string Location { get; }

                /// <summary>标记句柄已回收。</summary>
                internal void Kill()
                {
                    IsAlive = false;
                }
            }
        }

        /// <summary>记录调用顺序的伪音频端口。</summary>
        public sealed class FakeAudioPort : INarrativeAudioPort
        {
            private readonly List<string> log;

            /// <summary>创建一个伪音频端口。</summary>
            public FakeAudioPort(List<string> log)
            {
                this.log = log;
            }

            /// <inheritdoc />
            public void PlayOneShot(string eventKey, Vector3 position)
            {
                log.Add($"sfx:{eventKey}");
            }

            /// <inheritdoc />
            public INarrativeAudioHandle PlayPersistent(string eventKey, float fadeInSeconds)
            {
                log.Add($"audio-start:{eventKey}");
                return new FakeAudioHandle(eventKey);
            }

            /// <inheritdoc />
            public void StopPersistent(INarrativeAudioHandle handle, float fadeOutSeconds)
            {
                if (handle == null) return;
                log.Add($"audio-stop:{handle.EventKey}");
            }

            /// <inheritdoc />
            public void StopAll()
            {
                log.Add("audio-stop-all");
            }

            /// <summary>伪音频句柄。</summary>
            private sealed class FakeAudioHandle : INarrativeAudioHandle
            {
                /// <summary>创建一个伪音频句柄。</summary>
                internal FakeAudioHandle(string eventKey)
                {
                    EventKey = eventKey;
                }

                /// <inheritdoc />
                public bool IsAlive => true;

                /// <inheritdoc />
                public string EventKey { get; }
            }
        }

        /// <summary>按登记表返回资源的伪资源端口。</summary>
        public sealed class FakeAssetPort : INarrativeAssetPort
        {
            private readonly List<string> log;
            private readonly Dictionary<string, UnityEngine.Object> assets = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);

            /// <summary>创建一个伪资源端口。</summary>
            public FakeAssetPort(List<string> log)
            {
                this.log = log;
            }

            /// <summary>登记一个可按地址取得的资源。</summary>
            public void Register(string location, UnityEngine.Object asset)
            {
                assets[location] = asset;
            }

            /// <inheritdoc />
            public UniTask<TAsset> LoadAsync<TAsset>(string location, CancellationToken cancellationToken) where TAsset : UnityEngine.Object
            {
                log.Add($"load:{location}");
                assets.TryGetValue(location, out UnityEngine.Object asset);
                return UniTask.FromResult(asset as TAsset);
            }

            /// <inheritdoc />
            public bool TryGet<TAsset>(string location, out TAsset asset) where TAsset : UnityEngine.Object
            {
                assets.TryGetValue(location, out UnityEngine.Object stored);
                asset = stored as TAsset;
                return asset != null;
            }

            /// <inheritdoc />
            public void Release(string location)
            {
                log.Add($"unload:{location}");
            }

            /// <inheritdoc />
            public void ReleaseAll()
            {
                log.Add("unload-all");
            }
        }

        /// <summary>释放时写入一条日志的租约。</summary>
        private sealed class LoggingLease : IDisposable
        {
            private readonly List<string> log;
            private readonly string message;
            private bool released;

            /// <summary>创建一个记录释放事件的租约。</summary>
            internal LoggingLease(List<string> log, string message)
            {
                this.log = log;
                this.message = message;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                if (released) return;
                released = true;
                log.Add(message);
            }
        }
    }
}
