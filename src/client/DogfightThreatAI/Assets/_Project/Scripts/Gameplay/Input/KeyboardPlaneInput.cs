using Dogfight.Ai;
using UnityEngine;

namespace Dogfight.Gameplay
{
    /// <summary>
    /// 键鼠输入 → IPlaneInput。
    ///
    /// 刻意做成**普通类而不是 MonoBehaviour**：
    /// 这样 PlaneAgent 只要换一个 IPlaneInput 实现就变成 AI 控制，
    /// 不需要禁用/启用组件、也不需要判断"这是不是我控制的飞机"。
    ///
    /// 操作：W/S 油门，A/D 转向，鼠标瞄准，左键机炮，右键导弹。
    /// 转向符号：本类输出 +1 = 左转（逆时针），与 IPlaneInput 的约定一致；
    /// 而 Unity 的 Horizontal 是 A=-1 / D=+1，所以这里要取负号。
    /// </summary>
    public sealed class KeyboardPlaneInput : IPlaneInput
    {
        readonly Camera _camera;

        int _lastPolledFrame = -1;
        float _throttle;
        float _turn;
        bool _firePrimary;
        bool _fireSecondary;
        Vec2 _aim;

        public KeyboardPlaneInput(Camera camera)
        {
            _camera = camera;
        }

        /// <summary>
        /// 每帧采一次。之所以要自己判帧：Step() 是按固定步长调用的，
        /// 一个渲染帧里可能被调多次，而 Input 每帧只应读一次（否则会把同一次按键
        /// 在多步里重复计入，且 GetMouseButtonDown 只在第一帧为真）。
        /// </summary>
        public void Poll(Vector3 worldPosition)
        {
            if (Time.frameCount == _lastPolledFrame) return;
            _lastPolledFrame = Time.frameCount;

            // 沿用旧输入系统：本工程没有装 com.unity.inputsystem
            _throttle = Input.GetAxisRaw("Vertical");
            _turn = -Input.GetAxisRaw("Horizontal");

            _firePrimary = Input.GetMouseButton(0);
            _fireSecondary = Input.GetMouseButton(1);

            if (_camera != null)
            {
                Vector3 screen = Input.mousePosition;
                screen.z = Mathf.Abs(_camera.transform.position.z);
                Vector3 world = _camera.ScreenToWorldPoint(screen);
                _aim = new Vec2(world.x, world.y);
            }
            else
            {
                _aim = new Vec2(worldPosition.x, worldPosition.y);
            }
        }

        public float Throttle => _throttle;

        public float Turn => _turn;

        public bool FirePrimary => _firePrimary;

        public bool FireSecondary => _fireSecondary;

        public Vec2 AimPoint => _aim;
    }
}
