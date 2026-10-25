using DogfightThreatAI.Core.Config;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Config
{
    [CreateAssetMenu(menuName = "DogfightThreatAI/Weapon", fileName = "Cannon")]
    public sealed class WeaponData : ScriptableObject
    {
        [SerializeField] string weaponId = "cannon";
        [SerializeField, Min(.05f)] float shotInterval = .45f;
        [SerializeField, Min(1)] float projectileSpeed = 46f;
        [SerializeField, Min(.1f)] float lifetime = 1.5f;
        [SerializeField, Min(.01f)] float radius = .09f;
        [SerializeField, Min(.1f)] float muzzleOffset = .85f;
        public WeaponSpec ToSpec() => new WeaponSpec(weaponId, shotInterval, projectileSpeed, lifetime, radius, muzzleOffset);
    }
}
