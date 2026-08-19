using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
#endif

namespace ShinySSRR {

    static class InputProxy {

        public static void SetupEventSystem() {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return;
            var standaloneModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (standaloneModule != null) {
                Object.Destroy(standaloneModule);
                if (eventSystem.GetComponent<InputSystemUIInputModule>() == null) {
                    eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                }
            }
#endif
        }

        public static bool GetKeyDown(KeyCode keyCode) {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(keyCode);
#elif ENABLE_INPUT_SYSTEM
            return GetKeyInternal(keyCode, true);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        static bool GetKeyInternal(KeyCode keyCode, bool down) {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            var control = GetKeyControl(keyboard, keyCode);
            if (control == null) return false;
            return down ? control.wasPressedThisFrame : control.isPressed;
        }

        static KeyControl GetKeyControl(Keyboard keyboard, KeyCode keyCode) {
            switch (keyCode) {
                case KeyCode.Space: return keyboard.spaceKey;
                default:
                    return null;
            }
        }
#endif
    }
}
