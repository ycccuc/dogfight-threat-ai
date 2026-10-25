using DogfightThreatAI.Core.Input;
using UnityEngine;

namespace DogfightThreatAI.Tests.PlayMode
{
    /// <summary>仅用于集成测试的输入源；通过真实 PlayerController 和 FlightModel 驱动玩家。</summary>
    public sealed class BattleTestInputSource : MonoBehaviour, IPlayerInputSource
    {
        public FlightInput DesiredInput;

        public FlightInput ReadInput(float deltaTime) => DesiredInput;
    }
}
