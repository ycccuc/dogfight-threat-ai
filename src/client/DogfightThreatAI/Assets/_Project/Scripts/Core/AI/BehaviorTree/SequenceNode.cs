using System;

namespace DogfightThreatAI.Core.AI.BehaviorTree
{
    /// <summary>响应式序列，每步重新检查条件；所有子节点成功才成功。</summary>
    public sealed class SequenceNode<T> : IBehaviorNode<T>
    {
        readonly IBehaviorNode<T>[] _children;

        // 校验并复制子节点列表，固定顺序执行的步骤。
        public SequenceNode(params IBehaviorNode<T>[] children)
        {
            if (children == null)
            {
                throw new ArgumentNullException(nameof(children));
            }
            foreach (var child in children)
            {
                if (child == null)
                {
                    throw new ArgumentException("Child nodes must not be null.", nameof(children));
                }
            }
            _children = (IBehaviorNode<T>[])children.Clone();
        }

        // 依次推进子节点，遇到失败或运行中状态立即返回。
        public NodeStatus Tick(T context)
        {
            foreach (var child in _children)
            {
                var result = child.Tick(context);
                if (result != NodeStatus.Success)
                {
                    return result;
                }
            }
            return NodeStatus.Success;
        }
    }
}
