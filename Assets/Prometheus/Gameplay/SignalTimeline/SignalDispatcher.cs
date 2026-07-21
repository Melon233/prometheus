using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// Finds handler components and sends every emitted Signal to the handlers that support it.
    /// Attach this to the character root; handlers may live on the same object or its children.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SignalDispatcher : MonoBehaviour
    {
        [SerializeField] private bool _includeChildren = true;

        private readonly List<ISignalHandler> _handlers = new List<ISignalHandler>();
        private readonly List<ISignalHandler> _registeredHandlers = new List<ISignalHandler>();
        private readonly List<ISignalHandler> _dispatchBuffer = new List<ISignalHandler>();

        public event Action<Signal, SignalContext> SignalDispatched;

        private void Awake()
        {
            RebuildHandlers();
        }

        private void OnTransformChildrenChanged()
        {
            if (isActiveAndEnabled)
                RebuildHandlers();
        }

        public void RebuildHandlers()
        {
            _handlers.Clear();

            var components = _includeChildren
                ? GetComponentsInChildren<MonoBehaviour>(true)
                : GetComponents<MonoBehaviour>();

            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component == this)
                    continue;

                if (component is ISignalHandler handler)
                    AddHandlerIfMissing(handler);
            }

            for (var i = 0; i < _registeredHandlers.Count; i++)
                AddHandlerIfMissing(_registeredHandlers[i]);
        }

        /// <summary>Registers a non-MonoBehaviour handler, or a handler created at runtime.</summary>
        public void Register(ISignalHandler handler)
        {
            if (!IsValidHandler(handler))
                return;

            if (!_registeredHandlers.Contains(handler))
                _registeredHandlers.Add(handler);

            AddHandlerIfMissing(handler);
        }

        public void Unregister(ISignalHandler handler)
        {
            if (ReferenceEquals(handler, null))
                return;

            _registeredHandlers.Remove(handler);
            _handlers.Remove(handler);
        }

        public void Dispatch(Signal signal, SignalContext context)
        {
            if (signal == null)
                return;

            if (context == null)
                context = new SignalContext(gameObject);

            // A handler may register or unregister another handler while processing the signal.
            _dispatchBuffer.Clear();
            _dispatchBuffer.AddRange(_handlers);

            for (var i = 0; i < _dispatchBuffer.Count; i++)
            {
                var handler = _dispatchBuffer[i];
                if (!IsValidHandler(handler) || !handler.CanHandle(signal))
                    continue;

                try
                {
                    handler.Handle(signal, context);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            try
            {
                SignalDispatched?.Invoke(signal, context);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void AddHandlerIfMissing(ISignalHandler handler)
        {
            if (IsValidHandler(handler) && !_handlers.Contains(handler))
                _handlers.Add(handler);
        }

        private static bool IsValidHandler(ISignalHandler handler)
        {
            if (ReferenceEquals(handler, null))
                return false;

            return !(handler is UnityEngine.Object unityObject) || unityObject != null;
        }
    }
}