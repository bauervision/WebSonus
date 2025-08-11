using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MPUIKIT;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


[System.Serializable]
public class RouteStep
{
    // If true, compute destination as offset from current via heading+distance.
    // If false, go to absolute lat/lon in 'toGeo'.
    public bool useHeading = true;

    [Range(0, 360)] public float headingDegrees = 0f;  // used when useHeading = true
    public float distanceMeters = 0f;                 // used when useHeading = true

    public Vector2 toGeo;                             // (lat, lon) used when useHeading = false

    public float speedMetersPerSecond = 10.5f;        // movement speed for this leg
    public float pauseAfterSeconds = 0f;              // pause after reaching this leg
}

[System.Serializable]
public class Waypoint
{
    // Absolute destination in (lat, lon)
    public Vector2 latLon;
    // Speed used when traveling FROM this point TO the next point (m/s)
    public float speedToNext = 10.5f;
    // Pause AFTER arriving at this point (seconds)
    public float pauseAfterSeconds = 0f;

    public Waypoint(Vector2 latLon, float speedToNext = 10.5f, float pause = 0f)
    {
        this.latLon = latLon;
        this.speedToNext = speedToNext;
        this.pauseAfterSeconds = pause;
    }
}

public enum RouteMode { Once, Loop, PingPong, PingPongOnce }





public class TargetHUDManager : MonoBehaviour
{
    public static TargetHUDManager instance;

    public Camera sceneCamera;
    public RectTransform canvas;
    public GameObject reticlePrefab;
    public GameObject directionIndicatorPrefab;
    public TMP_Dropdown missionDropdown;
    public GameObject multiTargetPopup;

    [SerializeField] private Color activeHighlightColor = Color.cyan;
    private Dictionary<string, GameObject> activeReticles = new();
    private Dictionary<string, GameObject> activeIndicators = new();

    // Track one running route per actor so new routes cancel old ones.
    private readonly Dictionary<string, Coroutine> _activeRoutes = new();

    private bool visualsEnabled = true; // default AR on

    private HashSet<string> groupedTargets = new();

    private int _missionVersion = 0;


    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
        missionDropdown.onValueChanged.AddListener(OnMissionSelected);
    }


    private void RegisterAndName(TargetActor t, string name)
    {
        if (!string.IsNullOrEmpty(name)) t._Name = name;

        var marker = t.GetMarker();
        if (marker != null)
        {
            marker["data"] = t;
            marker["id"] = t._ID;
            marker.label = $"Target: {t._Name}";
        }
        ActiveTargetManager.Instance.Register(t);
    }

    public void SetVisualsEnabled(bool enable)
    {
        if (visualsEnabled == enable) return;
        visualsEnabled = enable;

        if (!visualsEnabled)
        {
            HideAllVisuals();
            ClearGroupingCache();
        }
        else
        {
            ClearGroupingCache();
            RefreshAll(); // redraw once when returning to AR
        }
    }
    private bool IsActiveTarget(string id)
    {
        var a = ActiveTargetManager.Instance?.ActiveTarget;
        return a != null && a._ID == id;
    }

    public bool VisualsEnabled => visualsEnabled;

    private void HideAllVisuals()
    {
        foreach (var r in activeReticles.Values) r.SetActive(false);
        foreach (var i in activeIndicators.Values) i.SetActive(false);
    }

    // call this when AR becomes active to force a redraw
    public void RefreshAll()
    {
        foreach (var t in TargetSceneManager.Instance.ActiveTargets)
            UpdateTargetUI(t);
    }

    public void OnMissionSelected(int index)
    {

        ClearHUD();

        switch (index)
        {
            case 1:
                LoadMission_SouthSingleDynamic();
                break;
            case 2:
                LoadMission_WestAndSouthEastDynamics();
                break;
            case 3:
                StartCoroutine(LoadMission_NorthGroupAndSplit());
                break;
            case 4:
                LoadMission_NESW();
                break;
            default:
                Debug.LogWarning("Invalid mission selected");
                break;
        }
    }

    // --- NEW: Two dynamic targets (West & Southeast) ---
    private void LoadMission_WestAndSouthEastDynamics()
    {
        Vector2 userGeo = PlayerLocator.instance.GetCurrentLocation();

        // West ~220m
        var west = TargetSceneManager.Instance.SpawnTarget(
            GeoUtils.OffsetLocation(userGeo, 270f, 220f), TargetType.DYNAMIC);
        RegisterAndName(west, "West Dynamic");

        // Southeast ~160m
        var se = TargetSceneManager.Instance.SpawnTarget(
            GeoUtils.OffsetLocation(userGeo, 135f, 160f), TargetType.DYNAMIC);
        RegisterAndName(se, "Southeast Dynamic");

        FinalizeMissionUI(); // shows popup (2 targets)

        // Leave active target unset so user can choose (great for testing selection / Sonic)
        // If you prefer auto-select nearest, uncomment:
        // var nearest = (Vector3.Distance(sceneCamera.transform.position, GeoUtils.GeoToWorld(new Vector2((float)west._Lat, (float)west._Lon))) <
        //                Vector3.Distance(sceneCamera.transform.position, GeoUtils.GeoToWorld(new Vector2((float)se._Lat, (float)se._Lon)))) ? west : se;
        // ActiveTargetManager.Instance.SetActiveTarget(nearest);
        // RefreshAll();
    }

    // --- NEW: Single dynamic target (South) -> auto active ---
    private void LoadMission_SouthSingleDynamic()
    {
        Vector2 userGeo = PlayerLocator.instance.GetCurrentLocation();

        TargetActor south = TargetSceneManager.Instance.SpawnTarget(
            GeoUtils.OffsetLocation(userGeo, 180f, 180f), TargetType.DYNAMIC);
        RegisterAndName(south, "South Dynamic");

        ActiveTargetManager.Instance.SetActiveTarget(south);
        RefreshAll();
        FinalizeMissionUI();

        // Build 3 points: A at spawn, B from A, C from A (or from B—your choice)
        Vector2 A = new Vector2((float)south._Lat, (float)south._Lon);
        Vector2 B = GeoUtils.OffsetLocation(A, 300f, 300f); // 300° / 300m
        Vector2 C = GeoUtils.OffsetLocation(A, 135f, 200f); // 135° / 200m

        var points = new List<Waypoint>
    {
        new Waypoint(A, 10.5f,  5f), // leave A at 10.5 m/s, pause 5s when (re)arriving A
        new Waypoint(B,  8.0f, 10f), // leave B at 8 m/s,   pause 10s when arriving B
        new Waypoint(C, 12.0f,  2f), // leave C at 12 m/s,  pause 2s  when arriving C
    };

        // A → B → C → B → A → stop (nice for Sonic Hunt scenarios)
        StartWaypointRoute(south, points, RouteMode.PingPong);

        // Alternatives:
        // StartWaypointRoute(south, points, RouteMode.Once);      // A → B → C stop
        // StartWaypointRoute(south, points, RouteMode.Loop);      // A → B → C → A …
        // StartWaypointRoute(south, points, RouteMode.PingPong);  // A → B → C → B → A … (repeat)
    }



    void LoadMission_NESW()
    {
        Vector2 userGeo = PlayerLocator.instance.GetCurrentLocation();
        float[] distances = { 100f, 200f, 150f, 50f }; // meters

        CreateTargetFromOffset(userGeo, 0, distances[0], TargetType.STATIONARY);  // North
        CreateTargetFromOffset(userGeo, 90, distances[1], TargetType.DYNAMIC);    // East
        CreateTargetFromOffset(userGeo, 180, distances[2], TargetType.STATIONARY); // South
        CreateTargetFromOffset(userGeo, 270, distances[3], TargetType.DYNAMIC);   // West

        FinalizeMissionUI(); // shows popup (4 targets)

    }


    IEnumerator LoadMission_NorthGroupAndSplit()
    {
        Vector2 userGeo = PlayerLocator.instance.GetCurrentLocation();

        TargetActor stationary = TargetSceneManager.Instance.SpawnTarget(
            GeoUtils.OffsetLocation(userGeo, 0f, 100f), TargetType.STATIONARY);
        stationary._Name = "North Stationary";

        TargetActor dynamic = TargetSceneManager.Instance.SpawnTarget(
            GeoUtils.OffsetLocation(userGeo, 0f, 110f), TargetType.DYNAMIC);
        dynamic._Name = "North Dynamic";

        // attach to markers + register (you already do this)
        foreach (var t in new[] { stationary, dynamic })
        {
            var marker = t.GetMarker();
            if (marker != null)
            {
                marker["data"] = t;
                marker["id"] = t._ID;
                marker.label = $"Target: {t._Name}";
            }
            ActiveTargetManager.Instance.Register(t);
        }

        FinalizeMissionUI(); // shows popup (2 targets)

        yield return new WaitForSeconds(5f);

        yield return StartCoroutine(MoveTargetByHeading(dynamic, 135f, 300f, 10.5f));
    }


    TargetActor CreateTargetFromOffset(Vector2 origin, float headingDegrees, float distanceMeters, TargetType type)
    {
        Vector2 newGeo = GeoUtils.OffsetLocation(origin, headingDegrees, distanceMeters);
        var target = TargetSceneManager.Instance.SpawnTarget(newGeo, type);

        // Name it based on heading
        target._Name = GetCardinalName(headingDegrees); // "North", "East", etc.

        // Attach to marker + id
        var marker = target.GetMarker();
        if (marker != null)
        {
            marker["data"] = target;
            marker["id"] = target._ID;
            marker.label = $"Target: {target._Name}";
        }

        ActiveTargetManager.Instance.Register(target);
        return target;
    }

    private string GetCardinalName(float heading)
    {
        // 0=N, 90=E, 180=S, 270=W
        float h = (heading % 360 + 360) % 360;
        if (h >= 315 || h < 45) return "North";
        if (h < 135) return "East";
        if (h < 225) return "South";
        return "West";
    }



    public void UpdateTargetUI(TargetActor target)
    {
        if (!visualsEnabled) { HideReticle(target._ID); HideIndicator(target._ID); return; }

        if (groupedTargets.Contains(target._ID)) return;

        Vector3 worldPos = GeoUtils.GeoToWorld(new Vector2((float)target._Lat, (float)target._Lon));
        worldPos.y = sceneCamera.transform.position.y;
        Vector3 screenPos = sceneCamera.WorldToViewportPoint(worldPos);

        var groupMembers = FindNearbyTargets(target);
        groupMembers.Add(target);

        bool isActive = IsActiveTarget(target._ID);

        if (groupMembers.Count > 1)
        {
            var closest = groupMembers
                .OrderBy(t => Vector3.Distance(sceneCamera.transform.position,
                    GeoUtils.GeoToWorld(new Vector2((float)t._Lat, (float)t._Lon))))
                .First();

            groupedTargets.UnionWith(groupMembers.Select(t => t._ID));
            bool isRepresentative = target._ID == closest._ID;

            if (isRepresentative)
            {
                // Cyan if active; otherwise keep original group color logic inside ShowReticle
                ShowReticle(target._ID, screenPos, worldPos, groupMembers.Count,
                            isActive ? activeHighlightColor : (Color?)null);
            }
            else HideReticle(target._ID);

            ShowDirectionIndicator(target._ID, worldPos);
            return;
        }

        // non-grouped
        Vector3 toTarget = (worldPos - sceneCamera.transform.position).normalized;
        Vector3 forward = sceneCamera.transform.forward; forward.y = 0; toTarget.y = 0;
        float angleToTarget = Vector3.Angle(forward, toTarget);
        bool isVisible = angleToTarget <= 60f && screenPos.z > 0;

        if (isVisible)
            ShowReticle(target._ID, screenPos, worldPos, 0, isActive ? activeHighlightColor : (Color?)null);
        else
            HideReticle(target._ID);

        ShowDirectionIndicator(target._ID, worldPos);
    }





    private void ShowReticle(string id, Vector3 viewportPos, Vector3 worldPos, int groupedCount = 0, Color? overrideColor = null)
    {
        if (!activeReticles.TryGetValue(id, out GameObject reticle))
        {
            reticle = Instantiate(reticlePrefab, canvas);
            activeReticles[id] = reticle;
        }
        reticle.SetActive(true);

        Vector2 anchoredPos = new((viewportPos.x - 0.5f) * canvas.rect.width,
                                  (viewportPos.y - 0.5f) * canvas.rect.height);
        reticle.GetComponent<RectTransform>().anchoredPosition = anchoredPos;

        var target = TargetSceneManager.Instance.GetTargetById(id);
        if (target == null) return;

        var mpImage = reticle.GetComponentInChildren<MPImage>();
        var text = reticle.GetComponentInChildren<TextMeshProUGUI>();

        bool forceActiveCyan = overrideColor.HasValue || IsActiveTarget(id);

        if (groupedCount > 1)
        {
            if (mpImage != null)
            {
                mpImage.DrawShape = DrawShape.Circle;
                var circle = mpImage.Circle; circle.Radius = 50f; mpImage.Circle = circle;

                var effect = mpImage.GradientEffect;

                if (forceActiveCyan)
                {
                    effect.Enabled = false;
                    mpImage.GradientEffect = effect;
                    mpImage.color = overrideColor ?? activeHighlightColor;
                }
                else
                {
                    // existing group coloring (all same type → solid, mixed → gradient)
                    var groupMembers = FindNearbyTargets(target); groupMembers.Add(target);
                    bool allSameType = groupMembers.All(t => t._Type == groupMembers[0]._Type);

                    if (allSameType)
                    {
                        effect.Enabled = false; mpImage.GradientEffect = effect;
                        mpImage.color = groupMembers[0]._Type == (int)TargetType.STATIONARY ? Color.red : Color.green;
                    }
                    else
                    {
                        effect.Enabled = true; effect.GradientType = GradientType.Linear; effect.Rotation = 90f;
                        effect.Gradient.SetKeys(
                            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 1f) },
                            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
                        );
                        mpImage.GradientEffect = effect;
                        mpImage.color = Color.white; // required for gradient
                    }
                }
                mpImage.SetAllDirty();
            }
            if (text != null) text.text = $"{groupedCount}";
        }
        else
        {
            if (mpImage != null)
            {
                mpImage.DrawShape = DrawShape.Rectangle;
                var effect = mpImage.GradientEffect;

                if (forceActiveCyan)
                {
                    effect.Enabled = false; mpImage.GradientEffect = effect;
                    mpImage.color = overrideColor ?? activeHighlightColor;
                }
                else
                {
                    effect.Enabled = false; mpImage.GradientEffect = effect;
                    mpImage.color = target._Type == (int)TargetType.STATIONARY ? Color.red : Color.green;
                }
            }

            if (text != null)
            {
                float distance = Vector3.Distance(sceneCamera.transform.position, worldPos);
                text.text = distance < 1000f ? $"{Mathf.RoundToInt(distance)} m" : $"{(distance / 1000f):F1} km";
            }
        }
    }




    private List<TargetActor> FindNearbyTargets(TargetActor baseTarget)
    {
        List<TargetActor> nearby = new();

        foreach (var other in TargetSceneManager.Instance.ActiveTargets)
        {
            if (other._ID == baseTarget._ID) continue;

            if (IsWithinHeadingRange(baseTarget, other))
            {
                nearby.Add(other);
            }
        }

        return nearby;
    }




    private void ShowDirectionIndicator(string id, Vector3 worldPos)
    {
        if (!visualsEnabled) { HideIndicator(id); return; }

        var target = TargetSceneManager.Instance.GetTargetById(id);
        if (target == null) return;

        var groupMembers = FindNearbyTargets(target);
        groupMembers.Add(target);

        bool isGrouped = groupMembers.Count > 1;
        bool isRepresentative = true;

        if (isGrouped)
        {
            var closest = groupMembers
                .OrderBy(t => Vector3.Distance(sceneCamera.transform.position,
                    GeoUtils.GeoToWorld(new Vector2((float)t._Lat, (float)t._Lon))))
                .First();

            groupedTargets.UnionWith(groupMembers.Select(t => t._ID));
            isRepresentative = (target._ID == closest._ID);
            if (!isRepresentative) { HideIndicator(id); return; }
        }

        if (!activeIndicators.TryGetValue(id, out GameObject indicatorGO))
        {
            indicatorGO = Instantiate(directionIndicatorPrefab, canvas);
            activeIndicators[id] = indicatorGO;
        }
        indicatorGO.SetActive(true);

        // rotation (unchanged) ...
        Vector3 toTarget = worldPos - sceneCamera.transform.position; toTarget.y = 0;
        float angleToTarget = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        float cameraYaw = sceneCamera.transform.eulerAngles.y;
        float relativeAngle = Mathf.DeltaAngle(cameraYaw, angleToTarget);
        RectTransform rt = indicatorGO.GetComponent<RectTransform>();
        rt.localEulerAngles = new Vector3(0, 0, -relativeAngle);

        var mpImage = indicatorGO.GetComponentInChildren<MPImage>();
        Transform triangle = indicatorGO.transform.Find("Triangle");

        bool isActive = IsActiveTarget(id);

        if (mpImage != null)
        {
            var effect = mpImage.GradientEffect;

            if (isGrouped)
            {
                mpImage.DrawShape = DrawShape.Circle;

                if (isActive)
                {
                    effect.Enabled = false; mpImage.GradientEffect = effect;
                    mpImage.color = activeHighlightColor;
                }
                else
                {
                    // existing grouped coloring
                    bool allSameType = groupMembers.All(t => t._Type == groupMembers[0]._Type);
                    if (!allSameType)
                    {
                        effect.Enabled = true; effect.GradientType = GradientType.Linear; effect.Rotation = 90f;
                        effect.Gradient.SetKeys(
                            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 1f) },
                            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
                        );
                        mpImage.GradientEffect = effect;
                        mpImage.color = Color.white;
                    }
                    else
                    {
                        effect.Enabled = false; mpImage.GradientEffect = effect;
                        mpImage.color = groupMembers[0]._Type == (int)TargetType.STATIONARY ? Color.red : Color.green;
                    }
                }
            }
            else
            {
                mpImage.DrawShape = DrawShape.Triangle;

                effect.Enabled = false; mpImage.GradientEffect = effect;
                mpImage.color = isActive
                    ? activeHighlightColor
                    : (target._Type == (int)TargetType.STATIONARY ? Color.red : Color.green);
            }

            // fade alpha by angle (keep behavior)
            float absAngle = Mathf.Abs(relativeAngle);
            float fadeThreshold = 50f;
            var c = mpImage.color;
            c.a = Mathf.Clamp01(absAngle / fadeThreshold);
            mpImage.color = c;
        }

        // triangle scale throb unchanged...
        if (triangle != null && !isGrouped)
        {
            float distance = Vector3.Distance(sceneCamera.transform.position, worldPos);
            float scale = (distance < 100f) ? 0.7f : (distance < 500f ? 0.5f : (distance < 1000f ? 0.3f : 0.2f));
            if (distance < 100f)
            {
                float pulse = Mathf.Sin(Time.time * 5f) * 0.1f + 1.0f;
                triangle.localScale = pulse * scale * Vector3.one;
            }
            else triangle.localScale = Vector3.one * scale;
        }
    }




    private void HideReticle(string id)
    {
        if (activeReticles.TryGetValue(id, out GameObject reticle))
            reticle.SetActive(false);
    }

    private void HideIndicator(string id)
    {
        if (activeIndicators.TryGetValue(id, out GameObject indicator))
            indicator.SetActive(false);
    }


    private bool IsWithinHeadingRange(TargetActor t1, TargetActor t2, float thresholdDegrees = 10f)
    {
        Vector3 userPos = sceneCamera.transform.position;

        Vector3 dir1 = GeoUtils.GeoToWorld(new Vector2((float)t1._Lat, (float)t1._Lon)) - userPos;
        Vector3 dir2 = GeoUtils.GeoToWorld(new Vector2((float)t2._Lat, (float)t2._Lon)) - userPos;

        dir1.y = 0;
        dir2.y = 0;

        float angle = Vector3.Angle(dir1.normalized, dir2.normalized);
        return angle <= thresholdDegrees;
    }

    public void ClearGroupingCache()
    {
        groupedTargets.Clear();
    }


    public void ClearHUD()
    {

        _missionVersion++; // invalidate in-flight coroutines

        foreach (var kv in _activeRoutes) { if (kv.Value != null) StopCoroutine(kv.Value); }
        _activeRoutes.Clear();

        foreach (var reticle in activeReticles.Values)
            Destroy(reticle);
        foreach (var indicator in activeIndicators.Values)
            Destroy(indicator);

        activeReticles.Clear();
        activeIndicators.Clear();

        if (multiTargetPopup) multiTargetPopup.SetActive(false);

        // Remove all map markers
        OnlineMapsMarkerManager.instance.RemoveAll();
        // add the player back
        PlayerLocator.instance?.RestoreUserMarker();

        TargetSceneManager.Instance.ClearAllTargets();
    }

    private void FinalizeMissionUI()
    {
        bool multiple = TargetSceneManager.Instance.ActiveTargets.Count > 1;
        if (multiTargetPopup) multiTargetPopup.SetActive(multiple);
    }


    public IEnumerator MoveTargetSmoothly(TargetActor actor, Vector2 destination, float duration = 2f)
    {
        OnlineMapsMarker marker = actor.GetMarker();
        if (marker == null)
        {
            Debug.LogWarning("No marker found for target.");
            yield break;
        }

        // Capture mission version to auto-cancel on ClearHUD / reload
        int myVersion = _missionVersion;

        Vector2 start = new Vector2((float)actor._Lon, (float)actor._Lat); // (lon, lat)
        Vector2 end = new Vector2(destination.y, destination.x);         // (lon, lat)

        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Cancel if mission changed or marker/map became invalid
            if (myVersion != _missionVersion || !IsMarkerUsable(marker)) yield break;

            float t = elapsed / duration;
            Vector2 current = Vector2.Lerp(start, end, t);

            actor._Lat = current.y;
            actor._Lon = current.x;

            if (!SafeSetMarkerPosition(marker, current)) yield break;

            // Calculate and apply rotation (only for dynamic targets)
            if ((TargetType)actor._Type == TargetType.DYNAMIC)
            {
                SetMarkerRotationSafe(marker, GetBearing(start, current));
            }

            var map = OnlineMaps.instance;
            if (map != null && map.gameObject.activeInHierarchy) map.Redraw();

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Final position and rotation (with the same guards)
        if (myVersion != _missionVersion || !IsMarkerUsable(marker)) yield break;

        actor._Lat = destination.x;
        actor._Lon = destination.y;

        if (!SafeSetMarkerPosition(marker, new Vector2((float)actor._Lon, (float)actor._Lat))) yield break;

        if ((TargetType)actor._Type == TargetType.DYNAMIC)
        {
            SetMarkerRotationSafe(marker, GetBearing(start, new Vector2((float)actor._Lon, (float)actor._Lat)));
        }

        var mapFinal = OnlineMaps.instance;
        if (mapFinal != null && mapFinal.gameObject.activeInHierarchy) mapFinal.Redraw();

        actor._Alt = OnlineMapsElevationManagerBase.GetUnscaledElevationByCoordinate(actor._Lon, actor._Lat);
        actor._Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
    }



    public IEnumerator MoveTargetByHeading(TargetActor actor, float headingDegrees, float distanceMeters, float speedMetersPerSecond)
    {
        Vector2 startGeo = new Vector2((float)actor._Lat, (float)actor._Lon);
        Vector2 endGeo = GeoUtils.OffsetLocation(startGeo, headingDegrees, distanceMeters);

        float duration = distanceMeters / speedMetersPerSecond;

        yield return StartCoroutine(MoveTargetSmoothly(actor, endGeo, duration));
    }


    private float GetBearing(Vector2 from, Vector2 to)
    {
        float dLon = to.x - from.x;
        float dLat = to.y - from.y;
        float angle = Mathf.Atan2(dLon, dLat) * Mathf.Rad2Deg;
        return (angle + 360f) % 360f; // Normalize to 0–360
    }



    public void SyncAllMarkersToTargetPositions()
    {
        foreach (var actor in TargetSceneManager.Instance.ActiveTargets)
        {
            var marker = actor.GetMarker();
            if (marker != null)
            {
                marker.position = new Vector2((float)actor._Lon, (float)actor._Lat);

                if ((TargetType)actor._Type == TargetType.DYNAMIC)
                    marker.rotationDegree = GetBearingFromHistoryOrRecentMove(actor);
            }
        }

        OnlineMaps.instance?.Redraw();
    }

    private float GetBearingFromHistoryOrRecentMove(TargetActor actor)
    {
        // If you track historical positions, calculate based on last two
        // Otherwise, just return actor._Dir or fallback
        return actor._Dir; // or 0f if unknown
    }


    private void SetMarkerRotationSafe(OnlineMapsMarker marker, float rotation)
    {
        if (marker == null) return;

        var map = OnlineMaps.instance;
        if (map == null || map.gameObject == null || !map.gameObject.activeInHierarchy || map.control == null)
        {
            // Don't attempt to rotate if the map or control is disabled
            return;
        }

        marker.rotationDegree = rotation;
    }


    private bool IsMarkerUsable(OnlineMapsMarker marker)
    {
        if (marker == null) return false;

        var map = OnlineMaps.instance;
        if (map == null || map.gameObject == null || !map.gameObject.activeInHierarchy) return false;
        if (map.control == null) return false;

        var mm = OnlineMapsMarkerManager.instance;
        if (mm == null) return false;

        // Ensure marker wasn't removed
        try
        {
            foreach (var m in mm.items)
                if (ReferenceEquals(m, marker)) return true;
        }
        catch { /* items may change mid-iteration; treat as unusable */ }

        return false;
    }

    private bool SafeSetMarkerPosition(OnlineMapsMarker marker, Vector2 pos)
    {
        if (!IsMarkerUsable(marker)) return false;
        marker.position = pos;
        return true;
    }



    // optional sugar for building steps in code
    private RouteStep HeadingStep(float heading, float distance, float speed, float pause = 0f)
        => new RouteStep { useHeading = true, headingDegrees = heading, distanceMeters = distance, speedMetersPerSecond = speed, pauseAfterSeconds = pause };

    private RouteStep ToGeoStep(Vector2 latLon, float speed, float pause = 0f)
        => new RouteStep { useHeading = false, toGeo = latLon, speedMetersPerSecond = speed, pauseAfterSeconds = pause };


    public void StopRoute(TargetActor actor)
    {
        if (actor == null) return;
        if (_activeRoutes.TryGetValue(actor._ID, out var co) && co != null) StopCoroutine(co);
        _activeRoutes.Remove(actor._ID);
    }


    public void StartWaypointRoute(TargetActor actor, List<Waypoint> points, RouteMode mode)
    {
        if (actor == null || points == null || points.Count < 2) return;

        // cancel any existing
        StopRoute(actor);

        var co = StartCoroutine(RunWaypointRoute(actor, points, mode));
        _activeRoutes[actor._ID] = co;
    }

    private IEnumerator RunWaypointRoute(TargetActor actor, List<Waypoint> points, RouteMode mode)
    {
        int myVersion = _missionVersion;
        int n = points.Count;

        // Helpers to get leg endpoints by mode
        IEnumerable<(int from, int to)> LegSequence()
        {
            switch (mode)
            {
                case RouteMode.Once:
                    for (int i = 0; i < n - 1; i++) yield return (i, i + 1);
                    break;

                case RouteMode.Loop:
                    while (true)
                    {
                        for (int i = 0; i < n - 1; i++) yield return (i, i + 1);
                        yield return (n - 1, 0); // wrap back to A
                    }
                // ReSharper disable once IteratorNeverReturns
                // (intended infinite)
                case RouteMode.PingPong:
                case RouteMode.PingPongOnce:
                    {
                        // Build one cycle: 0→1→…→n-1→n-2→…→1
                        var cycle = new List<(int, int)>();
                        for (int i = 0; i < n - 1; i++) cycle.Add((i, i + 1));      // forward
                        for (int i = n - 1; i >= 1; i--) cycle.Add((i, i - 1));     // backward

                        if (mode == RouteMode.PingPongOnce)
                        {
                            foreach (var leg in cycle) yield return leg;
                        }
                        else
                        {
                            while (true)
                            {
                                foreach (var leg in cycle) yield return leg;
                            }
                        }
                        break;
                    }
            }
        }

        foreach (var (fromIdx, toIdx) in LegSequence())
        {
            if (myVersion != _missionVersion) yield break;

            var from = points[fromIdx];
            var to = points[toIdx];

            // Duration from geo distance & speed on the *from* point
            float meters = HaversineMeters(from.latLon, to.latLon);
            float speed = Mathf.Max(0.01f, from.speedToNext);
            float duration = meters / speed;

            // Move (your coroutine expects destination (lat,lon) + duration)
            yield return StartCoroutine(MoveTargetSmoothly(actor, to.latLon, duration));

            // Pause after arrival at 'to'
            float pause = Mathf.Max(0f, to.pauseAfterSeconds);
            if (pause > 0f)
            {
                float t = 0f;
                while (t < pause)
                {
                    if (myVersion != _missionVersion) yield break;
                    t += Time.deltaTime;
                    yield return null;
                }
            }

            // If RouteMode.Once: the iterator ends after last leg automatically.
        }

        // done
        _activeRoutes.Remove(actor._ID);
    }


    // Run a sequence once; set loop=true to repeat; pingPong not included here to keep it simple (can add later).
    public void StartRoute(TargetActor actor, IList<RouteStep> steps, bool loop = false)
    {
        if (actor == null || steps == null || steps.Count == 0) return;

        // Cancel any existing route for this actor
        StopRoute(actor);

        var co = StartCoroutine(RunRoute(actor, steps, loop));
        _activeRoutes[actor._ID] = co;
    }

    private IEnumerator RunRoute(TargetActor actor, IList<RouteStep> steps, bool loop)
    {
        // Capture mission version so clearing missions cancels mid-route.
        int myVersion = _missionVersion;

        while (true)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (myVersion != _missionVersion) yield break; // mission changed

                var step = steps[i];

                // Compute destination (lat, lon)
                Vector2 currentGeo = new Vector2((float)actor._Lat, (float)actor._Lon); // (lat, lon)
                Vector2 destination;

                if (step.useHeading)
                {
                    destination = GeoUtils.OffsetLocation(currentGeo, step.headingDegrees, step.distanceMeters);
                }
                else
                {
                    destination = step.toGeo; // absolute (lat, lon)
                }

                // Move there at given speed
                float duration = Mathf.Max(0.01f, step.distanceMeters > 0f && step.useHeading
                    ? step.distanceMeters / Mathf.Max(0.01f, step.speedMetersPerSecond)
                    : Vector2.Distance(new Vector2((float)actor._Lat, (float)actor._Lon), destination) / Mathf.Max(0.01f, step.speedMetersPerSecond));

                // Your MoveTargetSmoothly expects (actor, destination(lat,lon), durationSeconds)
                yield return StartCoroutine(MoveTargetSmoothly(actor, destination, duration));

                // Optional pause
                if (step.pauseAfterSeconds > 0f)
                {
                    float t = 0f;
                    while (t < step.pauseAfterSeconds)
                    {
                        if (myVersion != _missionVersion) yield break;
                        t += Time.deltaTime;
                        yield return null;
                    }
                }
            }

            if (!loop) break;
            // loop: repeat from first step
        }

        // finished
        _activeRoutes.Remove(actor._ID);
    }




    // Start a ping-pong route across the given waypoints (A..Z..A..)
    public void StartWaypointPingPong(TargetActor actor, List<Waypoint> points)
    {
        if (actor == null || points == null || points.Count < 2) return;

        // cancel any existing
        StopRoute(actor);

        var co = StartCoroutine(RunWaypointPingPong(actor, points));
        _activeRoutes[actor._ID] = co;
    }

    private IEnumerator RunWaypointPingPong(TargetActor actor, List<Waypoint> points)
    {
        // mission guard
        int myVersion = _missionVersion;

        // Precompute the index pattern: 0->1->...->N-1->N-2->...->1 and repeat
        int n = points.Count;
        var forward = new List<int>(n);
        for (int i = 0; i < n; i++) forward.Add(i);

        var backward = new List<int>(Mathf.Max(0, n - 2));
        for (int i = n - 2; i >= 1; i--) backward.Add(i);

        var cycle = new List<int>(forward.Count + backward.Count);
        cycle.AddRange(forward);
        cycle.AddRange(backward);
        // Example n=3 => cycle: [0,1,2,1] repeating

        // Start from actor's current position; assume it is at points[0] or near it
        int idx = 0;

        while (true)
        {
            // from cycle[idx] to cycle[idx+1]
            int fromIdx = cycle[idx % cycle.Count];
            int toIdx = cycle[(idx + 1) % cycle.Count];

            if (myVersion != _missionVersion) yield break;

            Waypoint from = points[fromIdx];
            Waypoint to = points[toIdx];

            // distance -> duration using 'from.speedToNext'
            float meters = HaversineMeters(from.latLon, to.latLon); // geo distance in meters
            float speed = Mathf.Max(0.01f, from.speedToNext);
            float duration = meters / speed;

            // Move
            yield return StartCoroutine(MoveTargetSmoothly(actor, to.latLon, duration));

            // Pause AFTER arriving at 'to'
            float pause = Mathf.Max(0f, to.pauseAfterSeconds);
            if (pause > 0f)
            {
                float t = 0f;
                while (t < pause)
                {
                    if (myVersion != _missionVersion) yield break;
                    t += Time.deltaTime;
                    yield return null;
                }
            }

            idx++;
        }
    }


    private static float HaversineMeters(Vector2 aLatLon, Vector2 bLatLon)
    {
        // a=(lat,lon), b=(lat,lon) in degrees
        const double R = 6371000.0; // Earth radius in m
        double lat1 = aLatLon.x * Mathf.Deg2Rad;
        double lat2 = bLatLon.x * Mathf.Deg2Rad;
        double dLat = (bLatLon.x - aLatLon.x) * Mathf.Deg2Rad;
        double dLon = (bLatLon.y - aLatLon.y) * Mathf.Deg2Rad;

        double s = Mathf.Sin((float)(dLat / 2.0));
        double t = Mathf.Sin((float)(dLon / 2.0));
        double h = s * s + Mathf.Cos((float)lat1) * Mathf.Cos((float)lat2) * t * t;
        double c = 2.0 * Mathf.Atan2(Mathf.Sqrt((float)h), Mathf.Sqrt((float)(1.0 - h)));
        return (float)(R * c);
    }


}
