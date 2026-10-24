using System;

namespace DogfightThreatAI.Core.AI
{
    /// <summary>Owns the pursuit budget and game-time deadlines, independently of aircraft motion.</summary>
    public sealed class PursuitBoostModel
    {
        public const float Capacity = 60f, StartCost = 30f, RechargeDelay = .8f, RechargePerSecond = 15f;
        float _now, _endsAt, _readyAt, _rechargeAt;
        public float Energy { get; private set; } = Capacity;
        public bool IsBoosting { get; private set; }
        public float Remaining => IsBoosting ? MathF.Max(0f, _endsAt - _now) : 0f;
        public float CooldownRemaining => MathF.Max(0f, _readyAt - _now);

        public void Reset(float now = 0f)
        {
            _now = now; _endsAt = _readyAt = _rechargeAt = now;
            Energy = Capacity; IsBoosting = false;
        }

        public void Advance(float now)
        {
            if (!Finite(now) || now < _now) return;
            if (IsBoosting && now >= _endsAt - .000001f) { IsBoosting = false; _rechargeAt = _endsAt + RechargeDelay; }
            if (!IsBoosting) Energy = MathF.Min(Capacity, Energy + MathF.Max(0f, now - MathF.Max(_now, _rechargeAt)) * RechargePerSecond);
            _now = now;
        }

        public bool TryStart(float now, float duration, PursuitDifficultyProfile profile)
        {
            if (!Finite(now) || now < _now) return false;
            Advance(now);
            if (!Finite(duration) || duration < .2f || duration > profile.BoostDuration + .00001f ||
                IsBoosting || CooldownRemaining > .00001f || Energy < StartCost) return false;
            Energy -= StartCost; IsBoosting = true;
            _endsAt = now + duration; _readyAt = now + profile.BoostCooldown;
            _rechargeAt = _endsAt + RechargeDelay;
            return true;
        }

        public void Cancel(float now)
        {
            if (!Finite(now) || now < _now) return;
            Advance(now);
            if (!IsBoosting) return;
            IsBoosting = false; _endsAt = now; _rechargeAt = now + RechargeDelay;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
