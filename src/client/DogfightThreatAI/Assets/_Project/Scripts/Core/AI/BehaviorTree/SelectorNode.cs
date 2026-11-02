using System;

namespace DogfightThreatAI.Core.AI.BehaviorTree
{
    /// <summary>响应式优先选择：每步从高优先级开始，紧急分支能打断低优先级行为。</summary>
    public sealed class SelectorNode<T> : IBehaviorNode<T>
    {
        readonly IBehaviorNode<T>[] _children;

        // 校验并复制子节点列表，固定行为选择的优先级。
        public SelectorNode(params IBehaviorNode<T>[] children)
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
            // 调用方后续修改原数组，不应改变已构建的树。
            _children = (IBehaviorNode<T>[])children.Clone();
        }

        // 从高到低检查子节点，返回首个未失败行为的状态。
        public NodeStatus Tick(T context)
        {
            foreach (var child in _children)
            {
                var result = child.Tick(context);
                if (result != NodeStatus.Failure)
                {
                    return result;
                }
            }
            return NodeStatus.Failure;
        }
    }
}
