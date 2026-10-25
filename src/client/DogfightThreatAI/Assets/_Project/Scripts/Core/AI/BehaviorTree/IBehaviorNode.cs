namespace DogfightThreatAI.Core.AI.BehaviorTree
{
    public interface IBehaviorNode<in T> { NodeStatus Tick(T context); }
}
