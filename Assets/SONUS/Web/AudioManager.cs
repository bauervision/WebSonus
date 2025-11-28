using System.Collections;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Config")]
    public SONUS sonus;
    public GeoMapper geoMapper;
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
    [SerializeField] private float lockCorridorDeg = 24f;       // user “on course” cone


    [SerializeField] private float minMoveSpeed = 0.3f;         // m/s

    [SerializeField] private float movementCueCooldown = 4f;   // seconds
    [SerializeField] private float ignoreIfCloserThan = 10f;    // meters

    [SerializeField] private float straightAheadMovementGrace = 0.5f; // allow movement cue shortly after SA
    private float _lastStraightAheadPlayTime = -999f;


    [Header("Initial Direction Clips")]
    [Tooltip("Use strong orientation phrases for the very first callout")]
    public AudioClip initialAhead;     // “Target location straight ahead”
    public AudioClip initialLeft;      // “New target to your left”
    public AudioClip initialRight;     // “Target identified off to your right”
    public AudioClip initialBehind;    // “Target located behind you”

    [Header("Milestone Clips")]
    [Tooltip("Played when all anchors in a mission are found.")]
    public AudioClip missionComplete;


    [Tooltip("Degrees for choosing L/R/Ahead/Behind on initial callout")]
    [SerializeField] private float initialAheadDeg = 25f;     // <= this → ahead
    [SerializeField] private float initialBehindDeg = 140f;   // >= this → behind

    [Header("Directional Counsel")]
    [SerializeField] private bool counselEnabled = true;
    [SerializeField] private float counselCooldown = 2.5f;   // min seconds between any counsel
    [SerializeField] private float maintainCooldown = 6f;  // min seconds between “maintain heading”
    [SerializeField] private float maintainBandMax = 8f;   // ≤ this is “looking good”
    [SerializeField] private float driftBandMin = 5f;      // start of drift band
    [SerializeField] private float driftBandMax = 35f;     // up to this still “small drift” (beyond: handled by other cues)
    [SerializeField] private bool driftRequiresMaintain = false; // only drift after a maintain cue
    [SerializeField] private float driftAfterLockWindow = 7f;   // seconds after last corridor lock we allow drift counsel
    [SerializeField] private float backOnTrackCooldown = 4f;    // min gap between "back on track" calls
    [SerializeField] private float minLockForMaintain = 1.25f; // seconds aligned before we allow "maintain"
    [SerializeField] private float maintainAfterSA = 3.0f;     // seconds after StraightAhead before "maintain" allowed
    [SerializeField] private float maintainMinDistance = 20f;  // meters; 0 to disable

    [Header("New Target Grace")]
    [SerializeField] private float newTargetDriftGraceSeconds = 8f;
    private float _newTargetGraceUntil = -999f;


    private float _lastCounselTime = -999f;
    private float _lastMaintainTime = -999f;
    private bool _maintainPlayedThisLock = false; // set when we speak maintain while locked
    private bool _driftActive = false;            // we’re in a drift episode since last maintain
    private int _driftSide = 0;                  // -1 left, +1 right
    private float _lastBackOnTrackTime = -999f;


    private bool LoopsRunning => _periodicLoop != null || _movementLoop != null;

    public void PlayMissionComplete()
    {
        if (voiceSource == null || missionComplete == null) return;

        // Place the “voice” straight ahead of the player so it feels centered.
        var cam = sceneCamera ?? Camera.main;
        if (cam != null)
        {
            Vector3 player = cam.transform.position;
            Vector3 ahead = player + cam.transform.forward.normalized * Mathf.Max(0.5f, headingRadius);
            ahead.y = player.y;
            PositionAudioHeading(player, ahead);
        }

        // Single, clean line (stops any current VO, no sequencing)
        // NOTE: PlaySingle is private; we're inside AudioManager so this is fine.
        PlaySingle(missionComplete);

        // Optional: bump the HUD timer; harmless if loops are stopped.
        BumpInterval();
    }


    public void PlayInitialDirectionForActiveTarget(bool alsoSpeakDistance = true)
    {
        var active = ActiveTargetManager.Instance?.ActiveTarget;
        if (active == null || sceneCamera == null || voiceSource == null) return;

        Vector3 player = sceneCamera.transform.position;
        Vector3 target = geoMapper != null
            ? geoMapper.LatLonToWorld(active._Lat, active._Lon, player.y)
            : GeoUtils.GeoToWorld(new Vector2((float)active._Lat, (float)active._Lon));
        target.y = player.y;

        // Aim the “voice”
        PositionAudioHeading(player, target);

        float rel = ComputeRelativeAngleDeg(player, target); // -180..+180
        float abs = Mathf.Abs(rel);

        AudioClip dir;
        if (abs <= initialAheadDeg) dir = initialAhead;
        else if (abs >= initialBehindDeg) dir = initialBehind;
        else if (rel > 0f) dir = initialRight;  // target is to your left
        else dir = initialLeft; // target is to your right

        if (!alsoSpeakDistance || dir == null)
        {
            PlaySingle(dir);
            BumpInterval();
            return;
        }

        // Append distance (helps the player commit to a direction with urgency)
        var distClip = PickDistanceClip(Vector3.Distance(player, target));
        PlaySequence(dir, distClip);
        BumpInterval();

    }


    public void PlayArrival(TargetActor actor)
    {
        if (sonus == null || voiceSource == null) return;

        AudioClip clip = null;
        // Prefer type-specific, fall back to generic
        if (actor != null && sonus.arrival != null)
        {
            var t = (TargetType)actor._Type;
            if (t == TargetType.STATIONARY) clip = PickRandom(sonus.arrival.stationary);
            else if (t == TargetType.DYNAMIC) clip = PickRandom(sonus.arrival.dynamic);
            if (clip == null) clip = PickRandom(sonus.arrival.generic);
        }

        if (clip == null) return;

        // Position voice at target direction if we can
        var cam = sceneCamera ?? Camera.main;
        if (cam != null && actor != null)
        {
            Vector3 player = cam.transform.position;
            Vector3 target = geoMapper != null
                ? geoMapper.LatLonToWorld(actor._Lat, actor._Lon, player.y)
                : GeoUtils.GeoToWorld(new Vector2((float)actor._Lat, (float)actor._Lon));
            target.y = player.y;
            PositionAudioHeading(player, target);
        }

        PlaySingle(clip);   // uses your existing single-clip play (stops current, plays new)
        BumpInterval();     // optional: reset periodic timer so we don’t layer cues
    }

    private void PlayBackOnTrack(Vector3 player, Vector3 target)
    {
        // Prefer explicit backOnTrack bucket, else reuse maintain
        AudioClip clip = null;
        if (sonus?.counsel != null)
        {
            if (clip == null)
                clip = PickRandom(sonus.counsel.maintain);
        }
        if (clip == null) return;

        PositionAudioHeading(player, target);
        PlaySingle(clip);
        _lastCounselTime = Time.time;
        _lastMaintainTime = Time.time; // treat as a maintain reset
        _lastBackOnTrackTime = Time.time;
        BumpInterval();
    }


    private AudioClip PickRandom(AudioClip[] arr)
    {
        if (arr == null || arr.Length == 0) return null;
        int i = Random.Range(0, arr.Length);
        return arr[i];
    }

    private void PlayCounselMaintain(Vector3 player, Vector3 target)
    {
        if (sonus?.counsel == null) return;
        var clip = PickRandom(sonus.counsel.maintain);
        if (clip == null) return;
        PositionAudioHeading(player, target);
        PlaySingle(clip);
        _lastCounselTime = Time.time;
        _lastMaintainTime = Time.time;
        BumpInterval();
    }

    private void PlayCounselLeft(Vector3 player, Vector3 target)
    {
        if (sonus?.counsel == null) return;
        var clip = PickRandom(sonus.counsel.driftLeft);
        if (clip == null) return;
        PositionAudioHeading(player, target);
        PlaySingle(clip);
        _lastCounselTime = Time.time;
        BumpInterval();
    }

    private void PlayCounselRight(Vector3 player, Vector3 target)
    {
        if (sonus?.counsel == null) return;
        var clip = PickRandom(sonus.counsel.driftRight);
        if (clip == null) return;
        PositionAudioHeading(player, target);
        PlaySingle(clip);
        _lastCounselTime = Time.time;
        BumpInterval();
    }


    // --- Add with the other Movement-cue tuning fields ---
    // Anti-spam & hysteresis for straight-ahead
    [SerializeField] private float straightAheadDeg = 24f;        // instant fire threshold
    [SerializeField] private float straightAheadCooldown = 3f;    // min gap between straight-ahead calls
    [SerializeField] private float straightAheadRearmSeconds = 0.5f; // how long out of corridor before re-arming
    [SerializeField] private float straightAheadRearmExtraDeg = 4f;   // must exceed corridor by this extra angle to re-arm
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


    private void Update()
    {
        WatchdogEnsureLoops();
    }

    private void WatchdogEnsureLoops()
    {
        if (!movementCuesEnabled) return;
        if (sceneCamera == null) return;

        // If movement loop vanished (or never started), relaunch
        if (_movementLoop == null)
        {
            if (debugMovementCues) Debug.Log("[AudioManager] Watchdog: starting movement loop");
            _movementLoop = StartCoroutine(MovementCueLoop());
        }

        // If periodic loop vanished (or never started), relaunch with current frequency
        if (_periodicLoop == null)
        {
            if (debugMovementCues) Debug.Log("[AudioManager] Watchdog: starting periodic loop");
            _periodicLoop = StartCoroutine(PeriodicCueLoop());
        }
    }


    // --- PUBLIC control for UI ---
    public void StartSonic(float frequencySeconds)
    {
        ApplyFrequency(frequencySeconds);

        if (_periodicLoop != null)
            _periodicLoop = StartCoroutine(PeriodicCueLoop());

        if (_movementLoop != null)
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
            PlayPeriodicSmartCue();
        }
    }

    private void PlayPeriodicSmartCue()
    {
        var active = ActiveTargetManager.Instance?.ActiveTarget;
        if (active == null)
        {
            if (playNoTargetCue && sonus?._noTargets) PlaySingle(sonus._noTargets);
            return;
        }

        if (sceneCamera == null) { PlayForActiveTargetNow(); return; }

        Vector3 player = sceneCamera.transform.position;
        Vector3 target = geoMapper != null
            ? geoMapper.LatLonToWorld(active._Lat, active._Lon, player.y)
            : GeoUtils.GeoToWorld(new Vector2((float)active._Lat, (float)active._Lon));
        target.y = player.y;

        float distance = Vector3.Distance(player, target);
        float rel = ComputeRelativeAngleDeg(player, target);
        float absRel = Mathf.Abs(rel);
        float now = Time.time;

        // 1) If we are lined up and SA is armed & off cooldown → speak SA now (don’t miss the moment)
        bool saReady = _straightAheadArmed && (now - _lastStraightAheadTime) >= straightAheadCooldown;
        if (absRel <= straightAheadDeg && saReady && sonus?._straightAhead != null)
        {
            PositionAudioHeading(player, target);
            PlaySingle(sonus._straightAhead);
            _lastStraightAheadTime = now;
            _lastStraightAheadPlayTime = now;
            BumpInterval();
            return;
        }

        // 2) If we’ve never locked this target yet → replay strong initial (+ distance)
        if (!_everLockedThisTarget)
        {
            PlayInitialDirectionForActiveTarget(true);
            // OnNewTargetSelected() already ran at target-switch; do NOT call it here.
            return;
        }

        // 3) Otherwise, fall back to your normal interval: direction + distance
        PlayForActiveTargetNow();
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

            var active = ActiveTargetManager.Instance.ActiveTarget;
            if (active == null) { ResetMovementState(); continue; }

            // Positions
            Vector3 player = sceneCamera.transform.position;
            Vector3 target = geoMapper != null
                ? geoMapper.LatLonToWorld(active._Lat, active._Lon, player.y)
                : GeoUtils.GeoToWorld(new Vector2((float)active._Lat, (float)active._Lon));
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

            float camYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            float tarYaw = Mathf.Atan2(toT.x, toT.z) * Mathf.Rad2Deg;
            float relAngle = Mathf.DeltaAngle(camYaw, tarYaw); // -180..+180
            float absRel = Mathf.Abs(relAngle);
            float now = Time.time;

            bool saReady = _straightAheadArmed && (now - _lastStraightAheadTime) >= straightAheadCooldown;
            if (absRel <= straightAheadDeg && saReady && sonus?._straightAhead != null)
            {
                PositionAudioHeading(player, target);
                PlaySingle(sonus._straightAhead);
                _lastStraightAheadTime = now;
                _lastStraightAheadPlayTime = now;
                _straightAheadArmed = false;
                BumpInterval();
                if (debugMovementCues) Debug.Log($"[MC] SA (absRel={absRel:F1}°) fired pre-corridor.");
                continue; // skip rest of this tick after playing SA
            }


            bool inCorridor = absRel <= lockCorridorDeg;
            bool inMaintainBand = absRel <= maintainBandMax;

            if (inCorridor)
            {
                _everLockedThisTarget = true;
                _lockTimer += sampleInterval;
                _lastLockTime = now;
                _leftCorridorAt = -999f;

                // If we were drifting and re-aligned into the maintain band → "back on track"
                if (_driftActive && inMaintainBand && (now - _lastBackOnTrackTime) >= backOnTrackCooldown)
                {
                    PlayBackOnTrack(player, target);
                    _driftActive = false;
                    _driftSide = 0;
                    if (debugMovementCues) Debug.Log("[MC] Counsel: BACK ON TRACK");
                }

                // Instant "Straight Ahead" (anti-spam + re-arm)
                if (_straightAheadArmed && absRel <= straightAheadDeg && (now - _lastStraightAheadTime) >= straightAheadCooldown)
                {
                    if (sonus != null && sonus._straightAhead != null)
                    {
                        PositionAudioHeading(player, target);
                        PlaySingle(sonus._straightAhead);
                        _lastStraightAheadTime = now;
                        _lastStraightAheadPlayTime = now;   // for movement-cue grace
                        _straightAheadArmed = false;
                        BumpInterval();
                        if (debugMovementCues) Debug.Log($"[MC] StraightAhead PLAY (absRel={absRel:F1}°)");
                    }
                }
                else
                {
                    // --- Directional counsel: maintain / drift left-right ---
                    if (counselEnabled && (now - _lastCounselTime) >= counselCooldown)
                    {
                        // Maintain: only when stably aligned, spaced out, and not right after SA
                        if (absRel <= maintainBandMax
    && (now - _lastMaintainTime) >= maintainCooldown
    && _lockTimer >= minLockForMaintain
    && (now - _lastStraightAheadPlayTime) >= maintainAfterSA
    && (maintainMinDistance <= 0f || distance >= maintainMinDistance))
                        {
                            PlayCounselMaintain(player, target);
                            _maintainPlayedThisLock = true;   // unlocks drift cues later
                            if (debugMovementCues) Debug.Log("[MC] Counsel: MAINTAIN");
                        }

                        // Drift: only AFTER we've given a maintain while locked, and within drift band
                        else if (!inMaintainBand && absRel >= driftBandMin && absRel <= driftBandMax)
                        {
                            bool recentlyLocked = (now - _lastLockTime) <= driftAfterLockWindow;
                            bool cooldownOk = (now - _lastCounselTime) >= counselCooldown;
                            bool inGrace = now <= _newTargetGraceUntil;

                            bool allowed = cooldownOk &&
                                           (
                                               (recentlyLocked && (!driftRequiresMaintain || _maintainPlayedThisLock))  // existing rule
                                               || inGrace                                                                // new grace path
                                               || absRel >= (driftBandMin + 4f)                                          // your existing “clearly off” escape hatch
                                           );

                            if (allowed)
                            {
                                float sideY = Mathf.Sign(Vector3.Cross(fwd, toT).y); // +left / -right
                                if (sideY > 0f) { PlayCounselLeft(player, target); _driftActive = true; _driftSide = -1; }
                                else if (sideY < 0f) { PlayCounselRight(player, target); _driftActive = true; _driftSide = +1; }
                            }
                        }

                    }
                }
            }
            else
            {
                // Left corridor: reset per-lock state; drift can only happen after next maintain
                if (_wasLockedLastTick) _maintainPlayedThisLock = false;

                _lockTimer = 0f;

                // Track corridor exit for re-arm timing
                if (_leftCorridorAt < 0f) _leftCorridorAt = now;

                // Re-arm straight-ahead ONLY after time and angle thresholds
                bool rearmTime = (now - _leftCorridorAt) >= straightAheadRearmSeconds;
                bool rearmAngle = absRel >= (lockCorridorDeg + straightAheadRearmExtraDeg);
                if (rearmTime && rearmAngle) _straightAheadArmed = true;
            }

            // Detect corridor exit edge (for immediate movement cue)
            bool justExitedCorridor = _wasLockedLastTick && !inCorridor;

            if (justExitedCorridor)
            {
                bool cooldownOk = (now - _lastCounselTime) >= (counselCooldown * 0.75f);
                if (cooldownOk && absRel >= (lockCorridorDeg + 2f))
                {
                    float sideY = Mathf.Sign(Vector3.Cross(fwd, toT).y);
                    if (sideY > 0f) PlayCounselLeft(player, target);
                    else if (sideY < 0f) PlayCounselRight(player, target);
                    // mark drift episode
                    _driftActive = true; _driftSide = sideY > 0f ? -1 : +1;
                }
            }

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
            if (dt <= 0f) { if (debugMovementCues) Debug.Log("[MC] dt<=0"); continue; }

            Vector3 delta = target - _lastTargetWorld; delta.y = 0f;
            float disp = delta.magnitude;
            float speed = disp / dt;

            // Update cache AFTER measuring
            _lastTargetWorld = target;
            _lastSampleTime = now;

            // Speed-only gate
            if (speed < minMoveSpeed)
            {
                if (debugMovementCues) Debug.Log($"[MC] Too slow: {speed:F2} < {minMoveSpeed:F2}");
                continue;
            }

            // Movement cue on EXIT (if we were recently aligned)
            bool recentlyLockedForMove = (now - _lastLockTime) <= recentLockGrace;
            bool canPlayMovement = (now - _lastMoveCueTime) >= movementCueCooldown
                                   || (now - _lastStraightAheadPlayTime) >= straightAheadMovementGrace;

            if (justExitedCorridor && recentlyLockedForMove && canPlayMovement)
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

            if (debugMovementCues)
            {
                Debug.Log($"[MC] rel={absRel:F1}°, dist={distance:F1}m, lock={_lockTimer:F2}, " +
                          $"maintained={_maintainPlayedThisLock}, driftActive={_driftActive}, exited={justExitedCorridor}");
            }
        }
    }

    private bool _everLockedThisTarget = false;
    public void OnNewTargetSelected()
    {
        // Reset per-target movement/lock state
        ResetMovementState(); // this also sets _straightAheadArmed = true in your code

        // Make SA eligible immediately (don’t require rearm delay)
        _lastStraightAheadTime = Time.time - straightAheadCooldown;

        // Clear any stale recency that might suppress early cues
        _lastLockTime = -999f;
        _lastCounselTime = -999f;
        _lastMaintainTime = -999f;
        _lastStraightAheadPlayTime = -999f;
        _everLockedThisTarget = false;

        _newTargetGraceUntil = Time.time + newTargetDriftGraceSeconds;
    }


    private void ResetMovementState(bool clearLock = true)
    {
        if (clearLock) _lockTimer = 0f;

        _haveLastSample = false;
        _wasLockedLastTick = false;
        _straightAheadArmed = true;
        _leftCorridorAt = -999f;

        _maintainPlayedThisLock = false;
        _driftActive = false;
        _driftSide = 0;

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
        Vector3 targetPos = geoMapper != null
    ? geoMapper.LatLonToWorld(active._Lat, active._Lon, playerPos.y)
    : GeoUtils.GeoToWorld(new Vector2((float)active._Lat, (float)active._Lon));

        float distance = Vector3.Distance(playerPos, targetPos);
        float relativeAngle = ComputeRelativeAngleDeg(playerPos, targetPos);

        PositionAudioHeading(playerPos, targetPos);

        AudioClip dirClip = PickDirectionClip(relativeAngle);
        AudioClip distClip = PickDistanceClip(distance);

        PlaySequence(dirClip, distClip);
        //AudioCueSlider.instance.ResetTimer();
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
        Vector3 toT = targetPos - playerPos;
        toT.y = 0f;

        if (toT.sqrMagnitude < 1e-6f || fwd.sqrMagnitude < 1e-6f) return 0f;
        return Vector3.SignedAngle(fwd, toT, Vector3.up); // + = left, − = right
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

    public void UpdateGuidanceWorld(Vector3 playerPos, Vector3 targetPos, bool arrived)
    {
        // Keep the 3D “voice” anchored toward the target continuously.
        PositionAudioHeading(playerPos, targetPos);

        // Optional: fire an arrival cue if you have one
        // if (arrived && sonus != null && sonus._straightAhead != null)
        //     PlaySingle(sonus._straightAhead);
    }


}
