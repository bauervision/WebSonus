using System.Collections;
using Sonus.Core;
using UnityEngine;

/// AudioManager v2
/// - Driven by TargetManager.currentTarget (no ActiveTargetManager)
/// - Uses OLMGeoMapper for target world placement + player lat/lon sampling
/// - Periodic loop only (movement/drift counsel comes next)
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

    // Straight-ahead opportunistic moment
    [Header("Straight-Ahead opportunistic moment")]
    [SerializeField] private float straightAheadDeg = 24f;
    [SerializeField] private float straightAheadCooldown = 3f;

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

    public void StartSonic() // uses cueFrequencySeconds
    {
        StopSonic();
        _periodicLoop = StartCoroutine(PeriodicCueLoop());
    }

    public void StopSonic()
    {
        if (_periodicLoop != null) StopCoroutine(_periodicLoop);
        _periodicLoop = null;

        if (_playRoutine != null) StopCoroutine(_playRoutine);
        _playRoutine = null;
    }

    public void OnTargetChanged(TargetActor newTarget)
    {
        _everLockedThisTarget = false;
        _lastStraightAheadTime = Time.time - straightAheadCooldown; // allow SA immediately
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

        // Aim voice at target direction (world if possible)
        if (TryGetPlayerAndTargetWorld(actor, out Vector3 playerW, out Vector3 targetW))
            PositionAudioHeading(playerW, targetW);

        PlaySingle(clip);
        BumpInterval();
    }

    public void PlayInitialDirectionForTarget(TargetActor actor, bool alsoSpeakDistance = true)
    {
        if (actor == null) return;
        if (voiceSource == null) return;

        if (!TryGetPlayerGeo(out double pLat, out double pLon)) return;

        // Target world for direction
        bool haveWorld = TryGetPlayerAndTargetWorld(actor, out Vector3 playerW, out Vector3 targetW);

        float relDeg = 0f;
        if (haveWorld)
            relDeg = ComputeRelativeAngleDeg(playerW, targetW);
        else
            relDeg = ComputeRelativeAngleDegFromGeo(pLat, pLon, actor._Lat, actor._Lon);

        float abs = Mathf.Abs(relDeg);

        AudioClip dirClip;
        if (abs <= initialAheadDeg) dirClip = initialAhead;
        else if (abs >= initialBehindDeg) dirClip = initialBehind;
        else if (relDeg > 0f) dirClip = initialLeft;   // + = left
        else dirClip = initialRight;                   // - = right

        if (!alsoSpeakDistance || dirClip == null)
        {
            PlaySingle(dirClip);
            BumpInterval();
            return;
        }

        float meters = (float)TargetGeoUtil.ApproxMetersBetween(pLat, pLon, actor._Lat, actor._Lon);
        AudioClip distClip = PickDistanceClip(meters);

        if (haveWorld) PositionAudioHeading(playerW, targetW);
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
            Debug.Log("[Audio] Periodic tick");
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

        // Use geo for distance (matches your FOUND logic)
        if (!TryGetPlayerGeo(out double pLat, out double pLon))
        {
            // If we can’t sample geo yet, still try a world-only callout
            PlayForActiveTargetNow();
            return;
        }

        float meters = (float)TargetGeoUtil.ApproxMetersBetween(pLat, pLon, actor._Lat, actor._Lon);

        // Direction: world preferred
        float relDeg = 0f;
        if (TryGetPlayerAndTargetWorld(actor, out Vector3 playerW, out Vector3 targetW))
            relDeg = ComputeRelativeAngleDeg(playerW, targetW);
        else
            relDeg = ComputeRelativeAngleDegFromGeo(pLat, pLon, actor._Lat, actor._Lon);

        float absRel = Mathf.Abs(relDeg);
        float now = Time.time;

        // 1) Opportunistic straight-ahead moment
        if (sonus != null && sonus._straightAhead != null &&
            absRel <= straightAheadDeg &&
            (now - _lastStraightAheadTime) >= straightAheadCooldown)
        {
            if (TryGetPlayerAndTargetWorld(actor, out playerW, out targetW))
                PositionAudioHeading(playerW, targetW);

            PlaySingle(sonus._straightAhead);
            _lastStraightAheadTime = now;
            _everLockedThisTarget = true;
            BumpInterval();
            return;
        }

        // 2) If we’ve never “locked” this target yet, replay strong initial (dir + distance)
        if (!_everLockedThisTarget)
        {
            PlayInitialDirectionForTarget(actor, true);
            return;
        }

        // 3) Normal periodic direction + distance
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

        if (!TryGetPlayerGeo(out double pLat, out double pLon)) return;

        float meters = (float)TargetGeoUtil.ApproxMetersBetween(pLat, pLon, actor._Lat, actor._Lon);

        float relDeg = 0f;
        bool haveWorld = TryGetPlayerAndTargetWorld(actor, out Vector3 playerW, out Vector3 targetW);
        if (haveWorld) relDeg = ComputeRelativeAngleDeg(playerW, targetW);
        else relDeg = ComputeRelativeAngleDegFromGeo(pLat, pLon, actor._Lat, actor._Lon);

        if (haveWorld) PositionAudioHeading(playerW, targetW);

        AudioClip dirClip = PickDirectionClip(relDeg);
        AudioClip distClip = PickDistanceClip(meters);

        PlaySequence(dirClip, distClip);
        BumpInterval();
    }

    // -------------------------
    // Helpers
    // -------------------------

    private bool TryGetPlayerGeo(out double lat, out double lon)
    {
        lat = 0; lon = 0;

        // Prefer mapper’s “feet sample” (your recommended current position source)
        if (geoMapper != null && geoMapper.TryFeetScreenToLatLon(out lat, out lon))
            return true;

        // Fallback: SonusLocationState (if it’s valid)
        if (SonusLocationState.Lat != 0.0 && SonusLocationState.Lng != 0.0)
        {
            lat = SonusLocationState.Lat;
            lon = SonusLocationState.Lng;
            return true;
        }

        return false;
    }

    private bool TryGetPlayerAndTargetWorld(TargetActor actor, out Vector3 playerW, out Vector3 targetW)
    {
        playerW = default;
        targetW = default;

        var cam = sceneCamera ?? Camera.main;
        if (cam == null) return false;

        playerW = cam.transform.position;

        if (geoMapper == null) return false;

        targetW = geoMapper.LatLonToWorld(actor._Lat, actor._Lon, 0f);
        // Flatten to player Y so voice is on the horizontal plane
        targetW.y = playerW.y;

        return true;
    }

    private float ComputeRelativeAngleDeg(Vector3 playerPos, Vector3 targetPos)
    {
        var cam = sceneCamera ?? Camera.main;
        if (cam == null) return 0f;

        Vector3 fwd = cam.transform.forward; fwd.y = 0f;
        Vector3 toT = targetPos - playerPos; toT.y = 0f;

        if (toT.sqrMagnitude < 1e-6f || fwd.sqrMagnitude < 1e-6f) return 0f;

        // + = left, - = right
        return Vector3.SignedAngle(fwd, toT, Vector3.up);
    }

    // Fallback direction when world mapping isn’t ready:
    // compute geo bearing and compare to camera yaw.
    private float ComputeRelativeAngleDegFromGeo(double pLat, double pLon, double tLat, double tLon)
    {
        var cam = sceneCamera ?? Camera.main;
        if (cam == null) return 0f;

        Vector3 fwd = cam.transform.forward; fwd.y = 0f;
        float camYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;

        float bearing = (float)GeoBearingDeg(pLat, pLon, tLat, tLon); // 0=N, 90=E
        // rel: -180..180
        return Mathf.DeltaAngle(camYaw, bearing);
    }

    private static double GeoBearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double phi1 = lat1 * Mathf.Deg2Rad;
        double phi2 = lat2 * Mathf.Deg2Rad;
        double dLon = (lon2 - lon1) * Mathf.Deg2Rad;

        double y = System.Math.Sin(dLon) * System.Math.Cos(phi2);
        double x = System.Math.Cos(phi1) * System.Math.Sin(phi2) - System.Math.Sin(phi1) * System.Math.Cos(phi2) * System.Math.Cos(dLon);

        double brng = System.Math.Atan2(y, x) * Mathf.Rad2Deg;
        brng = (brng + 360.0) % 360.0;
        return brng;
    }

    private void PositionAudioHeading(Vector3 playerPos, Vector3 targetPos)
    {
        if (audioHeading == null) return;

        Vector3 dir = (targetPos - playerPos); dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;

        audioHeading.position = playerPos + dir.normalized * headingRadius;
        audioHeading.forward = dir.normalized;
    }

    private AudioClip PickRandom(AudioClip[] arr)
    {
        if (arr == null || arr.Length == 0) return null;
        return arr[Random.Range(0, arr.Length)];
    }

    private AudioClip PickDirectionClip(float relAngle)
    {
        if (sonus == null) return null;
        float abs = Mathf.Abs(relAngle);

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
        if (sonus == null || sonus._straightAhead == null) { Debug.LogWarning("[Audio] No straightAhead clip"); return; }
        Debug.Log("[Audio] TEST play straightAhead");
        PlaySingle(sonus._straightAhead);
    }



}
