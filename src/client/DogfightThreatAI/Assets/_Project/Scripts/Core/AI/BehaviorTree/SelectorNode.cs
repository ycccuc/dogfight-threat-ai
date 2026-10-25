namespace DogfightThreatAI.Core.AI.BehaviorTree
{
    /// <summary>响应式优先选择：每步从高优先级开始，紧急分支能打断低优先级行为。</summary>
    public sealed class SelectorNode<T> : IBehaviorNode<T>
    {
        readonly IBehaviorNode<T>[] _children;
        public SelectorNode(params IBehaviorNode<T>[] children) { _children = children; }
        public NodeStatus Tick(T context)
        {
            foreach (var child in _children)
            { var result = child.Tick(context); if (result != NodeStatus.Failure) return result; }
            return NodeStatus.Failure;
        }
    }
}
