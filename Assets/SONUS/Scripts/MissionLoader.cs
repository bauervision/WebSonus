using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Mission
{
    public string name;
    [Tooltip("Anchors that belong to this mission, in the order you want them encountered.")]
    public List<MissionAnchor> anchors = new();

    [Header("Flow")]
    public bool randomizeFirst = true;     // optional
    public bool selectRandomOnTryAnother = true;
}

public class MissionLoader : MonoBehaviour
{
    public static MissionLoader Instance { get; private set; }

    [Header("Configure in Inspector")]
    public List<Mission> missions = new();

    [Header("Runtime")]
    [SerializeField] private int activeMissionIndex = -1;
    [SerializeField] private MissionAnchor _activeAnchor; // which anchor in the active mission is currently "selected"

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        SyncAnchorMissionFields();

    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
            SyncAnchorMissionFields();
    }
#endif

    public Mission ActiveMission => (activeMissionIndex >= 0 && activeMissionIndex < missions.Count) ? missions[activeMissionIndex] : null;
    public string ActiveMissionName => ActiveMission?.name ?? "";

    // ---------- Public API ----------
    // --- Add this helper to ensure the scene is neutral while waiting to start ---
    void DeactivateAllAnchorsAcrossAllMissions()
    {
        foreach (var mission in missions)
            foreach (var a in mission.anchors)
                ToggleAnchorObject(a, false);
    }

    // --- NEW: stage-only APIs (do NOT activate) ---
    public void SelectMissionByName(string missionName)
    {
        int idx = missions.FindIndex(m => string.Equals(m.name, missionName, StringComparison.Ordinal));
        if (idx < 0) { Debug.LogWarning($"[MissionLoader] Mission not found: {missionName}"); return; }
        SelectMissionByIndex(idx);
    }

    public void SelectMissionByIndex(int index)
    {
        if (index < 0 || index >= missions.Count) { Debug.LogWarning("[MissionLoader] Bad mission index"); return; }
        EndMission();                                // clean up any previous run
        activeMissionIndex = index;                  // stage the mission
        DeactivateAllAnchorsAcrossAllMissions();     // keep scene quiet until Start
        _activeAnchor = null;
        // (Optional) notify UI: we're staged and waiting to start
        var thm = FindFirstObjectByType<TargetHUDManager>();
        if (thm) thm.SendMessage("PrepareMissionUI", SendMessageOptions.DontRequireReceiver);
    }

    // --- Bring back public ActivateMission() that starts the staged mission ---
    public void ActivateMission() => ActivateMissionInternal();

    // --- Keep these as convenience (stage + start) ---
    public void LoadMissionByName(string missionName) { SelectMissionByName(missionName); }

    public void LoadMissionByIndex(int index)
    {
        SelectMissionByIndex(index);

    }

    /// <summary>
    /// UI: Try another target within the current mission.
    /// Uses the mission's selectRandomOnTryAnother setting (or true if unset).
    /// </summary>
    public void UI_TryAnotherAndResume()
    {
        bool ok = SelectAnotherTargetInMission(true);
        if (!ok) { Debug.Log("[MissionLoader] UI_TryAnotherAndResume: no candidates."); return; }

        var fpc = FindFirstObjectByType<FirstPersonController>();
        fpc.SetUIMode(false);
        // Optional: kick guidance back on if you gate it via UI mode
        // AudioManager.Instance?.StartSonicForActiveTarget(); // if you have this
    }

    // End mission from the dialog and exit UI mode.
    public void UI_EndMission()
    {
        EndMission();
        var fpc = FindFirstObjectByType<FirstPersonController>();
        fpc.SetUIMode(false);
        UIManager.instance.EnterMapMode();
    }

    /// <summary>Choose another target within the current mission (excludes current anchor).</summary>
    public bool SelectAnotherTargetInMission(bool randomize = true)
    {
        var m = ActiveMission;
        if (m == null) { Debug.LogWarning("[MissionLoader] No active mission."); return false; }

        // Build candidate list from configured anchors
        var candidates = new List<MissionAnchor>();
        foreach (var a in m.anchors)
            if (a && a.gameObject.activeInHierarchy && a != _activeAnchor)
                candidates.Add(a);

        if (candidates.Count == 0)
        {
            Debug.Log("[MissionLoader] No remaining anchors to select.");
            return false;
        }

        var next = randomize ? candidates[UnityEngine.Random.Range(0, candidates.Count)] : candidates[0];
        return SetActiveAnchor(next, playStinger: true);
    }

    /// <summary>Convenience for your dialog button: swap to an entirely different mission by name.</summary>
    public void TryAnotherMission(string missionName)
    {
        LoadMissionByName(missionName);
    }

    public void EndMission()
    {
        // Stop any movers
        var movers = FindObjectsByType<SimpleRouteMover>(FindObjectsSortMode.None);
        foreach (var m in movers) m.StopMoving();

        // Deactivate all anchors in the active mission
        if (ActiveMission != null)
        {
            foreach (var a in ActiveMission.anchors)
                ToggleAnchorObject(a, false);
        }

        _activeAnchor = null;
        ActiveTargetManager.Instance?.SetActiveTarget(null);
    }

    // ---------- Internals ----------

    // Keep mission anchors' missionId/Name in sync with MissionLoader.missions
    public void SyncAnchorMissionFields()
    {
        var seen = new HashSet<MissionAnchor>();

        // Stamp anchors that are referenced by missions
        foreach (var m in missions)
        {
            if (m == null) continue;
            foreach (var a in m.anchors)
            {
                if (!a) continue;

                if (seen.Contains(a))
                    Debug.LogWarning($"[MissionLoader] Anchor '{a.name}' is referenced by multiple missions; last assignment wins.");

                a.AssignMissionMeta(m.name);
                seen.Add(a);
            }
        }

#if UNITY_EDITOR
        // In editor, clear stale labels on anchors that are no longer in any mission list
        var all = FindObjectsByType<MissionAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var a in all)
            if (a && !seen.Contains(a))
                a.AssignMissionMeta(null);
#endif
    }

    void ActivateMissionInternal()
    {
        var m = ActiveMission;
        if (m == null) return;

        // First, disable EVERY anchor object across all missions to guarantee a clean slate
        foreach (var mission in missions)
            foreach (var a in mission.anchors)
                ToggleAnchorObject(a, false);

        // Enable only this mission’s anchors + wire proxies/movers
        foreach (var a in m.anchors)
        {
            if (!a) continue;

            var go = a.targetObject ? a.targetObject : a.gameObject;
            go.SetActive(true);

            if (!string.IsNullOrEmpty(a.name)) go.name = a.name;

            // Route movers for Dynamics
            if (a.targetType == MissionTargetType.Dynamic && a.routePoints != null && a.routePoints.Count >= 2)
            {
                var mover = go.GetComponent<SimpleRouteMover>() ?? go.AddComponent<SimpleRouteMover>();
                mover.Configure(a.routePoints, a.moveSpeed, a.dwellSeconds, a.loop);
                mover.Begin();
            }

            // Keep TargetProxy actor lat/lon in sync up-front (Audio distances)
            var proxy = go.GetComponent<TargetProxy>();
            var mapper = PlayerLocator.instance?.mapper ?? FindFirstObjectByType<GeoMapper>();
            if (proxy && proxy.actor != null && mapper != null)
            {
                var (lat, lon) = mapper.WorldToLatLon(go.transform.position);
                proxy.actor._Lat = lat;
                proxy.actor._Lon = lon;
                ActiveTargetManager.Instance?.Register(proxy.actor);
            }
        }

        // Pick the first active target for this mission
        MissionAnchor first = null;
        var activeList = m.anchors.FindAll(a => a && (a.targetObject ? a.targetObject.activeInHierarchy : a.gameObject.activeInHierarchy));
        if (activeList.Count > 0)
        {
            first = m.randomizeFirst ? activeList[UnityEngine.Random.Range(0, activeList.Count)] : activeList[0];
        }

        if (first != null)
        {
            SetActiveAnchor(first, playStinger: true);
        }

        // Finalize any HUD
        var thm = FindFirstObjectByType<TargetHUDManager>();
        if (thm) thm.SendMessage("FinalizeMissionUI", SendMessageOptions.DontRequireReceiver);
    }

    bool SetActiveAnchor(MissionAnchor anchor, bool playStinger)
    {
        if (!anchor) return false;

        var go = anchor.targetObject ? anchor.targetObject : anchor.gameObject;
        if (!go.activeSelf) go.SetActive(true);

        // ensure next anchor can trigger arrival later
        anchor.GetComponent<MissionAnchor>().ResetArrivalGate();

        var proxy = go.GetComponent<TargetProxy>();
        if (proxy == null || proxy.actor == null) return false;

        _activeAnchor = anchor;

        ActiveTargetManager.Instance?.Register(proxy.actor);
        ActiveTargetManager.Instance?.SetActiveTarget(proxy.actor);
        if (playStinger) AudioManager.Instance?.PlayNewTargetClip(proxy.actor);

        return true;
    }

    static void ToggleAnchorObject(MissionAnchor a, bool on)
    {
        if (!a) return;
        var go = a.targetObject ? a.targetObject : a.gameObject;
        if (go && go.activeSelf != on) go.SetActive(on);
    }

    // ---------- Editor helpers (optional) ----------
#if UNITY_EDITOR
    [ContextMenu("Build Missions From Scene (group by MissionAnchor.missionId)")]
    void BuildMissionsFromScene()
    {
        var all = FindObjectsByType<MissionAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var byId = new Dictionary<string, List<MissionAnchor>>();
        foreach (var a in all)
        {
            var key = string.IsNullOrEmpty(a.missionId) ? "Unnamed" : a.missionId;
            if (!byId.TryGetValue(key, out var list)) { list = new List<MissionAnchor>(); byId[key] = list; }
            list.Add(a);
        }

        missions.Clear();
        foreach (var kv in byId)
            missions.Add(new Mission { name = kv.Key, anchors = kv.Value });

        // NEW: refresh anchor labels immediately
        SyncAnchorMissionFields();

        Debug.Log($"[MissionLoader] Built {missions.Count} mission(s) from scene anchors.");
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif


}


