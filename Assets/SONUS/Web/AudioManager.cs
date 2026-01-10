// Assets/SONUS/Web/AudioManager.cs
using System.Collections;
using Sonus.Core;
using UnityEngine;

/// AudioManager v2 (stable + demo-safe)
/// - Distance MUST match the reticle -> use TargetManager.DistanceToTargetMeters() (geo truth)
/// - Direction + spatial placement MUST match reticle -> prefer TargetManager.TryGetTargetWorldPos()
/// - Initial direction MUST fire on new target even if geo isn't ready -> world-only OK
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Refs")]
    public SONUS sonus;
    public OLMGeoMapper geoMapper;
    public TargetManager targetManager;
    public Camera sceneCamera;
    public Transform audioHeading;
    public AudioSource voiceSource;

    [Header("3D voice placement")]
    public float headingRadius = 1.5f;

    [Header("Heading reference (IMPORTANT)")]
    [Tooltip("This transform defines what 'forward' means for left/right. If null, uses sceneCamera.")]
    public Transform headingSource;

    [Tooltip("Degrees to rotate headingSource forward before computing left/right. Set to 180 if your player starts at Y=180.")]
    public float headingOffsetDeg = 0f;

    [Tooltip("If your rig still reports left/right flipped after offset, toggle this.")]
    public bool invertLeftRight = false;

    [Header("Periodic cues")]
    [SerializeField] private float cueFrequencySeconds = 30f;
    [SerializeField] private bool playNoTargetCue = false;

    [Header("Initial Direction Clips")]
    public AudioClip initialAhead;
    public AudioClip initialLeft;
    public AudioClip initialRight;
    public AudioClip initialBehind;

    [SerializeField] private float initialAheadDeg = 25f;
    [SerializeField] private float initialBehindDeg = 140f;

    [Header("Milestone Clips")]
    public AudioClip missionComplete;

    [Header("Straight-Ahead opportunistic moment")]
    [SerializeField] private float straightAheadDeg = 24f;
    [SerializeField] private float straightAheadCooldown = 3f;

    [Header("Drift guidance")]
    [Tooltip("How often we evaluate drift (seconds). 0.25–0.5 is typical.")]
    [SerializeField] private float driftTickSeconds = 0.35f;

    [Tooltip("Distance (m) where we start widening the cone a lot (close-in).")]
    [SerializeField] private float driftNearDistanceM = 50f;

    [Tooltip("Distance (m) where we use our tightest cone (far).")]
    [SerializeField] private float driftFarDistanceM = 250f;

    [Tooltip("Allowed heading error (deg) when FAR away (tight).")]
    [SerializeField] private float driftConeFarDeg = 10f;

    [Tooltip("Allowed heading error (deg) when CLOSE (wide).")]
    [SerializeField] private float driftConeNearDeg = 35f;

    [Tooltip("Hysteresis: must come back within (allowed - this) to trigger Maintain.")]
    [SerializeField] private float driftReenterHysteresisDeg = 4f;

    [Tooltip("Cooldown between drift voice cues (seconds).")]
    [SerializeField] private float driftCueCooldown = 2.5f;

    [Tooltip("Cooldown between Maintain cues (seconds).")]
    [SerializeField] private float maintainCueCooldown = 2.5f;

    [Tooltip("If target is super close, stop drift chatter entirely (meters).")]
    [SerializeField] private float driftDisableUnderMeters = 12f;

    private Coroutine _driftLoop;
    private bool _isDrifting = false;
    private float _lastDriftCueTime = -999f;
    private float _lastMaintainCueTime = -999f;




    [Header("Debug")]
    public bool debugLogs = false;

    private Coroutine _periodicLoop;
    private Coroutine _playRoutine;

    private float _lastStraightAheadTime = -999f;
    private bool _everLockedThisTarget = false;

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

    // -------------------------
    // Public control
    // -------------------------

    public void ApplyFrequency(float seconds)
    {
        cueFrequencySeconds = Mathf.Max(1f, seconds);

        if (_periodicLoop != null)
        {
            StopCoroutine(_periodicLoop);
            _periodicLoop = StartCoroutine(PeriodicCueLoop());
        }
    }

    public void StartSonic()
    {
        StopSonic();
        _periodicLoop = StartCoroutine(PeriodicCueLoop());
        _driftLoop = StartCoroutine(DriftCueLoop());
    }

    public void StopSonic()
    {
        if (_periodicLoop != null) StopCoroutine(_periodicLoop);
        _periodicLoop = null;

        if (_driftLoop != null) StopCoroutine(_driftLoop);
        _driftLoop = null;

        if (_playRoutine != null) StopCoroutine(_playRoutine);
        _playRoutine = null;
    }

    public void OnTargetChanged(TargetActor newTarget)
    {
        _everLockedThisTarget = false;
        _lastStraightAheadTime = Time.time - straightAheadCooldown;

        // drift state reset
        _isDrifting = false;
        _lastDriftCueTime = Time.time - driftCueCooldown;
        _lastMaintainCueTime = Time.time - maintainCueCooldown;

        if (debugLogs)
            Debug.Log($"[Audio] OnTargetChanged -> {(newTarget != null ? $"{newTarget._Lat:F6},{newTarget._Lon:F6}" : "null")}");
    }

    private IEnumerator DriftCueLoop()
    {
        // Give initial callout a moment so we don't immediately fight it.
        yield return new WaitForSecondsRealtime(0.25f);

        while (true)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, driftTickSeconds));
            EvaluateDriftTick();
        }
    }

    private void EvaluateDriftTick()
    {
        var actor = targetManager != null ? targetManager.currentTarget : null;
        if (actor == null) return;

        // Distance must match TargetManager truth (GEO meters).
        if (!TryGetDistanceMeters(actor, out float meters)) return;
        if (!float.IsFinite(meters)) return;

        // If very close, don't nag.
        if (meters <= driftDisableUnderMeters)
        {
            _isDrifting = false;
            return;
        }

        float allowed = GetAllowedConeDeg(meters);

        // Direction: world preferred (matches AR reticle), geo fallback.
        bool haveWorld = TryGetPlayerAndTargetWorld(actor, out Vector3 playerW, out Vector3 targetW);

        float relDeg;
        if (haveWorld)
        {
            relDeg = ComputeRelativeAngleDeg(playerW, targetW);
        }
        else
        {
            if (!TryGetPlayerGeo(out double pLat, out double pLon)) return;
            relDeg = ComputeRelativeAngleDegFromGeo(pLat, pLon, actor._Lat, actor._Lon);
        }

        float abs = Mathf.Abs(relDeg);
        float now = Time.time;

        // OUTSIDE cone -> drift cue
        if (abs > allowed)
        {
            if (now - _lastDriftCueTime < driftCueCooldown) return;

            // Don’t stack over existing VO; drift will retry next tick.
            if (voiceSource != null && voiceSource.isPlaying) return;

            // Aim spatial VO at target direction if possible
            if (haveWorld) PositionAudioHeading(playerW, targetW);

            // NOTE: your relDeg is +RIGHT / -LEFT
            AudioClip driftClip = PickDriftClip(relDeg);
            if (driftClip == null) return;

            PlaySingle(driftClip);

            _isDrifting = true;
            _lastDriftCueTime = now;
            return;
        }

        // INSIDE cone -> maintain (only if we were drifting)
        if (_isDrifting)
        {
            float reenter = Mathf.Max(0f, allowed - driftReenterHysteresisDeg);

            if (abs <= reenter)
            {
                if (now - _lastMaintainCueTime < maintainCueCooldown) return;
                if (voiceSource != null && voiceSource.isPlaying) return;

                AudioClip m = PickMaintainClip();
                if (m != null) PlayConfirmWithDistance(m, actor);
                _isDrifting = false;
                _lastMaintainCueTime = now;

                // Once they’ve demonstrated alignment, this target is "locked"
                _everLockedThisTarget = true;
            }
        }
    }

    private float GetAllowedConeDeg(float meters)
    {
        // Far -> tight. Near -> wide.
        // We interpolate between farDistanceM..nearDistanceM
        float t;

        if (meters >= driftFarDistanceM) t = 0f;          // far -> use driftConeFarDeg
        else if (meters <= driftNearDistanceM) t = 1f;    // near -> use driftConeNearDeg
        else
        {
            float span = Mathf.Max(1f, driftFarDistanceM - driftNearDistanceM);
            t = (driftFarDistanceM - meters) / span;      // 0..1 as we approach
        }

        return Mathf.Lerp(driftConeFarDeg, driftConeNearDeg, t);
    }
    private AudioClip PickDriftClip(float relDeg)
    {
        if (sonus == null || sonus.counsel == null) return null;

        // relDeg: + => target is RIGHT => you are drifting LEFT
        // relDeg: - => target is LEFT  => you are drifting RIGHT
        if (relDeg > 0f) return PickRandom(sonus.counsel.driftLeft);
        return PickRandom(sonus.counsel.driftRight);
    }


    private AudioClip PickMaintainClip()
    {
        if (sonus == null || sonus.counsel == null) return null;
        return PickRandom(sonus.counsel.maintain);
    }



    private void PlayConfirmWithDistance(AudioClip confirmClip, TargetActor actor)
    {
        if (confirmClip == null) return;

        if (actor != null && TryGetDistanceMeters(actor, out float meters))
        {
            AudioClip dist = PickDistanceClip(meters);
            PlaySequence(confirmClip, dist);
        }
        else
        {
            PlaySingle(confirmClip);
        }

        BumpInterval();
    }


    private bool TryGetDistanceMeters(TargetActor actor, out float meters)
    {
        meters = float.NaN;
        if (actor == null) return false;

        if (targetManager != null)
        {
            float d = targetManager.DistanceToTargetMeters();
            if (float.IsFinite(d) && d < float.PositiveInfinity)
            {
                meters = d;
                return true;
            }
        }

        // Fallback (if you ever call audio before TargetManager is ready)
        if (TryGetPlayerGeo(out double pLat, out double pLon))
        {
            meters = (float)TargetGeoUtil.ApproxMetersBetween(pLat, pLon, actor._Lat, actor._Lon);
            return float.IsFinite(meters);
        }

        return false;
    }





    public void HearNow() => PlayForActiveTargetNow();

    // -------------------------
    // One-shots
    // -------------------------

    public void PlayMissionComplete()
    {
        if (voiceSource == null || missionComplete == null) return;

        var cam = sceneCamera ?? Camera.main;
        if (cam != null)
        {
            Vector3 player = cam.transform.position;
            Vector3 ahead = player + cam.transform.forward.normalized * Mathf.Max(0.5f, headingRadius);
            ahead.y = player.y;
            PositionAudioHeading(player, ahead);
        }

        PlaySingle(missionComplete);
        BumpInterval();
    }

    public void PlayArrival(TargetActor actor)
    {
        if (sonus == null || voiceSource == null) return;
        if (actor == null) return;

        AudioClip clip = null;

        if (sonus.arrival != null)
        {
            if (actor._Type == (int)TargetType.STATIONARY) clip = PickRandom(sonus.arrival.stationary);
            else if (actor._Type == (int)TargetType.DYNAMIC) clip = PickRandom(sonus.arrival.dynamic);

            if (clip == null) clip = PickRandom(sonus.arrival.generic);
        }

        if (clip == null) return;

        if (TryGetPlayerAndTargetWorld(actor, out Vector3 playerW, out Vector3 targetW))
            PositionAudioHeading(playerW, targetW);

        PlaySingle(clip);
        BumpInterval();
    }


    private Transform GetHeadingTransform()
    {
        if (headingSource != null) return headingSource;
        if (sceneCamera != null) return sceneCamera.transform;
        if (Camera.main != null) return Camera.main.transform;
        return null;
    }

    private bool TryGetHeadingBasis(out Vector3 origin, out Vector3 headingFwd)
    {
        origin = default;
        headingFwd = default;

        Transform h = GetHeadingTransform();
        if (h == null) return false;

        origin = h.position;

        headingFwd = h.forward;
        headingFwd.y = 0f;

        if (headingFwd.sqrMagnitude < 1e-6f) return false;
        headingFwd.Normalize();

        if (Mathf.Abs(headingOffsetDeg) > 0.001f)
            headingFwd = Quaternion.AngleAxis(headingOffsetDeg, Vector3.up) * headingFwd;

        return true;
    }


    /// Strong initial callout: direction ALWAYS if possible, distance only if geo truth is ready.
    public void PlayInitialDirectionForTarget(TargetActor actor, bool alsoSpeakDistance = true)
    {
        if (actor == null) return;
        if (voiceSource == null) return;

        // --- Direction (prefer world, matching reticle) ---
        bool haveWorld = TryGetPlayerAndTargetWorld(actor, out Vector3 playerW, out Vector3 targetW);

        float relDeg;
        if (haveWorld)
        {
            relDeg = ComputeRelativeAngleDeg(playerW, targetW);

            // Spatial placement should match direction
            PositionAudioHeading(playerW, targetW);
        }
        else
        {
            // Fallback direction from geo bearing (only if geo is available)
            if (!TryGetPlayerGeo(out double pLat0, out double pLon0))
            {
                if (debugLogs) Debug.LogWarning("[Audio] InitialDir: no world + no geo -> cannot speak.");
                return;
            }

            relDeg = ComputeRelativeAngleDegFromGeo(pLat0, pLon0, actor._Lat, actor._Lon);
        }

        AudioClip dirClip = PickInitialDirClip(relDeg);
        if (dirClip == null) return;

        // Direction-only is ALWAYS allowed (even if distance not ready)
        if (!alsoSpeakDistance)
        {
            if (debugLogs) Debug.Log($"[Audio] InitialDir (dir-only) rel={relDeg:F1}");
            PlaySingle(dirClip);
            BumpInterval();
            return;
        }

        // --- Distance (must match reticle truth) ---
        if (!TryGetDistanceMeters(actor, out float meters))
        {
            // Geo truth not ready -> still speak direction now
            if (debugLogs) Debug.Log($"[Audio] InitialDir (no distance yet) rel={relDeg:F1}");
            PlaySingle(dirClip);
            BumpInterval();
            return;
        }

        AudioClip distClip = PickDistanceClip(meters);

        if (debugLogs) Debug.Log($"[Audio] InitialDir rel={relDeg:F1} meters={meters:F0}");
        PlaySequence(dirClip, distClip);
        BumpInterval();
    }

    // -------------------------
    // Periodic loop
    // -------------------------

    private IEnumerator PeriodicCueLoop()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(cueFrequencySeconds);
            PlayPeriodicSmartCue();
        }
    }

    private void PlayPeriodicSmartCue()
    {
        var actor = targetManager != null ? targetManager.currentTarget : null;

        if (actor == null)
        {
            if (playNoTargetCue && sonus != null && sonus._noTargets != null)
                PlaySingle(sonus._noTargets);
            return;
        }

        // Distance must match reticle (geo truth)
        bool haveMeters = TryGetDistanceMeters(actor, out float meters);

        // Direction: prefer world
        float relDeg;
        bool haveWorld = TryGetPlayerAndTargetWorld(actor, out Vector3 playerW, out Vector3 targetW);

        if (haveWorld)
        {
            relDeg = ComputeRelativeAngleDeg(playerW, targetW);
        }
        else
        {
            // fallback direction from geo (only if available)
            if (!TryGetPlayerGeo(out double pLat, out double pLon))
            {
                // If we haven't locked yet, at least try to speak initial direction w/out distance
                if (!_everLockedThisTarget)
                    PlayInitialDirectionForTarget(actor, false);

                return;
            }
            relDeg = ComputeRelativeAngleDegFromGeo(pLat, pLon, actor._Lat, actor._Lon);
        }

        float absRel = Mathf.Abs(relDeg);
        float now = Time.time;

        // 1) Opportunistic straight-ahead moment (world placement if available)
        if (sonus != null && sonus._straightAhead != null &&
     absRel <= straightAheadDeg &&
     (now - _lastStraightAheadTime) >= straightAheadCooldown)
        {
            if (haveWorld) PositionAudioHeading(playerW, targetW);

            PlayConfirmWithDistance(sonus._straightAhead, actor);
            _lastStraightAheadTime = now;
            _everLockedThisTarget = true;
            return;
        }


        // 2) Never locked yet -> replay strong initial (dir + distance if possible)
        if (!_everLockedThisTarget)
        {
            PlayInitialDirectionForTarget(actor, haveMeters);
            // If distance wasn't available, the call will still speak direction-only.
            return;
        }

        // 3) Normal periodic: direction + distance if possible; otherwise direction only
        PlayForActiveTargetNow();
    }

    public void PlayForActiveTargetNow()
    {
        var actor = targetManager != null ? targetManager.currentTarget : null;
        if (actor == null)
        {
            if (playNoTargetCue && sonus != null && sonus._noTargets != null)
                PlaySingle(sonus._noTargets);
            return;
        }

        bool haveMeters = TryGetDistanceMeters(actor, out float meters);

        bool haveWorld = TryGetPlayerAndTargetWorld(actor, out Vector3 playerW, out Vector3 targetW);

        float relDeg;
        if (haveWorld)
        {
            relDeg = ComputeRelativeAngleDeg(playerW, targetW);
            PositionAudioHeading(playerW, targetW);
        }
        else
        {
            if (!TryGetPlayerGeo(out double pLat, out double pLon))
                return;

            relDeg = ComputeRelativeAngleDegFromGeo(pLat, pLon, actor._Lat, actor._Lon);
        }

        AudioClip dirClip = PickDirectionClip(relDeg);

        if (!haveMeters)
        {
            // Direction-only
            if (debugLogs) Debug.Log($"[Audio] Now dir-only rel={relDeg:F1}");
            PlaySingle(dirClip);
            BumpInterval();
            return;
        }

        AudioClip distClip = PickDistanceClip(meters);

        if (debugLogs) Debug.Log($"[Audio] Now rel={relDeg:F1} meters={meters:F0}");
        PlaySequence(dirClip, distClip);
        BumpInterval();
    }

    // -------------------------
    // Distance + world truth helpers
    // -------------------------

    // ✅ Must match reticle truth. Prefer TargetManager.DistanceToTargetMeters().

    // ✅ Geo truth for distance must match reticle: SonusPlayerGeoState (the 3D driver).
    private bool TryGetPlayerGeo(out double lat, out double lon)
    {
        lat = 0; lon = 0;

        if (SonusPlayerGeoState.HasValue)
        {
            lat = SonusPlayerGeoState.Lat;
            lon = SonusPlayerGeoState.Lng;

            if (lat != 0.0 && lon != 0.0) return true;
        }

        return false;
    }

    // ✅ World truth should match reticle: prefer TargetManager.TryGetTargetWorldPos.
    private bool TryGetPlayerAndTargetWorld(TargetActor actor, out Vector3 playerW, out Vector3 targetW)
    {
        playerW = default;
        targetW = default;

        // Player origin must match the heading basis (same as viz)
        if (!TryGetHeadingBasis(out playerW, out _)) return false;

        // 1) Best: EXACT same world position the AR reticle + viz use
        if (targetManager != null && targetManager.TryGetTargetWorldPos(out Vector3 tpos))
        {
            targetW = tpos;
            targetW.y = playerW.y; // flatten
            return true;
        }

        // 2) Fallback: world from geoMapper (less ideal)
        if (geoMapper != null && actor != null)
        {
            targetW = geoMapper.LatLonToWorld(actor._Lat, actor._Lon, 0f);
            targetW.y = playerW.y;
            return true;
        }

        return false;
    }


    // -------------------------
    // Direction math
    // -------------------------

    // Convention:
    // +relDeg => target is to the RIGHT of heading
    // -relDeg => target is to the LEFT of heading
    private float ComputeRelativeAngleDeg(Vector3 playerPos, Vector3 targetPos)
    {
        if (!TryGetHeadingBasis(out _, out Vector3 fwd)) return 0f;

        Vector3 toT = targetPos - playerPos;
        toT.y = 0f;

        if (toT.sqrMagnitude < 1e-6f) return 0f;
        toT.Normalize();

        // EXACTLY like the viz: SignedAngle(heading, toTarget, up)
        float rel = Vector3.SignedAngle(fwd, toT, Vector3.up);

        if (invertLeftRight) rel = -rel;
        return rel;
    }


    private float ComputeRelativeAngleDegFromGeo(double pLat, double pLon, double tLat, double tLon)
    {
        if (!TryGetHeadingBasis(out _, out Vector3 fwd)) return 0f;

        // Heading in compass terms: 0=N, 90=E
        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        if (yaw < 0f) yaw += 360f;

        float bearing = (float)GeoBearingDeg(pLat, pLon, tLat, tLon); // 0=N, 90=E

        // Positive when bearing is to the RIGHT (clockwise) from yaw
        float rel = Mathf.DeltaAngle(yaw, bearing);

        if (invertLeftRight) rel = -rel;
        return rel;
    }


    private static double GeoBearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double phi1 = lat1 * Mathf.Deg2Rad;
        double phi2 = lat2 * Mathf.Deg2Rad;
        double dLon = (lon2 - lon1) * Mathf.Deg2Rad;

        double y = System.Math.Sin(dLon) * System.Math.Cos(phi2);
        double x =
            System.Math.Cos(phi1) * System.Math.Sin(phi2) -
            System.Math.Sin(phi1) * System.Math.Cos(phi2) * System.Math.Cos(dLon);

        double brng = System.Math.Atan2(y, x) * Mathf.Rad2Deg;
        brng = (brng + 360.0) % 360.0;
        return brng;
    }

    // -------------------------
    // Clip selection
    // -------------------------

    private AudioClip PickInitialDirClip(float relDeg)
    {
        float abs = Mathf.Abs(relDeg);

        if (abs <= initialAheadDeg) return initialAhead;
        if (abs >= initialBehindDeg) return initialBehind;

        // + = RIGHT, - = LEFT
        if (relDeg > 0f) return initialRight;
        return initialLeft;
    }

    private AudioClip PickDirectionClip(float relAngle)
    {
        if (sonus == null) return null;

        float abs = Mathf.Abs(relAngle);

        // NOTE: This is your existing minimal system.
        // You can expand to left/right later, but for now keep what you had.
        if (abs <= 20f) return sonus._straightAhead;
        if (abs >= 170f) return sonus._directlyBehind;
        if (abs >= 110f) return sonus._behindYou;

        return null;
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

    // -------------------------
    // Spatial placement + playback
    // -------------------------

    private void PositionAudioHeading(Vector3 playerPos, Vector3 targetPos)
    {
        if (audioHeading == null) return;

        Vector3 dir = (targetPos - playerPos);
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;

        audioHeading.position = playerPos + dir.normalized * headingRadius;
        audioHeading.forward = dir.normalized;
    }

    private AudioClip PickRandom(AudioClip[] arr)
    {
        if (arr == null || arr.Length == 0) return null;
        return arr[Random.Range(0, arr.Length)];
    }

    private void PlaySingle(AudioClip clip)
    {
        if (clip == null || voiceSource == null) return;

        if (_playRoutine != null) StopCoroutine(_playRoutine);
        _playRoutine = null;

        voiceSource.Stop();
        voiceSource.clip = clip;
        voiceSource.Play();
    }

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
            yield return new WaitForSeconds(c.length);
        }

        _playRoutine = null;
    }

    private void BumpInterval()
    {
        if (_periodicLoop != null)
        {
            StopCoroutine(_periodicLoop);
            _periodicLoop = StartCoroutine(PeriodicCueLoop());
        }
    }

    [ContextMenu("TEST: Play StraightAhead")]
    public void DebugPlayStraightAhead()
    {
        if (sonus == null || sonus._straightAhead == null)
        {
            Debug.LogWarning("[Audio] No straightAhead clip");
            return;
        }

        Debug.Log("[Audio] TEST play straightAhead");
        PlaySingle(sonus._straightAhead);
    }
}
