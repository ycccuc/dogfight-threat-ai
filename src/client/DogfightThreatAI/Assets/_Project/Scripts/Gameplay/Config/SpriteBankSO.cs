using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dogfight.Gameplay
{
    /// <summary>
    /// 贴图库：**用语义键取图，代码里不出现任何贴图路径或文件名**。
    ///
    /// 为什么值得单独做一层：
    ///   我们的素材策略是"先用第三方占位，之后再换成自己的"。
    ///   如果代码里写 Resources.Load("Ships/ship_0003") 或者直接拖引用，
    ///   换素材时就得满工程改代码 + 重连引用。
    ///   有了贴图库，换素材 = 在这个资产的 Inspector 里改一格，代码一行不动。
    ///
    /// 用法：PlaneAgent / HUD 只认 Keys 里的常量键。
    /// 创建方式：Project 窗口右键 → Create → Dogfight → 贴图库
    /// </summary>
    [CreateAssetMenu(fileName = "SpriteBank", menuName = "Dogfight/贴图库", order = 2)]
    public sealed class SpriteBankSO : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("语义键，用 Keys 里的常量。")]
            public string Key;

            [Tooltip("对应的贴图。换素材时只改这里。")]
            public Sprite Sprite;
        }

        [Tooltip("语义键 → 贴图。缺哪个会有警告，不会静默变空白。")]
        [SerializeField] Entry[] _entries = Array.Empty<Entry>();

        [Tooltip("勾上则在打包时预建索引（推荐）；关闭则首次访问时懒建。")]
        [SerializeField] bool _buildIndexOnEnable = true;

        Dictionary<string, Sprite> _index;

        /// <summary>项目里所有语义键集中在这里定义，避免各处手写字符串拼错。</summary>
        public static class Keys
        {
            public const string PlayerPlane = "plane.player";
            public const string EnemyPlaneScout = "plane.enemy.scout";
            public const string EnemyPlaneHeavy = "plane.enemy.heavy";
            public const string BulletCannon = "bullet.cannon";
            public const string BulletMissile = "bullet.missile";
            public const string VfxExplosion = "vfx.explosion";
            public const string UiRadarBlipAlly = "ui.radar.ally";
            public const string UiRadarBlipEnemy = "ui.radar.enemy";
        }

        void OnEnable()
        {
            if (_buildIndexOnEnable) BuildIndex();
        }

        public void BuildIndex()
        {
            if (_index == null) _index = new Dictionary<string, Sprite>(_entries.Length, StringComparer.Ordinal);
            else _index.Clear();

            for (int i = 0; i < _entries.Length; i++)
            {
                Entry e = _entries[i];
                if (string.IsNullOrEmpty(e.Key)) continue;
                if (e.Sprite == null) continue;
                _index[e.Key] = e.Sprite;
            }
        }

        public bool TryGet(string key, out Sprite sprite)
        {
            if (_index == null) BuildIndex();
            return _index.TryGetValue(key, out sprite);
        }

        /// <summary>
        /// 取图；缺失时返回 null 并**打一条明确的警告**。
        /// 静默返回 null 会让"素材没接上"变成一个很难查的空白画面 Bug。
        /// </summary>
        public Sprite Get(string key)
        {
            if (TryGet(key, out Sprite sprite)) return sprite;
            Debug.LogWarning("[SpriteBank] 缺少键 \"" + key + "\"，请在 " + name + " 里补上贴图。");
            return null;
        }

        /// <summary>校验：返回所有缺失的键（没有缺失则为空数组）。用于 CI / 场景检查。</summary>
        public string[] FindMissingKeys(params string[] requiredKeys)
        {
            var missing = new List<string>();
            for (int i = 0; i < requiredKeys.Length; i++)
            {
                if (!TryGet(requiredKeys[i], out _)) missing.Add(requiredKeys[i]);
            }
            return missing.ToArray();
        }
    }
}
