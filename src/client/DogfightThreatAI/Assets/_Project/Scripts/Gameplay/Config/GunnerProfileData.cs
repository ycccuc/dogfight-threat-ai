using DogfightThreatAI.Core.AI;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Config
{
    [CreateAssetMenu(menuName = "DogfightThreatAI/Gunner Difficulty", fileName = "GunnerDifficulty")]
    public sealed class GunnerProfileData : ScriptableObject
    {
        [SerializeField] string displayName = "标准";
        [SerializeField, TextArea] string description = "保持警惕，寻找射击机会。";
        [SerializeField] bool useAStar = true;
        [SerializeField, Min(.02f)] float observationInterval = .15f;
        [SerializeField, Range(0, 1)] float prediction = .5f;
        [SerializeField, Range(1, 20)] float aimToleranceDegrees = 6f;
        [SerializeField, Min(0)] float fireDelay = .2f;
        [Tooltip("AI 主动放弃部分射击机会的节奏，不改变共用机炮的硬冷却。")]
        [SerializeField, Min(.05f)] float shotSpacing = .85f;
        [SerializeField, Min(1)] float cruiseSpeed = 10f;
        [SerializeField, Min(1)] float turnRate = 220f;
        [SerializeField, Min(1)] float retreatDistance = 3f;
        [SerializeField, Min(2)] float engageDistance = 7f;
        public string DisplayName => displayName;
        public string Description => description;
        public GunnerProfile ToSpec() => new GunnerProfile(useAStar, observationInterval, prediction,
            aimToleranceDegrees * Mathf.Deg2Rad, fireDelay, cruiseSpeed, turnRate, retreatDistance, engageDistance, shotSpacing);
    }
}
