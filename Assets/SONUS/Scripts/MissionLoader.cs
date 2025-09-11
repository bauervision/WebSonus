using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Mission
{
    [Tooltip("Display name / identifier for this mission")]
    public string name;

    [Tooltip("Anchors that belong to this mission, in the order you want them encountered.")]
    public List<MissionAnchor> anchors = new();

    [Header("Flow")]
    [Tooltip("Pick a random first target when the mission starts.")]
    public bool randomizeFirst = true;

    [Tooltip("When the user hits \"Try Another\", should we randomize the next pick?")]
    public bool selectRandomOnTryAnother = true;
}

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

    // Progress within the active mission
    private readonly HashSet<MissionAnchor> _completedThisMission = new();

    public Mission ActiveMission =>
        (activeMissionIndex >= 0 && activeMissionIndex < missions.Count) ? missions[activeMissionIndex] : null;

    public string ActiveMissionName => ActiveMission?.name ?? string.Empty;

    // ─────────────────────────────────────────────────────────────────────────────
    // Unity
    // ─────────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Keep anchor metadata (missionName/missionId) in sync at runtime start
        SyncAnchorMissionFields();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) SyncAnchorMissionFields();
    }
#endif

    // ─────────────────────────────────────────────────────────────────────────────
    // Public API: Stage → Activate, or one-shot Load (stage+activate)
    // ─────────────────────────────────────────────────────────────────────────────
    /// <summary>Stage a mission by name. Scene remains quiet until ActivateMission().</summary>
    public void SelectMissionByName(string missionName)
    {
        int idx = missions.FindIndex(m => string.Equals(m.name, missionName, StringComparison.Ordinal));
        if (idx < 0) { Debug.LogWarning($"[MissionLoader] Mission not found: {missionName}"); return; }
        SelectMissionByIndex(idx);
    }

    /// <summary>Stage a mission by index. Scene remains quiet until ActivateMission().</summary>
    public void SelectMissionByIndex(int index)
    {
        if (index < 0 || index >= missions.Count) { Debug.LogWarning("[MissionLoader] Bad mission index"); return; }
        EndMission();                                        // cleanup prior run
        activeMissionIndex = index;                          // stage
        _completedThisMission.Clear();                       // reset progress for new stage
        _activeAnchor = null;
        DeactivateAllAnchorsAcrossAllMissions();             // stay quiet until Start
        // Optional UI signal:
        var thm = FindFirstObjectByType<TargetHUDManager>();
        if (thm) thm.SendMessage("PrepareMissionUI", SendMessageOptions.DontRequireReceiver);
    }

    /// <summary>Start (activate) the currently staged mission.</summary>
    public void ActivateMission() => ActivateMissionInternal();

    /// <summary>Convenience: stage + start by name.</summary>
    public void LoadMissionByName(string missionName)
    {
        SelectMissionByName(missionName);

    }

    /// <summary>Convenience: stage + start by index.</summary>
    public void LoadMissionByIndex(int index)
    {
        SelectMissionByIndex(index);

    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Mission run lifecycle
    // ─────────────────────────────────────────────────────────────────────────────
    private void ActivateMissionInternal()
    {
        var m = ActiveMission;
        if (m == null)
        {
            Debug.LogWarning("[MissionLoader] ActivateMissionInternal: no active mission staged.");
            return;
        }

        // Stop any movers left around
        var movers = FindObjectsByType<SimpleRouteMover>(FindObjectsSortMode.None);
        foreach (var mv in movers) mv.StopMoving();

        // Reset run state
        _completedThisMission.Clear();
        _activeAnchor = null;

        // Clean slate: everything off across all missions
        DeactivateAllAnchorsAcrossAllMissions();

        // Validate anchors
        var available = new List<MissionAnchor>();
        foreach (var a in m.anchors) if (a != null) available.Add(a);
        if (available.Count == 0)
        {
            Debug.LogWarning($"[MissionLoader] ActivateMissionInternal: mission '{m.name}' has no anchors.");
            return;
        }

        // If not hiding inactives, enable all now (but still select one below)
        if (!hideInactiveAnchors)
        {
            foreach (var a in available)
            {
                EnableAnchor(a);
                if (!string.IsNullOrEmpty(a.name))
                {
                    var go = a.targetObject ? a.targetObject : a.gameObject;
                    if (go) go.name = a.name;
                }
            }
        }

        // Choose first anchor
        MissionAnchor first = m.randomizeFirst
            ? available[UnityEngine.Random.Range(0, available.Count)]
            : available[0];

        // Select it (will hide previous if needed)
        if (!SetActiveAnchor(first, playStinger: true))
        {
            Debug.LogWarning("[MissionLoader] ActivateMissionInternal: failed to set first anchor active.");
            return;
        }

        // Finalize HUD if present
        var thm = FindFirstObjectByType<TargetHUDManager>();
        if (thm) thm.SendMessage("FinalizeMissionUI", SendMessageOptions.DontRequireReceiver);
    }

    /// <summary>Stop movers, disable anchors, clear active target & state.</summary>
    public void EndMission()
    {
        // Stop all movers
        var movers = FindObjectsByType<SimpleRouteMover>(FindObjectsSortMode.None);
        foreach (var m in movers) m.StopMoving();

        // Deactivate all anchors in all missions (safe no-op if already off)
        DeactivateAllAnchorsAcrossAllMissions();

        // Calm audio/target
        ActiveTargetManager.Instance?.SetActiveTarget(null);
        // (Optional) AudioManager.Instance?.StopSonic();

        _activeAnchor = null;
        _completedThisMission.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Progress / Completion
    // ─────────────────────────────────────────────────────────────────────────────
    /// <summary>Call this when the player arrives at an anchor (from MissionAnchor).</summary>
    public void NotifyAnchorArrived(MissionAnchor anchor)
    {
        var m = ActiveMission;
        if (m == null || anchor == null) return;
        if (!m.anchors.Contains(anchor)) return;

        if (!_completedThisMission.Contains(anchor))
            _completedThisMission.Add(anchor);

        if (_completedThisMission.Count >= m.anchors.Count)
            HandleMissionComplete();
    }

    private void HandleMissionComplete()
    {
        AudioManager.Instance?.StopSonic();
        ActiveTargetManager.Instance?.SetActiveTarget(null);

        var fpc = FindFirstObjectByType<FirstPersonController>();
        fpc?.SetUIMode(true);

        UIManager.instance.ShowMissionCompleteDialog();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Anchor visibility / selection
    // ─────────────────────────────────────────────────────────────────────────────
    private void EnableAnchor(MissionAnchor a)
    {
        if (!a) return;
        var go = a.targetObject ? a.targetObject : a.gameObject;
        if (!go) return;

        if (!go.activeSelf) go.SetActive(true);

        // Start mover if dynamic
        if (a.targetType == MissionTargetType.Dynamic && a.routePoints != null && a.routePoints.Count >= 2)
        {
            var mover = go.GetComponent<SimpleRouteMover>() ?? go.AddComponent<SimpleRouteMover>();
            mover.Configure(a.routePoints, a.moveSpeed, a.dwellSeconds, a.loop);
            mover.Begin();
        }

        // Sync actor & register
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

    private void DisableAnchor(MissionAnchor a, bool stopMover = true)
    {
        if (!a) return;
        var go = a.targetObject ? a.targetObject : a.gameObject;
        if (!go) return;

        if (stopMover)
        {
            var mover = go.GetComponent<SimpleRouteMover>();
            if (mover) mover.StopMoving();
        }
        if (go.activeSelf) go.SetActive(false);
    }

    private void DeactivateAllAnchorsAcrossAllMissions()
    {
        foreach (var mission in missions)
            foreach (var a in mission.anchors)
                DisableAnchor(a, stopMover: true);
    }

    /// <summary>Switches the active anchor (disables previous if hiding is enabled), sets active target, plays stinger.</summary>
    private bool SetActiveAnchor(MissionAnchor anchor, bool playStinger)
    {
        if (!anchor) return false;

        if (_activeAnchor && hideInactiveAnchors)
            DisableAnchor(_activeAnchor);

        EnableAnchor(anchor);

        var go = anchor.targetObject ? anchor.targetObject : anchor.gameObject;
        var proxy = go.GetComponent<TargetProxy>();
        if (proxy == null || proxy.actor == null) return false;

        _activeAnchor = anchor;
        ActiveTargetManager.Instance?.SetActiveTarget(proxy.actor);
        if (playStinger) AudioManager.Instance?.PlayNewTargetClip(proxy.actor);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Try Another (same mission)
    // ─────────────────────────────────────────────────────────────────────────────
    /// <summary>Pick another target within the current mission, excluding the current and completed anchors.</summary>
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
        return SetActiveAnchor(next, playStinger: true);
    }

    // Legacy convenience (no-arg random)
    public void SelectAnotherTarget() => SelectAnotherTargetInMission(true);

    // ─────────────────────────────────────────────────────────────────────────────
    // UI Hooks (attach in Inspector)
    // ─────────────────────────────────────────────────────────────────────────────
    /// <summary>UI: Try another target using mission's setting (or true if unset).</summary>
    public void UI_TryAnotherTarget()
    {
        bool randomize = ActiveMission?.selectRandomOnTryAnother ?? true;
        if (!SelectAnotherTargetInMission(randomize))
            Debug.Log("[MissionLoader] UI_TryAnotherTarget: no candidates available (mission may be complete).");
    }

    /// <summary>UI: Try another target; checkbox controls randomness.</summary>
    public void UI_TryAnotherTargetRandom(bool randomize)
    {
        if (!SelectAnotherTargetInMission(randomize))
            Debug.Log("[MissionLoader] UI_TryAnotherTargetRandom: no candidates available (mission may be complete).");
    }

    /// <summary>UI: Try another, then exit UI mode (resume play).</summary>
    public void UI_TryAnotherAndResume()
    {
        bool randomize = ActiveMission?.selectRandomOnTryAnother ?? true;
        if (!SelectAnotherTargetInMission(randomize)) return; // completion handled

        var fpc = FindFirstObjectByType<FirstPersonController>();
        fpc?.SetUIMode(false);
        // If you want to immediately speak a cue:
        // AudioManager.Instance?.HearNow();
    }

    /// <summary>UI: End mission and exit UI mode.</summary>
    public void UI_EndMission()
    {
        EndMission();
        var fpc = FindFirstObjectByType<FirstPersonController>();
        fpc?.SetUIMode(false);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Anchor metadata sync (keeps MissionAnchor.missionName/Id in sync)
    // ─────────────────────────────────────────────────────────────────────────────
    public void SyncAnchorMissionFields()
    {
        var seen = new HashSet<MissionAnchor>();

        // Stamp anchors referenced by missions
        foreach (var m in missions)
        {
            if (m == null) continue;
            foreach (var a in m.anchors)
            {
                if (!a) continue;
                a.AssignMissionMeta(m.name);   // MissionAnchor should implement this method
                seen.Add(a);
            }
        }

#if UNITY_EDITOR
        // In editor, clear labels for anchors not referenced by any mission
        var all = FindObjectsByType<MissionAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var a in all)
            if (a && !seen.Contains(a))
                a.AssignMissionMeta(null);

        // Mark dirty so changes persist
        UnityEditor.EditorUtility.SetDirty(this);
        foreach (var m in missions)
            if (m != null)
                foreach (var a in m.anchors)
                    if (a != null) UnityEditor.EditorUtility.SetDirty(a);
#endif
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Editor helper: Merge-build missions from scene anchors (non-destructive)
    // ─────────────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    [ContextMenu("Build Missions From Scene (Merge)")]
    private void BuildMissionsFromScene_Merge()
    {
        var index = new Dictionary<string, Mission>(StringComparer.Ordinal);
        foreach (var m in missions)
        {
            if (m == null || string.IsNullOrEmpty(m.name)) continue;
            if (!index.ContainsKey(m.name)) index[m.name] = m;
        }

        var all = FindObjectsByType<MissionAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var a in all)
        {
            if (!a) continue;

            // Prefer stamped missionName, else missionId, else "Unassigned"
            var key = !string.IsNullOrEmpty(a.missionName) ? a.missionName :
                      !string.IsNullOrEmpty(a.missionId) ? a.missionId : "Unassigned";

            if (!index.TryGetValue(key, out var mission))
            {
                mission = new Mission { name = key, anchors = new List<MissionAnchor>() };
                missions.Add(mission);
                index[key] = mission;
            }
            if (!mission.anchors.Contains(a)) mission.anchors.Add(a);
        }

        SyncAnchorMissionFields();

        Debug.Log($"[MissionLoader] Merge-built missions. Count: {missions.Count}");
        UnityEditor.EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }
#endif
}
