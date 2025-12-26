using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MissionLoader (Online Maps / runtime missions)
/// - Builds missions at runtime (no scene anchor references required)
/// - Spawns target prefabs and attaches MissionAnchor + TargetProxy wiring
/// - Optionally builds a quick mission with 3 targets around the player's start (N/E/W)
///
/// Key dependency:
/// - OLMGeoMapper in scene
///   - LatLonToWorld(lat, lon, extraYOffset)
///   - TryFeetScreenToLatLon(out lat, out lon)   ✅ preferred player position source
///   - TryWorldToLatLon(world, out lat, out lon) (best-effort diagnostics)
/// </summary>
public class MissionLoader : MonoBehaviour
{
    public static MissionLoader Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────────
    // Runtime Mission Loading (Online Maps)
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Runtime Mission Loading (Online Maps)")]
    [Tooltip("If true, we will build missions at runtime on Start (from defs or quick mission).")]
    public bool autoLoadMissionsOnStart = true;

    [Tooltip("If true, after building missions we will stage+activate the first mission.")]
    public bool autoStartFirstMissionOnStart = true;

    [Tooltip("Prefab spawned per target. Must be visible. Should include TargetProxy (or we add it).")]
    public GameObject targetPrefab;

    [Tooltip("Optional parent for spawned targets. If null, we create RuntimeMissionTargets.")]
    public Transform spawnedRoot;

    [Tooltip("Extra Y offset when placing spawned targets (in world units).")]
    public float spawnExtraYOffsetMeters = 0f;

    [Tooltip("Mission definitions (data-only) used to build runtime missions.")]
    public List<MissionDef> missionDefs = new();

    [Header("Quick Test Mission (spawn around player)")]
    [Tooltip("If true, we will generate one mission with 3 targets around the player at startup (N/E/W).")]
    public bool buildQuickMissionAroundPlayer = true;

    [Tooltip("Distance from player in meters (approx) for the quick mission targets.")]
    public float quickTargetOffsetMeters = 180f;

    [Tooltip("Name for the quick mission.")]
    public string quickMissionName = "Quick Mission";

    [Header("Visibility")]
    [Tooltip("If true, only the current anchor is enabled; switching targets hides the previous one.")]
    public bool hideInactiveAnchors = true;

    // Spawn tracking so we can destroy runtime targets between loads
    private readonly List<GameObject> _spawned = new();

    // ─────────────────────────────────────────────────────────────────────────────
    // Missions list (runtime)
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Missions (runtime)")]
    public List<Mission> missions = new();

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
    public event Action<string, MissionAnchor, TargetActor> MissionActivated; // missionName, firstAnchor, firstActor (may be null)
    public event Action<MissionAnchor, TargetActor, bool> ActiveTargetChanged; // anchor, actor, isInitialSelect (actor may be null)
    public event Action<MissionAnchor, TargetActor, bool> AnchorArrived; // anchor, actor, isMissionCompleteNow (actor may be null)
    public event Action<string> MissionCompleted; // missionName

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // NOTE: For runtime-built missions, anchors don't exist yet in Awake.
        // This is mainly for legacy inspector-anchored workflows.
        SyncAnchorMissionFields();
    }

    private void Start()
    {
        if (!autoLoadMissionsOnStart) return;

        if (buildQuickMissionAroundPlayer)
            BuildQuickMissionAroundPlayer();
        else
            LoadMissionsFromDefs();

        if (autoStartFirstMissionOnStart && missions.Count > 0)
        {
            SelectMissionByIndex(0);
            ActivateMission();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) SyncAnchorMissionFields();
    }
#endif

    // ─────────────────────────────────────────────────────────────────────────────
    // Runtime mission build helpers
    // ─────────────────────────────────────────────────────────────────────────────
    public void LoadMissionsFromDefs()
    {
        DestroySpawnedTargets();
        missions.Clear();

        var mapper = FindFirstObjectByType<OLMGeoMapper>();
        if (!mapper)
        {
            Debug.LogError("[MissionLoader] OLMGeoMapper not found. Cannot build missions.");
            return;
        }

        if (!targetPrefab)
        {
            Debug.LogError("[MissionLoader] targetPrefab not assigned.");
            return;
        }

        EnsureSpawnedRoot();

        foreach (var def in missionDefs)
        {
            if (def == null) continue;

            var m = new Mission
            {
                name = string.IsNullOrWhiteSpace(def.name) ? "Mission" : def.name,
                anchors = new List<MissionAnchor>(),
                randomizeFirst = def.randomizeFirst,
                selectRandomOnTryAnother = def.selectRandomOnTryAnother
            };

            foreach (var t in def.targets)
            {
                if (t == null) continue;
                var anchor = SpawnAnchorForTarget(mapper, m.name, t);
                if (anchor != null) m.anchors.Add(anchor);
            }

            missions.Add(m);
        }

        SyncAnchorMissionFields();
        Debug.Log($"[MissionLoader] Loaded {missions.Count} mission(s) from defs. Spawned {_spawned.Count} target(s).");
    }

    /// <summary>
    /// Builds a single mission with 3 stationary targets around the player's start position: North, East, West.
    /// Player position comes from OLMGeoMapper.TryFeetScreenToLatLon() (preferred for OL).
    /// </summary>
    public void BuildQuickMissionAroundPlayer()
    {
        DestroySpawnedTargets();
        missions.Clear();

        var mapper = FindFirstObjectByType<OLMGeoMapper>();
        if (!mapper)
        {
            Debug.LogError("[MissionLoader] OLMGeoMapper not found. Cannot build quick mission.");
            return;
        }

        if (!targetPrefab)
        {
            Debug.LogError("[MissionLoader] targetPrefab not assigned.");
            return;
        }

        EnsureSpawnedRoot();

        if (!mapper.TryFeetScreenToLatLon(out double plat, out double plon))
        {
            Debug.LogError("[MissionLoader] OLMGeoMapper.TryFeetScreenToLatLon failed. Ensure control3D + camera are wired and map is initialized.");
            return;
        }

        // Convert meter offsets into lat/lon deltas at this latitude (good enough for short distances)
        // 1 deg lat ≈ 111_320 m
        // 1 deg lon ≈ 111_320 * cos(lat) m
        double metersPerDegLat = 111_320.0;
        double metersPerDegLon = 111_320.0 * Math.Cos(plat * Mathf.Deg2Rad);

        double dLat = quickTargetOffsetMeters / metersPerDegLat;
        double dLon = (metersPerDegLon > 1e-6) ? (quickTargetOffsetMeters / metersPerDegLon) : 0.0;

        var m = new Mission
        {
            name = string.IsNullOrWhiteSpace(quickMissionName) ? "Quick Mission" : quickMissionName,
            anchors = new List<MissionAnchor>(),
            randomizeFirst = false, // deterministic first for testing
            selectRandomOnTryAnother = true
        };

        var targets = new List<TargetDef>
        {
            new TargetDef { id = "N", targetType = MissionTargetType.Stationary, lat = plat + dLat, lon = plon },
            new TargetDef { id = "E", targetType = MissionTargetType.Stationary, lat = plat,        lon = plon + dLon },
            new TargetDef { id = "W", targetType = MissionTargetType.Stationary, lat = plat,        lon = plon - dLon },
        };

        foreach (var t in targets)
        {
            var anchor = SpawnAnchorForTarget(mapper, m.name, t);
            if (anchor != null) m.anchors.Add(anchor);
        }

        missions.Add(m);

        SyncAnchorMissionFields();
        Debug.Log($"[MissionLoader] Built quick mission '{m.name}' around feet-sample @ ({plat:F6},{plon:F6}). Spawned {_spawned.Count} target(s).");
    }

    private void EnsureSpawnedRoot()
    {
        if (spawnedRoot) return;

        var root = GameObject.Find("RuntimeMissionTargets");
        if (!root) root = new GameObject("RuntimeMissionTargets");
        spawnedRoot = root.transform;
    }

    private MissionAnchor SpawnAnchorForTarget(OLMGeoMapper mapper, string missionName, TargetDef t)
    {
        var go = Instantiate(targetPrefab, spawnedRoot);
        go.name = $"Target_{missionName}_{t.id}";

        // Position in world from lat/lon (OLMGeoMapper adds its own yOffset; we can add extra)
        var world = mapper.LatLonToWorld(t.lat, t.lon, spawnExtraYOffsetMeters);
        if (world == Vector3.zero)
        {
            Debug.LogWarning($"[MissionLoader] LatLonToWorld returned Vector3.zero for {t.id}. Target will still be created but may be misplaced.");
        }
        go.transform.position = world;

        // Ensure TargetProxy exists + actor model exists/updated (TargetActor is NOT a Component)
        var proxy = go.GetComponent<TargetProxy>() ?? go.AddComponent<TargetProxy>();
        if (proxy.actor == null)
        {
            // Legacy TargetActor ctor takes TargetType; we’ll map Stationary/Dynamic later.
            proxy.actor = new TargetActor((TargetType)0, t.lat, t.lon);
            proxy.actor._ID = t.id;
            proxy.actor._Name = t.id;
        }
        else
        {
            proxy.actor._Lat = t.lat;
            proxy.actor._Lon = t.lon;
            proxy.actor._ID = t.id;
            proxy.actor._Name = t.id;
        }

        // Ensure MissionAnchor exists
        var anchor = go.GetComponent<MissionAnchor>() ?? go.AddComponent<MissionAnchor>();
        anchor.targetType = t.targetType;
        anchor.targetObject = go;
        anchor.AssignMissionMeta(missionName);

        // Start hidden; activation enables the relevant target
        go.SetActive(false);

        _spawned.Add(go);
        return anchor;
    }

    private void DestroySpawnedTargets()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i]) Destroy(_spawned[i]);
        }
        _spawned.Clear();
    }

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
        DeactivateAllAnchorsAcrossAllMissions();
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

        // Optional: keep actor lat/lon in sync (best-effort) when enabling
        var proxy = go.GetComponent<TargetProxy>();
        if (proxy != null && proxy.actor != null)
        {
            var mapper = FindFirstObjectByType<OLMGeoMapper>();
            if (mapper != null && mapper.TryWorldToLatLon(go.transform.position, out double lat, out double lon))
            {
                proxy.actor._Lat = lat;
                proxy.actor._Lon = lon;
            }
        }
    }

    private void DisableAnchor(MissionAnchor a, bool stopMover = true)
    {
        if (!a) return;
        var go = a.targetObject ? a.targetObject : a.gameObject;
        if (!go) return;

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

        // ✅ Visibility is the goal. Do not hard-fail on missing actor.
        var actor = GetActorForAnchor(anchor);

        _activeAnchor = anchor;

        Debug.Log($"[MissionLoader] Active anchor set: {anchor.name} (actor={(actor != null ? actor._ID : "null")})");

        ActiveTargetChanged?.Invoke(anchor, actor, isInitialSelect);
        return true;
    }

    private TargetActor GetActorForAnchor(MissionAnchor anchor)
    {
        if (!anchor) return null;
        var go = anchor.targetObject ? anchor.targetObject : anchor.gameObject;
        if (!go) return null;

        var proxy = go.GetComponent<TargetProxy>();
        return proxy != null ? proxy.actor : null;
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
