using System;

namespace DogfightThreatAI.Core.AI.BehaviorTree
{
    public sealed class ActionNode<T> : IBehaviorNode<T>
    {
        readonly Func<T, NodeStatus> _action;

        // 保存动作委托，构建时拒绝缺失的行为实现。
        public ActionNode(Func<T, NodeStatus> action)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
        }

        // 执行当前动作，并把其运行状态返回给父节点。
        public NodeStatus Tick(T context)
        {
            return _action(context);
        }
    }
}
