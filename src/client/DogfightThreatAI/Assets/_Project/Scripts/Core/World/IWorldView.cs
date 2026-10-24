namespace DogfightThreatAI.Core.World
{
    /// <summary>
    /// AI 原型的最小只读世界契约。一次 ReadInput 期间集合必须保持稳定，EntityId 唯一。
    /// 实现方按值返回快照，不向 AI 暴露可变模拟对象。接入 BattleSimulation 前由岗位 A 评审。
    /// </summary>
    public interface IWorldView
    {
        int AircraftCount { get; }
        AircraftSnapshot GetAircraft(int index);
    }
}
