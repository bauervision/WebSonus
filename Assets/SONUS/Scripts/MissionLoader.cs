// MissionLoader.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MissionLoader : MonoBehaviour
{
    public static MissionLoader Instance { get; private set; }
    public string activeMission;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void SetActiveMission(string name) => activeMission = name;

    public void ActivateMission()
    {
        // IMPORTANT: include inactive so we can enable placed anchors
        var anchors = FindObjectsByType<MissionAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // Disable all
        foreach (var a in anchors)
            if (a.targetObject) a.targetObject.SetActive(false);

        bool setActiveOnce = false;

        // Enable selected + wire behavior
        foreach (var a in anchors)
        {
            if (a.missionId != activeMission) continue;
            var go = a.targetObject ? a.targetObject : a.gameObject;

            go.SetActive(true);
            if (!string.IsNullOrEmpty(a.targetName)) go.name = a.targetName;

            // Prefer TargetProxy → actor
            // Sync world → actor lat/lon so AudioManager distances are correct
            var proxy = go.GetComponent<TargetProxy>();
            var mapper = PlayerLocator.instance?.mapper ?? FindFirstObjectByType<GeoMapper>();
            if (proxy != null && proxy.actor != null && mapper != null)
            {
                var (lat, lon) = mapper.WorldToLatLon(go.transform.position);
                proxy.actor._Lat = lat;
                proxy.actor._Lon = lon;

                // Register every actor, but only set active/play stinger once
                ActiveTargetManager.Instance.Register(proxy.actor);
                if (!setActiveOnce)
                {
                    ActiveTargetManager.Instance.SetActiveTarget(proxy.actor);
                    AudioManager.Instance?.PlayNewTargetClip(proxy.actor);
                    setActiveOnce = true;
                }
            }

            // Start route if dynamic
            if (a.targetType == MissionTargetType.Dynamic && a.routePoints != null && a.routePoints.Count >= 2)
            {
                var mover = go.GetComponent<SimpleRouteMover>() ?? go.AddComponent<SimpleRouteMover>();
                mover.Configure(a.routePoints, a.moveSpeed, a.dwellSeconds, a.loop);
                mover.Begin();
            }
        }

        // If you use a finalizer:
        var thm = FindFirstObjectByType<TargetHUDManager>();
        if (thm) thm.SendMessage("FinalizeMissionUI", SendMessageOptions.DontRequireReceiver);
    }

    public void EndMission()
    {
        // Stop movers (they survive SetActive otherwise)
        var movers = FindObjectsByType<SimpleRouteMover>(FindObjectsSortMode.None);
        foreach (var m in movers) m.StopMoving();
        // Optional: disable anchors for activeMission if desired.
    }

    // -------- New overloads so you can call with no params --------

    public void SelectAnotherTarget() { SelectAnotherTargetInMission(); }

    /// <summary>
    /// Pick another target within the current active mission, excluding the currently active anchor if we can find it.
    /// </summary>
    public bool SelectAnotherTargetInMission()
    {
        if (string.IsNullOrEmpty(activeMission))
        {
            Debug.LogWarning("[MissionLoader] No activeMission set.");
            return false;
        }

        // Try to infer the currently-active anchor to exclude
        var exclude = FindAnchorForActiveActor();
        return SelectAnotherTargetInMission(activeMission, exclude, true);
    }

    /// <summary>
    /// Same as above, but lets you pass randomize.
    /// </summary>
    public bool SelectAnotherTargetInMission(bool randomize)
    {
        if (string.IsNullOrEmpty(activeMission))
        {
            Debug.LogWarning("[MissionLoader] No activeMission set.");
            return false;
        }

        var exclude = FindAnchorForActiveActor();
        return SelectAnotherTargetInMission(activeMission, exclude, randomize);
    }

    /// <summary>
    /// Helper to infer which MissionAnchor corresponds to the currently active target.
    /// </summary>
    private MissionAnchor FindAnchorForActiveActor()
    {
        var active = ActiveTargetManager.Instance?.ActiveTarget;
        if (active == null) return null;

        var anchors = FindObjectsByType<MissionAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var a in anchors)
        {
            var go = a.targetObject ? a.targetObject : a.gameObject;
            var proxy = go.GetComponent<TargetProxy>();
            if (proxy != null && proxy.actor == active) return a;
        }
        return null;
    }

    /// <summary>
    /// Original selector (now used by the overloads).
    /// Chooses another enabled anchor in the same mission (excluding one if provided), sets it active, and plays the new target stinger.
    /// </summary>
    public bool SelectAnotherTargetInMission(string missionId, MissionAnchor exclude, bool randomize = true)
    {
        // Gather candidates (active objects only)
        var anchors = FindObjectsByType<MissionAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var candidates = new List<MissionAnchor>();

        foreach (var a in anchors)
        {
            if (a == null) continue;
            if (a.missionId != missionId) continue;
            if (a == exclude) continue;

            var go = a.targetObject ? a.targetObject : a.gameObject;
            if (!go.activeInHierarchy) continue;

            var proxy = go.GetComponent<TargetProxy>();
            if (proxy == null || proxy.actor == null) continue;

            candidates.Add(a);
        }

        if (candidates.Count == 0)
        {
            Debug.Log("[MissionLoader] No remaining targets in this mission.");
            return false;
        }

        var next = randomize ? candidates[Random.Range(0, candidates.Count)] : candidates[0];
        var nextGO = next.targetObject ? next.targetObject : next.gameObject;
        var nextProxy = nextGO.GetComponent<TargetProxy>();
        if (nextProxy == null || nextProxy.actor == null) return false;

        // Ensure enabled (belt-and-suspenders)
        if (!nextGO.activeSelf) nextGO.SetActive(true);

        // Make it the active target
        ActiveTargetManager.Instance.Register(nextProxy.actor);
        ActiveTargetManager.Instance.SetActiveTarget(nextProxy.actor);

        // Optional: play your "new target" stinger
        AudioManager.Instance?.PlayNewTargetClip(nextProxy.actor);

        return true;
    }
}

// (unchanged)
public class SimpleRouteMover : MonoBehaviour
{
    List<Transform> _points; float _speed; float _dwell; bool _loop; Coroutine _co;

    public void Configure(List<Transform> pts, float speed, float dwell, bool loop)
    { _points = pts; _speed = Mathf.Max(0.01f, speed); _dwell = Mathf.Max(0f, dwell); _loop = loop; }

    public void Begin() { if (_co != null) StopCoroutine(_co); if (_points == null || _points.Count < 2) return; _co = StartCoroutine(Run()); }
    public void StopMoving() { if (_co != null) StopCoroutine(_co); _co = null; }

    IEnumerator Run()
    {
        int i = 0;
        while (true)
        {
            var a = _points[i].position;
            var b = _points[(i + 1) % _points.Count].position;

            while ((transform.position - b).sqrMagnitude > 0.05f)
            {
                transform.position = Vector3.MoveTowards(transform.position, b, _speed * Time.deltaTime);
                yield return null;
            }

            if (_dwell > 0f) yield return new WaitForSeconds(_dwell);
            i++;
            if (i >= _points.Count - 1) { if (_loop) i = 0; else yield break; }
        }
    }
}
