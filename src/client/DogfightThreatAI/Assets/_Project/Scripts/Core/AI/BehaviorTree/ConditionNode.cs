using System;
namespace DogfightThreatAI.Core.AI.BehaviorTree
{
    public sealed class ConditionNode<T> : IBehaviorNode<T>
    {
        readonly Func<T, bool> _condition;
        public ConditionNode(Func<T, bool> condition) { _condition = condition ?? throw new ArgumentNullException(nameof(condition)); }
        public NodeStatus Tick(T context) => _condition(context) ? NodeStatus.Success : NodeStatus.Failure;
    }
}
