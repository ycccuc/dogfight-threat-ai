namespace DogfightThreatAI.Core.Combat
{
    /// <summary>
    /// 本固定步中公开的伤害许可。Fraction 是步内 [0,1] 的时间范围；
    /// 攻击编号为单调递增的正数，同一编号最多命中一次；窗口必须非空且有效。
    /// </summary>
    public readonly struct AttackWindow
    {
        public readonly int AttackId;
        public readonly bool IsActive;
        public readonly float StartFraction;
        public readonly float EndFraction;

        public static AttackWindow Inactive => default;

        public AttackWindow(int attackId, bool isActive, float startFraction = 0f, float endFraction = 1f)
        {
            AttackId = attackId;
            IsActive = isActive;
            StartFraction = startFraction;
            EndFraction = endFraction;
        }
    }
}
