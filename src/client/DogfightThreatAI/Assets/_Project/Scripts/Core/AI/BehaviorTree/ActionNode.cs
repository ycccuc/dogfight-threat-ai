using System;
namespace DogfightThreatAI.Core.AI.BehaviorTree
{
    public sealed class ActionNode<T> : IBehaviorNode<T>
    {
        readonly Func<T, NodeStatus> _action;
        public ActionNode(Func<T, NodeStatus> action) { _action = action ?? throw new ArgumentNullException(nameof(action)); }
        public NodeStatus Tick(T context) => _action(context);
    }
}
