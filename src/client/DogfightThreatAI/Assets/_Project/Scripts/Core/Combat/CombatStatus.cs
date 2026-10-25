namespace DogfightThreatAI.Core.Combat
{
    /// <summary>只读的战斗状态；飞行 AI 读取它，但不能直接扣血或跳过无敌帧。</summary>
    public readonly struct CombatStatus
    {
        public readonly int PlayerHealth;
        public readonly int MaxPlayerHealth;
        public readonly int HitSequence;
        public readonly float InvulnerabilityRemaining;
        public readonly float ElapsedTime;

        public bool IsPlayerAlive => PlayerHealth > 0;
        public float Difficulty01 => System.Math.Max(0f, System.Math.Min(1f, ElapsedTime / 60f));

        public CombatStatus(int playerHealth, int maxPlayerHealth, int hitSequence,
                            float invulnerabilityRemaining, float elapsedTime)
        {
            PlayerHealth = playerHealth;
            MaxPlayerHealth = maxPlayerHealth;
            HitSequence = hitSequence;
            InvulnerabilityRemaining = invulnerabilityRemaining;
            ElapsedTime = elapsedTime;
        }
    }

    public interface ICombatStatusView
    {
        CombatStatus Status { get; }
    }
}
