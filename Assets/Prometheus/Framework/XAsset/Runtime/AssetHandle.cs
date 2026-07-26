using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.XAsset
{
    public enum AssetOperationStatus
    {
        None,
        Loading,
        Succeeded,
        Failed,
        Released
    }

    public sealed class AssetHandle<T> : CustomYieldInstruction, IDisposable
        where T : UnityEngine.Object
    {
        private AssetProvider provider;
        private bool released;
        private Exception handleError;
        private Action<AssetHandle<T>> completed;
        private readonly TaskCompletionSource<T> completionSource =
            new TaskCompletionSource<T>();

        internal AssetHandle(AssetProvider provider)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.provider.AddReference();
            this.provider.Completed += OnProviderCompleted;

            if (this.provider.IsDone)
                OnProviderCompleted(this.provider);
        }

        public override bool keepWaiting => IsValid && !IsDone;

        public bool IsValid => !released && provider != null;

        public bool IsDone =>
            Status == AssetOperationStatus.Succeeded ||
            Status == AssetOperationStatus.Failed;

        public AssetOperationStatus Status
        {
            get
            {
                if (!IsValid)
                    return AssetOperationStatus.Released;

                if (handleError != null)
                    return AssetOperationStatus.Failed;

                switch (provider.State)
                {
                    case AssetProviderState.None:
                        return AssetOperationStatus.None;
                    case AssetProviderState.Loading:
                        return AssetOperationStatus.Loading;
                    case AssetProviderState.Succeeded:
                        return AssetOperationStatus.Succeeded;
                    case AssetProviderState.Failed:
                        return AssetOperationStatus.Failed;
                    default:
                        return AssetOperationStatus.Released;
                }
            }
        }

        public Exception Error
        {
            get
            {
                if (!IsValid)
                    return new ObjectDisposedException(nameof(AssetHandle<T>));

                return handleError ?? provider.Error;
            }
        }

        public T Asset
        {
            get
            {
                EnsureValid();

                if (!IsDone)
                    throw new InvalidOperationException(
                        $"Asset '{provider.AssetInfo.Address}' has not finished loading.");

                Exception error = Error;
                if (error != null)
                    throw error;

                return (T)provider.AssetObject;
            }
        }

        public Task<T> Task => completionSource.Task;

        public event Action<AssetHandle<T>> Completed
        {
            add
            {
                EnsureValid();

                if (IsDone)
                    value?.Invoke(this);
                else
                    completed += value;
            }
            remove => completed -= value;
        }

        public AssetHandle<T> Retain()
        {
            EnsureValid();
            return new AssetHandle<T>(provider);
        }

        public void Release()
        {
            if (released)
                return;

            released = true;

            AssetProvider currentProvider = provider;
            provider = null;

            if (currentProvider != null)
            {
                currentProvider.Completed -= OnProviderCompleted;
                currentProvider.RemoveReference();
            }

            if (!completionSource.Task.IsCompleted)
                completionSource.TrySetCanceled();

            completed = null;
        }

        public void Dispose()
        {
            Release();
        }

        private void OnProviderCompleted(AssetProvider completedProvider)
        {
            if (released || completedProvider != provider)
                return;

            if (completedProvider.Error != null)
            {
                handleError = completedProvider.Error;
                completionSource.TrySetException(handleError);
            }
            else if (!(completedProvider.AssetObject is T typedAsset))
            {
                string actualType = completedProvider.AssetObject == null
                    ? "null"
                    : completedProvider.AssetObject.GetType().FullName;

                handleError = new InvalidCastException(
                    $"Asset '{completedProvider.AssetInfo.Address}' is '{actualType}', " +
                    $"not '{typeof(T).FullName}'.");
                completionSource.TrySetException(handleError);
            }
            else
            {
                completionSource.TrySetResult(typedAsset);
            }

            Action<AssetHandle<T>> callbacks = completed;
            completed = null;
            callbacks?.Invoke(this);
        }

        private void EnsureValid()
        {
            if (!IsValid)
                throw new ObjectDisposedException(nameof(AssetHandle<T>));
        }
    }
}
