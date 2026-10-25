namespace DogfightThreatAI.Core.AI.BehaviorTree
{
    /// <summary>响应式序列，每步重新检查条件；所有子节点成功才成功。</summary>
    public sealed class SequenceNode<T> : IBehaviorNode<T>
    {
        readonly IBehaviorNode<T>[] _children;
        public SequenceNode(params IBehaviorNode<T>[] children) { _children = children; }
        public NodeStatus Tick(T context)
        {
            foreach (var child in _children)
            { var result = child.Tick(context); if (result != NodeStatus.Success) return result; }
            return NodeStatus.Success;
        }
    }
}
