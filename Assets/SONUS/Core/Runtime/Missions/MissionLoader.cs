using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MissionLoader : MonoBehaviour
{
    public static MissionLoader Instance { get; private set; }

    [Header("Configure in Inspector")]
    public List<Mission> missions = new();

    [Header("Visibility")]
    [Tooltip("If true, only the current anchor is enabled; switching targets hides the previous one.")]
    public bool hideInactiveAnchors = true;

    [Header("Runtime (read-only)")]
    [SerializeField] private int activeMissionIndex = -1;
    [SerializeField] private MissionAnchor _activeAnchor;

    private readonly HashSet<MissionAnchor> _completedThisMission = new();

    public Mission ActiveMission =>
        (activeMissionIndex >= 0 && activeMissionIndex < missions.Count) ? missions[activeMissionIndex] : null;

    public string ActiveMissionName => ActiveMission?.name ?? string.Empty;

    // ─────────────────────────────────────────────────────────────────────────────
    // Core events (gameplay layer subscribes to drive Audio/UI)
    // ─────────────────────────────────────────────────────────────────────────────
    public event Action<string> MissionStaged; // missionName
    public event Action<string, MissionAnchor, TargetActor> MissionActivated; // missionName, firstAnchor, firstActor
    public event Action<MissionAnchor, TargetActor, bool> ActiveTargetChanged; // anchor, actor, isInitialSelect
    public event Action<MissionAnchor, TargetActor, bool> AnchorArrived; // anchor, actor, isMissionCompleteNow
    public event Action<string> MissionCompleted; // missionName

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SyncAnchorMissionFields();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) SyncAnchorMissionFields();
    }
#endif

    // ─────────────────────────────────────────────────────────────────────────────
    // Stage / Activate
    // ─────────────────────────────────────────────────────────────────────────────
    public void SelectMissionByName(string missionName)
    {
        int idx = missions.FindIndex(m => string.Equals(m.name, missionName, StringComparison.Ordinal));
        if (idx < 0) { Debug.LogWarning($"[MissionLoader] Mission not found: {missionName}"); return; }
        SelectMissionByIndex(idx);
    }

    public void SelectMissionByIndex(int index)
    {
        if (index < 0 || index >= missions.Count) { Debug.LogWarning("[MissionLoader] Bad mission index"); return; }

        EndMission(); // cleanup prior run
        activeMissionIndex = index;
        _completedThisMission.Clear();
        _activeAnchor = null;

        DeactivateAllAnchorsAcrossAllMissions();
        MissionStaged?.Invoke(ActiveMissionName);
    }

    public void ActivateMission() => StartCoroutine(ActivateMissionCo());

    public void LoadMissionByName(string missionName) => SelectMissionByName(missionName);
    public void LoadMissionByIndex(int index) => SelectMissionByIndex(index);

    private IEnumerator ActivateMissionCo()
    {
        var m = ActiveMission;
        if (m == null)
        {
            Debug.LogWarning("[MissionLoader] ActivateMissionCo: no active mission staged.");
            yield break;
        }

        // Stop any movers left around
        {
            // var movers = FindObjectsByType<SimpleRouteMover>(FindObjectsSortMode.None);
            // foreach (var mv in movers) mv.StopMoving();
        }
        yield return null;

        _completedThisMission.Clear();
        _activeAnchor = null;

        yield return DeactivateAllAnchorsAcrossAllMissionsAsync(batchSize: 32);

        var available = new List<MissionAnchor>();
        foreach (var a in m.anchors) if (a) available.Add(a);

        if (available.Count == 0)
        {
            Debug.LogWarning($"[MissionLoader] ActivateMissionCo: mission '{m.name}' has no anchors.");
            yield break;
        }

#if UNITY_WEBGL
        if (!hideInactiveAnchors && available.Count > 8) hideInactiveAnchors = true;
#endif

        if (!hideInactiveAnchors)
        {
            int i = 0;
            foreach (var a in available)
            {
                EnableAnchor(a);
                if ((++i % 8) == 0) yield return null;
            }
        }

        MissionAnchor first = m.randomizeFirst
            ? available[UnityEngine.Random.Range(0, available.Count)]
            : available[0];

        if (!SetActiveAnchor(first, isInitialSelect: true))
        {
            Debug.LogWarning("[MissionLoader] ActivateMissionCo: failed to set first anchor active.");
            yield break;
        }

        // Notify gameplay layer that mission is live + initial target is selected.
        var firstGo = first.targetObject ? first.targetObject : first.gameObject;
        var firstActor = firstGo ? firstGo.GetComponent<TargetProxy>()?.actor : null;
        if (firstActor != null)
        {
            MissionActivated?.Invoke(m.name, first, firstActor);
        }
    }

    private IEnumerator DeactivateAllAnchorsAcrossAllMissionsAsync(int batchSize = 32)
    {
        int i = 0;
        foreach (var mission in missions)
        {
            foreach (var a in mission.anchors)
            {
                DisableAnchor(a, stopMover: true);
                if ((++i % batchSize) == 0) yield return null;
            }
        }
    }

    public void EndMission()
    {
        // var movers = FindObjectsByType<SimpleRouteMover>(FindObjectsSortMode.None);
        // foreach (var m in movers) m.StopMoving();

        DeactivateAllAnchorsAcrossAllMissions();

        //ActiveTargetManager.Instance?.SetActiveTarget(null);

        _activeAnchor = null;
        _completedThisMission.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Progress / Completion
    // ─────────────────────────────────────────────────────────────────────────────
    public bool IsMissionCompleteNow
    {
        get
        {
            var m = ActiveMission;
            return m != null && _completedThisMission.Count >= m.anchors.Count;
        }
    }

    public void NotifyAnchorArrived(MissionAnchor anchor)
    {
        var m = ActiveMission;
        if (m == null || anchor == null) return;
        if (!m.anchors.Contains(anchor)) return;

        if (!_completedThisMission.Contains(anchor))
            _completedThisMission.Add(anchor);

        var actor = GetActorForAnchor(anchor);
        AnchorArrived?.Invoke(anchor, actor, IsMissionCompleteNow);

        if (IsMissionCompleteNow)
            HandleMissionComplete();
    }

    private void HandleMissionComplete()
    {
        // ActiveTargetManager.Instance?.SetActiveTarget(null);
        MissionCompleted?.Invoke(ActiveMissionName);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Anchor visibility / selection
    // ─────────────────────────────────────────────────────────────────────────────
    private void EnableAnchor(MissionAnchor a)
    {
        if (!a) return;
        var go = a.targetObject ? a.targetObject : a.gameObject;
        if (!go) return;

        a.ResetArrivalGate();

        if (!go.activeSelf) go.SetActive(true);

        if (a.targetType == MissionTargetType.Dynamic && a.routePoints != null && a.routePoints.Count >= 2)
        {
            // var mover = go.GetComponent<SimpleRouteMover>() ?? go.AddComponent<SimpleRouteMover>();
            // mover.Configure(a.routePoints, a.moveSpeed, a.dwellSeconds, a.loop);
            // mover.Begin();
        }

        // Sync actor + register (core responsibility is fine)
        var proxy = go.GetComponent<TargetProxy>();
        // var mapper = PlayerLocator.instance?.mapper ?? FindFirstObjectByType<OnlineMapsGeoMapper>();
        // if (proxy && proxy.actor != null && mapper != null)
        // {
        //     var (lat, lon) = mapper.WorldToLatLon(go.transform.position);
        //     proxy.actor._Lat = lat;
        //     proxy.actor._Lon = lon;
        //     // ActiveTargetManager.Instance?.Register(proxy.actor);
        // }
    }

    private void DisableAnchor(MissionAnchor a, bool stopMover = true)
    {
        if (!a) return;
        var go = a.targetObject ? a.targetObject : a.gameObject;
        if (!go) return;

        if (stopMover)
        {
            // var mover = go.GetComponent<SimpleRouteMover>();
            // if (mover) mover.StopMoving();
        }
        if (go.activeSelf) go.SetActive(false);
    }

    private void DeactivateAllAnchorsAcrossAllMissions()
    {
        foreach (var mission in missions)
            foreach (var a in mission.anchors)
                DisableAnchor(a, stopMover: true);
    }

    private bool SetActiveAnchor(MissionAnchor anchor, bool isInitialSelect)
    {
        if (!anchor) return false;

        if (_activeAnchor && hideInactiveAnchors)
            DisableAnchor(_activeAnchor);

        EnableAnchor(anchor);

        var actor = GetActorForAnchor(anchor);
        if (actor == null) return false;

        _activeAnchor = anchor;
        // ActiveTargetManager.Instance?.SetActiveTarget(actor);

        ActiveTargetChanged?.Invoke(anchor, actor, isInitialSelect);
        return true;
    }

    private TargetActor GetActorForAnchor(MissionAnchor anchor)
    {
        if (!anchor) return null;
        var go = anchor.targetObject ? anchor.targetObject : anchor.gameObject;
        if (!go) return null;
        return go.GetComponent<TargetProxy>()?.actor;
    }

    public bool SelectAnotherTargetInMission(bool randomize = true)
    {
        var m = ActiveMission;
        if (m == null) { Debug.LogWarning("[MissionLoader] No active mission."); return false; }

        var candidates = new List<MissionAnchor>();
        foreach (var a in m.anchors)
            if (a && a != _activeAnchor && !_completedThisMission.Contains(a))
                candidates.Add(a);

        if (candidates.Count == 0)
        {
            HandleMissionComplete();
            return false;
        }

        var next = randomize ? candidates[UnityEngine.Random.Range(0, candidates.Count)] : candidates[0];
        return SetActiveAnchor(next, isInitialSelect: false);
    }

    public void SelectAnotherTarget() => SelectAnotherTargetInMission(true);

    // UI Hooks remain, but now they just call core selection (no UI/audio side effects here)
    public void UI_TryAnotherTarget()
    {
        bool randomize = ActiveMission?.selectRandomOnTryAnother ?? true;
        SelectAnotherTargetInMission(randomize);
    }

    public void UI_TryAnotherTargetRandom(bool randomize)
    {
        SelectAnotherTargetInMission(randomize);
    }

    public void UI_TryAnotherAndResume()
    {
        bool randomize = ActiveMission?.selectRandomOnTryAnother ?? true;
        SelectAnotherTargetInMission(randomize);
    }

    public void UI_EndMission()
    {
        EndMission();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Anchor metadata sync
    // ─────────────────────────────────────────────────────────────────────────────
    public void SyncAnchorMissionFields()
    {
        var seen = new HashSet<MissionAnchor>();

        foreach (var m in missions)
        {
            if (m == null) continue;
            foreach (var a in m.anchors)
            {
                if (!a) continue;
                a.AssignMissionMeta(m.name);
                seen.Add(a);
            }
        }

#if UNITY_EDITOR
        var all = FindObjectsByType<MissionAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var a in all)
            if (a && !seen.Contains(a))
                a.AssignMissionMeta(null);

        UnityEditor.EditorUtility.SetDirty(this);
        foreach (var m in missions)
            if (m != null)
                foreach (var a in m.anchors)
                    if (a != null) UnityEditor.EditorUtility.SetDirty(a);
#endif
    }
}
