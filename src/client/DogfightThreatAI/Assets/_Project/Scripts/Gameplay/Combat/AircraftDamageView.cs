using UnityEngine;

namespace DogfightThreatAI.Gameplay.Combat
{
    /// <summary>受击闪烁、机身震动；不改变飞行根节点或生命状态。</summary>
    public sealed class AircraftDamageView : MonoBehaviour
    {
        SpriteRenderer[] _sprites;
        Color[] _colors;
        PlayerView _view;
        float _lastHit = -10;
        void Awake()
        {
            _sprites = GetComponentsInChildren<SpriteRenderer>(); _colors = new Color[_sprites.Length];
            for (int i = 0; i < _sprites.Length; i++) _colors[i] = _sprites[i].color;
            _view = GetComponentInChildren<PlayerView>();
        }
        public void PlayHit() { _lastHit = Time.time; if (_view != null) _view.PlayHitShake(); }
        public void Present(bool alive, float protection)
        {
            bool dim = protection > 0 && ((int)(Time.time * 12) & 1) == 0;
            for (int i = 0; i < _sprites.Length; i++)
            {
                if (_sprites[i] == null) continue;
                Color tint = _colors[i];
                if (!alive) tint = new Color(.35f, .35f, .35f, .65f);
                else if (Time.time - _lastHit < .15f) tint = new Color(1, .45f, .35f);
                else if (dim) tint.a *= .3f;
                _sprites[i].color = tint;
            }
        }
    }
}
