using UnityEngine;

namespace Xuan.Prometheus.Component
{
    /// <summary>
    /// 提供运行时自由漫游相机控制：WASD 沿相机方向移动，Ctrl 与空格控制世界空间升降，鼠标控制朝向，滚轮调整视野角。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class WanderCamera : MonoBehaviour
    {
        /// <summary>相机每秒移动的世界单位距离。</summary>
        [SerializeField, Min(0f)] private float movementSpeed = 10f;

        /// <summary>是否使用不受 Time.timeScale 影响的时间，使暂停状态下仍可漫游观察。</summary>
        [SerializeField] private bool useUnscaledTime = true;

        /// <summary>鼠标每个输入单位对应的旋转角度。</summary>
        [SerializeField, Min(0f)] private float mouseSensitivity = 2f;

        /// <summary>启用时仅在按住鼠标右键期间旋转相机；关闭后鼠标移动会持续控制视角。</summary>
        [SerializeField] private bool requireRightMouseButton = true;

        /// <summary>旋转视角期间是否锁定并隐藏鼠标，结束旋转时恢复进入前的鼠标状态。</summary>
        [SerializeField] private bool lockCursorWhileLooking = true;

        /// <summary>相机允许向下俯视的最小俯仰角。</summary>
        [SerializeField, Range(-89f, 89f)] private float minimumPitch = -89f;

        /// <summary>相机允许向上仰视的最大俯仰角。</summary>
        [SerializeField, Range(-89f, 89f)] private float maximumPitch = 89f;

        /// <summary>滚轮每个刻度改变的垂直视野角度。</summary>
        [SerializeField, Min(0f)] private float scrollFovSensitivity = 5f;

        /// <summary>滚轮缩放允许达到的最小垂直视野角。</summary>
        [SerializeField, Range(1f, 179f)] private float minimumFov = 20f;

        /// <summary>滚轮缩放允许达到的最大垂直视野角。</summary>
        [SerializeField, Range(1f, 179f)] private float maximumFov = 100f;

        /// <summary>当前脚本控制的同节点相机组件。</summary>
        private Camera controlledCamera;

        /// <summary>当前相机绕世界 Y 轴的水平旋转角。</summary>
        private float yaw;

        /// <summary>当前相机绕自身 X 轴的俯仰角。</summary>
        private float pitch;

        /// <summary>记录右键视角控制开始前的鼠标锁定模式。</summary>
        private CursorLockMode cursorLockModeBeforeLook;

        /// <summary>记录右键视角控制开始前的鼠标可见状态。</summary>
        private bool cursorVisibleBeforeLook;

        /// <summary>标记当前脚本是否已经接管鼠标锁定状态。</summary>
        private bool isCursorCaptured;

        /// <summary>缓存由 RequireComponent 保证存在的同节点 Camera。</summary>
        private void Awake()
        {
            controlledCamera = GetComponent<Camera>();
        }

        /// <summary>组件重新启用时重新读取 Transform 朝向，避免外部在禁用期间修改旋转后产生视角跳变。</summary>
        private void OnEnable()
        {
            SynchronizeAnglesWithTransform();
        }

        /// <summary>组件停用时恢复被当前脚本接管前的鼠标锁定与可见状态。</summary>
        private void OnDisable()
        {
            ReleaseCursor();
        }

        /// <summary>每帧依次处理视角、位移和视野角输入，使本帧移动方向立即使用更新后的相机朝向。</summary>
        private void Update()
        {
            UpdateLook();
            UpdateMovement();
            UpdateFieldOfView();
        }

        /// <summary>读取鼠标移动并更新相机偏航角与受限俯仰角，同时管理右键观察期间的鼠标锁定。</summary>
        private void UpdateLook()
        {
            bool isLooking = !requireRightMouseButton || UnityEngine.Input.GetMouseButton(1);
            if (isLooking)
                CaptureCursor();
            else
                ReleaseCursor();

            if (!isLooking)
                return;

            yaw += UnityEngine.Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - UnityEngine.Input.GetAxisRaw("Mouse Y") * mouseSensitivity, minimumPitch, maximumPitch);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>读取 WASD、Ctrl 和空格，组合相机平面方向与世界竖直方向，并对斜向输入归一化后执行位移。</summary>
        private void UpdateMovement()
        {
            float horizontalInput = UnityEngine.Input.GetAxisRaw("Horizontal");
            float forwardInput = UnityEngine.Input.GetAxisRaw("Vertical");
            float verticalInput = (UnityEngine.Input.GetKey(KeyCode.Space) ? 1f : 0f) - (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl) ? 1f : 0f);
            Vector3 movementDirection = transform.right * horizontalInput + transform.forward * forwardInput + Vector3.up * verticalInput;
            if (movementDirection.sqrMagnitude > 1f)
                movementDirection.Normalize();

            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            transform.position += movementDirection * movementSpeed * deltaTime;
        }

        /// <summary>读取鼠标滚轮并在配置范围内修改 Camera 的垂直视野角。</summary>
        private void UpdateFieldOfView()
        {
            float scrollInput = UnityEngine.Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scrollInput, 0f))
                return;

            controlledCamera.fieldOfView = Mathf.Clamp(controlledCamera.fieldOfView - scrollInput * scrollFovSensitivity, minimumFov, maximumFov);
        }

        /// <summary>把 Transform 当前欧拉角转换为适合连续鼠标累加的偏航角和有符号俯仰角。</summary>
        private void SynchronizeAnglesWithTransform()
        {
            Vector3 eulerAngles = transform.eulerAngles;
            yaw = eulerAngles.y;
            pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, eulerAngles.x), minimumPitch, maximumPitch);
        }

        /// <summary>在配置允许时保存并接管鼠标状态，使连续旋转不会受到屏幕边缘限制。</summary>
        private void CaptureCursor()
        {
            if (!lockCursorWhileLooking || isCursorCaptured)
                return;

            cursorLockModeBeforeLook = Cursor.lockState;
            cursorVisibleBeforeLook = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            isCursorCaptured = true;
        }

        /// <summary>结束视角控制时恢复脚本接管前的鼠标锁定模式与可见状态。</summary>
        private void ReleaseCursor()
        {
            if (!isCursorCaptured)
                return;

            Cursor.lockState = cursorLockModeBeforeLook;
            Cursor.visible = cursorVisibleBeforeLook;
            isCursorCaptured = false;
        }

        /// <summary>在 Inspector 修改参数时约束上下界关系，保证运行时角度与视野角的 Clamp 区间始终有效。</summary>
        private void OnValidate()
        {
            movementSpeed = Mathf.Max(0f, movementSpeed);
            mouseSensitivity = Mathf.Max(0f, mouseSensitivity);
            scrollFovSensitivity = Mathf.Max(0f, scrollFovSensitivity);
            minimumPitch = Mathf.Clamp(minimumPitch, -89f, 89f);
            maximumPitch = Mathf.Clamp(maximumPitch, minimumPitch, 89f);
            minimumFov = Mathf.Clamp(minimumFov, 1f, 179f);
            maximumFov = Mathf.Clamp(maximumFov, minimumFov, 179f);
        }
    }
}
