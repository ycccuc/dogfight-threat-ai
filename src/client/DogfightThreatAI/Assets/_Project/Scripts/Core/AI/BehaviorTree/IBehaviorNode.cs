namespace DogfightThreatAI.Core.AI.BehaviorTree
{
    public interface IBehaviorNode<in T>
    { // 推进一次节点判断或动作，并返回本步执行状态。
      // 推进一次节点判断或动作，并返回本步执行状态。
        NodeStatus Tick(T context);
    }
}
