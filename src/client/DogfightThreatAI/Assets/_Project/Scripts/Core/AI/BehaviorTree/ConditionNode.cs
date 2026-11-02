using System;

namespace DogfightThreatAI.Core.AI.BehaviorTree
{
    public sealed class ConditionNode<T> : IBehaviorNode<T>
    {
        readonly Func<T, bool> _condition;

        // 保存条件委托，构建时拒绝缺失的判定实现。
        public ConditionNode(Func<T, bool> condition)
        {
            _condition = condition ?? throw new ArgumentNullException(nameof(condition));
        }

        // 把条件真假转换为行为树的成功或失败状态。
        public NodeStatus Tick(T context)
        {
            return _condition(context) ? NodeStatus.Success : NodeStatus.Failure;
        }
    }
}
