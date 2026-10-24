using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Gameplay.InputSource;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Combat
{
    /// <summary>Displays the real launch axis, then its committed direction. Charge has no forward marker.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ChaseAiInputSource))]
    public sealed class AttackCueView : MonoBehaviour
    {
        const int HaloPoints = 40;
        ChaseAiInputSource _source;
        PlayerController _aircraft;
        PlayerView _view;
        Material _material;
        LineRenderer _streak, _axis, _halo;
        SpriteRenderer[] _sprites;
        Color[] _baseColors;

        public bool IsShowingCue { get; private set; }

        void Awake()
        {
            _source = GetComponent<ChaseAiInputSource>();
            _aircraft = GetComponent<PlayerController>();
            _view = GetComponentInChildren<PlayerView>();
            _sprites = GetComponentsInChildren<SpriteRenderer>();
            _baseColors = new Color[_sprites.Length];
            for (int i = 0; i < _sprites.Length; i++) _baseColors[i] = _sprites[i].color;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return;
            _material = new Material(shader) { name = "AttackCue_Runtime", hideFlags = HideFlags.HideAndDontSave };
            _streak = CreateLine("AttackCue_ChargeStreak", .14f, true);
            _axis = CreateLine("AttackCue_LockedAxis", .025f, true);
            _halo = CreateLine("AttackCue_CommitHalo", .022f, false);
            _halo.loop = true;
        }

        LineRenderer CreateLine(string label, float width, bool taper)
        {
            var root = new GameObject(label);
            root.transform.SetParent(transform, false);
            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.widthMultiplier = width;
            line.widthCurve = taper
                ? new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.65f, .58f), new Keyframe(1f, .06f))
                : AnimationCurve.Constant(0f, 1f, 1f);
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingLayerID = _sprites.Length > 0 ? _sprites[0].sortingLayerID : 0;
            line.sortingOrder = (_sprites.Length > 0 ? _sprites[0].sortingOrder : 0) - 1;
            line.positionCount = 0;
            line.enabled = false;
            return line;
        }

        void LateUpdate()
        {
            if (_source == null || !_source.isActiveAndEnabled || _aircraft == null || !_aircraft.IsInitialized)
            {
                HideLines();
                SetAircraftTint(AttackRunPhase.NoTarget, 0f, false);
                return;
            }
            AttackRunPhase phase = _source.Phase;
            float progress = Mathf.Clamp01(1f - _source.PhaseRemaining / PursuitChargeInputSource.AimSeconds);
            bool committed = phase == AttackRunPhase.Windup && _source.IsAimLocked;
            SetAircraftTint(phase, progress, committed);
            if (_streak == null || phase != AttackRunPhase.Windup || _source.PreviewPointCount < 2)
            {
                HideLines();
                return;
            }

            // ReadInput precedes the flight step. Live aiming follows the displayed nose.
            // Commitment instead marks the fixed future launch lane while the aircraft
            // finishes its already selected bank into that lane.
            Vec2 heading = committed ? _source.LockedDirection : _aircraft.State.Forward;
            if (heading.SqrMagnitude < .000001f) { HideLines(); return; }
            var direction = new Vector3(heading.X, heading.Y, 0f).normalized;
            Vector3 center = _view != null ? _view.RenderPosition : transform.position;
            center.z = transform.position.z + .05f;
            Vec2 launch = _source.GetPreviewPoint(0);
            Vector3 laneOrigin = committed ? new Vector3(launch.X, launch.Y, center.z) : center;
            float length = Vec2.Distance(_source.GetPreviewPoint(0), _source.GetPreviewPoint(_source.PreviewPointCount - 1));
            length = Mathf.Clamp(length, 6f, 18f);
            Vector3 start = laneOrigin + direction * .5f;
            Vector3 end = laneOrigin + direction * length;
            Color cue = committed ? new Color(1f, .65f, .2f) : new Color(1f, .83f, .32f);
            float intensity = Mathf.Lerp(.65f, 1f, _source.Aggression01);
            _streak.widthMultiplier = committed ? .10f : .14f;
            _axis.widthMultiplier = committed ? .035f : .025f;
            SetLane(_streak, start, end, cue, (committed ? .34f : Mathf.Lerp(.18f, .29f, progress)) * intensity, 0f);
            SetLane(_axis, start, end, cue, (committed ? 1f : Mathf.Lerp(.55f, .85f, progress)) * intensity, .035f);

            // A small aircraft-centred halo communicates preparation, without placing a
            // misleading impact marker or an arrow at the target's changing position.
            _halo.positionCount = HaloPoints;
            float radius = Mathf.Lerp(.79f, .64f, progress);
            for (int i = 0; i < HaloPoints; i++)
            {
                float angle = i * Mathf.PI * 2f / HaloPoints;
                _halo.SetPosition(i, center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }
            _halo.startColor = _halo.endColor = WithAlpha(cue, (committed ? .8f : .12f + progress * .25f) * intensity);
            _halo.enabled = true;
            IsShowingCue = true;
        }

        static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        static void SetLane(LineRenderer line, Vector3 start, Vector3 end, Color color, float startAlpha, float endAlpha)
        {
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startColor = WithAlpha(color, startAlpha);
            line.endColor = WithAlpha(color, endAlpha);
            line.enabled = true;
        }

        void SetAircraftTint(AttackRunPhase phase, float progress, bool committed)
        {
            if (_sprites == null) return;
            Color color = PhaseColor(phase);
            float blend = phase == AttackRunPhase.Windup ? (committed ? .68f : .22f + progress * .18f)
                : phase == AttackRunPhase.Charge ? .7f : phase == AttackRunPhase.Recover ? .55f : 0f;
            if (phase == AttackRunPhase.Windup && committed) color = new Color(1f, .88f, .6f);
            for (int i = 0; i < _sprites.Length; i++)
                if (_sprites[i] != null) _sprites[i].color = Color.Lerp(_baseColors[i], color, blend);
        }

        void HideLines()
        {
            IsShowingCue = false;
            if (_streak != null) _streak.enabled = false;
            if (_axis != null) _axis.enabled = false;
            if (_halo != null) _halo.enabled = false;
        }

        void OnDisable()
        {
            HideLines();
            SetAircraftTint(AttackRunPhase.NoTarget, 0f, false);
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public static Color PhaseColor(AttackRunPhase phase)
        {
            switch (phase)
            {
                case AttackRunPhase.Windup: return new Color(1f, .78f, .27f, 1f);
                case AttackRunPhase.Charge: return new Color(1f, .58f, .28f, 1f);
                case AttackRunPhase.Recover: return new Color(.55f, .72f, .87f, 1f);
                default: return Color.white;
            }
        }
    }
}
