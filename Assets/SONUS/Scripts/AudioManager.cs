using System.Collections;
using Octo.Surge.Sonus;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Config")]
    public SONUS sonus;
    public Camera sceneCamera;
    public Transform audioHeading;
    public AudioSource voiceSource;
    public float headingRadius = 1.5f;

    [Header("Behavior")]
    public bool playNoTargetCue = false;

    // === NEW: Sonic mode control ===
    [SerializeField] private float cueFrequencySeconds = 30f;
    private Coroutine _periodicLoop;
    private Coroutine _movementLoop;

    // === NEW: Movement-cue tuning (anti-annoyance) ===
    [Header("Movement Cue Rules")]
    [SerializeField] private bool debugMovementCues = true;
    [SerializeField] private bool movementCuesEnabled = true;
    [SerializeField] private float lockCorridorDeg = 20f;       // user “on course” cone


    [SerializeField] private float minMoveSpeed = 0.3f;         // m/s

    [SerializeField] private float movementCueCooldown = 6f;   // seconds
    [SerializeField] private float ignoreIfCloserThan = 10f;    // meters

    [SerializeField] private float straightAheadMovementGrace = 0.5f; // allow movement cue shortly after SA
    private float _lastStraightAheadPlayTime = -999f;



    // --- Add with the other Movement-cue tuning fields ---
    // Anti-spam & hysteresis for straight-ahead
    [SerializeField] private float straightAheadDeg = 24f;        // instant fire threshold
    [SerializeField] private float straightAheadCooldown = 3f;    // min gap between straight-ahead calls
    [SerializeField] private float straightAheadRearmSeconds = 0.75f; // how long out of corridor before re-arming
    [SerializeField] private float straightAheadRearmExtraDeg = 6f;   // must exceed corridor by this extra angle to re-arm
    [SerializeField] private float recentLockGrace = 4f;     // seconds after being aligned we still consider "recently locked"

    private float _lastStraightAheadTime = -999f;
    private float _lastLockTime = -999f;
    private bool _wasLockedLastTick = false;
    // Re-arm bookkeeping
    private bool _straightAheadArmed = true;
    private float _leftCorridorAt = -999f;

    // movement state
    private float _lockTimer, _lastMoveCueTime = -999f;
    private Vector3 _lastTargetWorld;
    private double _lastSampleTime;
    private bool _haveLastSample;

    public enum Dir8 { N, NE, E, SE, S, SW, W, NW }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (voiceSource != null)
        {
            voiceSource.spatialBlend = 1f;
            voiceSource.playOnAwake = false;
            voiceSource.dopplerLevel = 0f;
        }
    }

    // --- PUBLIC control for UI ---
    public void StartSonic(float frequencySeconds)
    {
        ApplyFrequency(frequencySeconds);

        if (_periodicLoop != null) StopCoroutine(_periodicLoop);
        _periodicLoop = StartCoroutine(PeriodicCueLoop());

        if (_movementLoop != null) StopCoroutine(_movementLoop);
        _movementLoop = StartCoroutine(MovementCueLoop());
    }

    public void StopSonic()
    {
        if (_periodicLoop != null) StopCoroutine(_periodicLoop);
        if (_movementLoop != null) StopCoroutine(_movementLoop);
        _periodicLoop = _movementLoop = null;
        ResetMovementState();
        _wasLockedLastTick = false;
        _lastLockTime = -999f;
        _straightAheadArmed = true;
        _leftCorridorAt = -999f;
    }

    public void ApplyFrequency(float seconds)
    {
        cueFrequencySeconds = Mathf.Max(1f, seconds);
        // if running, restart periodic loop to apply immediately
        if (_periodicLoop != null)
        {
            StopCoroutine(_periodicLoop);
            _periodicLoop = StartCoroutine(PeriodicCueLoop());
        }
    }

    public void HearNow() => PlayForActiveTargetNow();

    // --- Periodic cue loop (direction + distance) ---
    private IEnumerator PeriodicCueLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(cueFrequencySeconds);
            PlayForActiveTargetNow();
        }
    }

    // --- Movement cue loop (hysteresis + cooldown) ---
    private IEnumerator MovementCueLoop()
    {
        if (!sceneCamera) yield break;

        const float sampleInterval = 0.2f; // 5 Hz
        while (true)
        {
            yield return new WaitForSeconds(sampleInterval);
            if (!movementCuesEnabled) continue;

            var active = ActiveTargetManager.Instance?.ActiveTarget;
            if (active == null) { ResetMovementState(); continue; }

            // Positions
            Vector3 player = sceneCamera.transform.position;
            Vector3 target = GeoUtils.GeoToWorld(new Vector2((float)active._Lat, (float)active._Lon));
            target.y = player.y;

            // Too close? (skip cues)
            float distance = Vector3.Distance(player, target);
            if (distance < ignoreIfCloserThan)
            {
                ResetMovementState(false);
                if (debugMovementCues) Debug.Log("[MC] Too close; ignoring.");
                continue;
            }

            // Camera vs target bearing (alignment)
            Vector3 fwd = sceneCamera.transform.forward; fwd.y = 0f;
            Vector3 toT = target - player; toT.y = 0f;
            if (toT.sqrMagnitude < 1e-4f)
            {
                ResetMovementState(false);
                if (debugMovementCues) Debug.Log("[MC] toT ~ 0");
                continue;
            }

            float camYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg; // 0°=N, 90°=E
            float tarYaw = Mathf.Atan2(toT.x, toT.z) * Mathf.Rad2Deg;
            float relAngle = Mathf.DeltaAngle(camYaw, tarYaw);          // -180..+180
            float absRel = Mathf.Abs(relAngle);
            float now = Time.time;

            // Corridor membership
            bool inCorridor = absRel <= lockCorridorDeg;
            if (inCorridor)
            {
                _lockTimer += sampleInterval;
                _lastLockTime = now;
                _leftCorridorAt = -999f;

                // Instant "Straight Ahead" (anti-spam + re-arm)
                if (_straightAheadArmed && absRel <= straightAheadDeg && (now - _lastStraightAheadTime) >= straightAheadCooldown)
                {
                    if (sonus != null && sonus._straightAhead != null)
                    {
                        PositionAudioHeading(player, target);
                        PlaySingle(sonus._straightAhead);
                        _lastStraightAheadTime = now;
                        _lastStraightAheadPlayTime = now; // track SA time ONLY
                        _straightAheadArmed = false;
                        BumpInterval();
                        if (debugMovementCues) Debug.Log($"[MC] StraightAhead PLAY (absRel={absRel:F1}°)");
                    }
                }
            }
            else
            {
                _lockTimer = 0f;

                // Start/track time since we left the corridor
                if (_leftCorridorAt < 0f) _leftCorridorAt = now;

                // Re-arm straight-ahead ONLY after time and angle thresholds
                bool rearmTime = (now - _leftCorridorAt) >= straightAheadRearmSeconds;
                bool rearmAngle = absRel >= (lockCorridorDeg + straightAheadRearmExtraDeg);
                if (rearmTime && rearmAngle) _straightAheadArmed = true;
            }

            // Detect corridor exit edge (for immediate movement cue)
            bool justExitedCorridor = _wasLockedLastTick && !inCorridor;
            _wasLockedLastTick = inCorridor;

            // --- Movement sampling (compute delta BEFORE updating cache) ---
            if (!_haveLastSample)
            {
                _lastTargetWorld = target;
                _lastSampleTime = now;
                _haveLastSample = true;
                continue;
            }

            float dt = now - (float)_lastSampleTime;
            if (dt <= 0f)
            {
                if (debugMovementCues) Debug.Log("[MC] dt<=0");
                continue;
            }

            Vector3 delta = target - _lastTargetWorld; delta.y = 0f;
            float disp = delta.magnitude;
            float speed = disp / dt;

            // Update cache AFTER measuring
            _lastTargetWorld = target;
            _lastSampleTime = now;

            // Speed-only gate (distance per sample is tiny at 5 Hz)
            if (speed < minMoveSpeed)
            {

                if (debugMovementCues) Debug.Log($"[MC] Too slow: {speed:F2} < {minMoveSpeed:F2}");
                continue;
            }

            // Movement cue on EXIT (if we were recently aligned)
            bool recentlyLocked = (now - _lastLockTime) <= recentLockGrace;

            bool canPlayMovement = (now - _lastMoveCueTime) >= movementCueCooldown || (now - _lastStraightAheadPlayTime) >= straightAheadMovementGrace;

            if (justExitedCorridor && recentlyLocked && canPlayMovement)
            {
                if (delta.sqrMagnitude > 1e-4f)
                {
                    float moveYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg; // 0°=N, CW
                    var d8 = BearingToDir8((moveYaw + 360f) % 360f);
                    PlayTargetMoving(d8, player, target);
                    _lastMoveCueTime = now;
                    BumpInterval();
                    if (debugMovementCues) Debug.Log($"[MC] Moving cue on EXIT: {d8} (absRel={absRel:F1}°)");
                }

            }
            else
            {
                // (Optional backup: persistence-based off-course cue)
                // bool offCourse = (absRel >= offCourseDeg) && recentlyLocked;
                // if (offCourse) _offTimer += sampleInterval; else _offTimer = 0f;
                // if (_offTimer >= offCoursePersistTime && (now - _lastMoveCueTime) >= movementCueCooldown)
                // {
                //     if (delta.sqrMagnitude > 1e-4f)
                //     {
                //         float moveYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                //         var d8 = BearingToDir8((moveYaw + 360f) % 360f);
                //         PlayTargetMoving(d8, player, target);
                //         _lastMoveCueTime = now;
                //         if (debugMovementCues) Debug.Log($"[MC] Moving cue PERSIST: {d8}");
                //     }
                //     _offTimer = 0f;
                // }
            }

            if (debugMovementCues)
            {
                Debug.Log($"[MC] rel={absRel:F1}°, speed={speed:F2}, lock={_lockTimer:F2}, armed={_straightAheadArmed}, " +
                          $"recent={recentlyLocked}, exited={justExitedCorridor}, cool={(now - _lastMoveCueTime):F1}");
            }
        }
    }



    private void ResetMovementState(bool clearLock = true)
    {
        if (clearLock) _lockTimer = 0f;

        _haveLastSample = false;
        _wasLockedLastTick = false;
        _straightAheadArmed = true;
        _leftCorridorAt = -999f;
    }

    // === Existing methods below (plus a tiny helper) ===

    private AudioClip GetMovingClip(Dir8 d)
    {
        var m = sonus?.targetMoving;
        if (m == null) return null;

        return d switch
        {
            Dir8.N => m.__north,     // uses your current field name
            Dir8.NE => m._northEast,
            Dir8.E => m._east,
            Dir8.SE => m._southEast,
            Dir8.S => m._south,
            Dir8.SW => m._southWest,
            Dir8.W => m._west,
            Dir8.NW => m._northWest,
            _ => null
        };
    }

    public void PlayTargetMoving(Dir8 d, Vector3 playerPos, Vector3 targetPos)
    {
        var clip = GetMovingClip(d);
        if (clip == null || voiceSource == null) return;
        PositionAudioHeading(playerPos, targetPos);
        voiceSource.Stop();
        voiceSource.clip = clip;
        voiceSource.Play();
    }

    public static Dir8 BearingToDir8(float bearingDeg)
    {
        int idx = Mathf.RoundToInt(((bearingDeg % 360f) + 360f) % 360f / 45f) % 8;
        return (Dir8)idx;
    }

    public void PlayForActiveTargetNow()
    {
        var active = ActiveTargetManager.Instance?.ActiveTarget;
        if (active == null)
        {
            if (playNoTargetCue && sonus != null && sonus._noTargets != null)
                PlaySingle(sonus._noTargets);
            return;
        }

        Vector3 playerPos = sceneCamera != null ? sceneCamera.transform.position : Vector3.zero;
        Vector3 targetPos = GeoUtils.GeoToWorld(new Vector2((float)active._Lat, (float)active._Lon));
        targetPos.y = playerPos.y;

        float distance = Vector3.Distance(playerPos, targetPos);
        float relativeAngle = ComputeRelativeAngleDeg(playerPos, targetPos);

        PositionAudioHeading(playerPos, targetPos);

        AudioClip dirClip = PickDirectionClip(relativeAngle);
        AudioClip distClip = PickDistanceClip(distance);

        PlaySequence(dirClip, distClip);
        AudioCueSlider.instance.ResetTimer();
    }

    public void PlayNewTargetClip(TargetActor actor)
    {
        if (sonus == null || actor == null) return;
        AudioClip clip = (TargetType)actor._Type == TargetType.STATIONARY ? sonus._newStationary : sonus._newDynamic;
        if (clip != null) PlaySingle(clip);
    }

    private void PlaySingle(AudioClip clip)
    {
        if (clip == null || voiceSource == null) return;
        if (_playRoutine != null) StopCoroutine(_playRoutine);
        voiceSource.Stop();
        voiceSource.clip = clip;
        voiceSource.Play();
    }

    private Coroutine _playRoutine;
    private void PlaySequence(params AudioClip[] clips)
    {
        if (_playRoutine != null) StopCoroutine(_playRoutine);
        _playRoutine = StartCoroutine(CoPlaySequence(clips));
    }

    private IEnumerator CoPlaySequence(AudioClip[] clips)
    {
        if (voiceSource == null) yield break;

        foreach (var c in clips)
        {
            if (c == null) continue;
            voiceSource.Stop();
            voiceSource.clip = c;
            voiceSource.Play();
            BumpInterval();
            yield return new WaitForSeconds(c.length);
        }
        _playRoutine = null;
    }

    private void PositionAudioHeading(Vector3 playerPos, Vector3 targetPos)
    {
        if (audioHeading == null) return;
        Vector3 dir = (targetPos - playerPos); dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        audioHeading.position = playerPos + dir.normalized * headingRadius;
        audioHeading.forward = dir.normalized;
    }

    private float ComputeRelativeAngleDeg(Vector3 playerPos, Vector3 targetPos)
    {
        if (sceneCamera == null) return 0f;
        Vector3 fwd = sceneCamera.transform.forward; fwd.y = 0f;
        Vector3 toT = (targetPos - playerPos); toT.y = 0f;
        float aCam = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        float aTar = Mathf.Atan2(toT.x, toT.z) * Mathf.Rad2Deg;
        return Mathf.DeltaAngle(aCam, aTar);
    }

    private AudioClip PickDirectionClip(float relAngle)
    {
        if (sonus == null) return null;
        float abs = Mathf.Abs(relAngle);
        if (abs <= 20f) return sonus._straightAhead;
        if (abs >= 170f) return sonus._directlyBehind;
        if (abs >= 110f) return sonus._behindYou;
        return null; // no side words yet
    }

    private AudioClip PickDistanceClip(float meters)
    {
        if (sonus == null || sonus.targetRange == null) return null;
        var r = sonus.targetRange;

        if (meters <= 10f) return r._10m;
        if (meters <= 20f) return r._20m;
        if (meters <= 30f) return r._30m;
        if (meters <= 40f) return r._40m;
        if (meters <= 50f) return r._50m;
        if (meters <= 60f) return r._60m;
        if (meters <= 70f) return r._70m;
        if (meters <= 80f) return r._80m;
        if (meters <= 90f) return r._90m;
        if (meters <= 100f) return r._100m;
        if (meters <= 125f) return r._125m;
        if (meters <= 150f) return r._150m;
        if (meters <= 175f) return r._175m;
        if (meters <= 200f) return r._200m;
        if (meters <= 250f) return r._250m;
        if (meters <= 300f) return r._300m;
        if (meters <= 350f) return r._350m;
        if (meters <= 400f) return r._400m;

        if (meters > 1000f && r._1000Greater != null) return r._1000Greater;
        if (meters > 500f && r._500Greater != null) return r._500Greater;
        if (r._400Greater != null) return r._400Greater;

        return r._400m ?? r._300m ?? r._200m ?? r._100m;
    }

    private void BumpInterval()
    {
        // reset the visible HUD countdown
        AudioCueSlider.instance?.ResetTimer();

        // restart the periodic loop so its internal timer resets too
        if (_periodicLoop != null)
        {
            StopCoroutine(_periodicLoop);
            _periodicLoop = StartCoroutine(PeriodicCueLoop());
        }
    }

}
