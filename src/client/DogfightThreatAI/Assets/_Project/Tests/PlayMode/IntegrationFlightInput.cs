using DogfightThreatAI.Core.Input;
using UnityEngine;

namespace DogfightThreatAI.Tests.Menu
{
    // 仅用于 PlayMode 集成测试；通过与键盘相同的输入接口确认切场景后仍能飞行。
    public sealed class IntegrationFlightInput : MonoBehaviour, IPlayerInputSource
    {
        public FlightInput ReadInput(float deltaTime)
        {
            return new FlightInput(1f, 0f);
        }
    }
}
