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
    public bool useHeading = true;
    [Range(0, 360)] public float headingDegrees = 0f;
    public float distanceMeters = 0f;
    public Vector2 toGeo; // (lat, lon) when useHeading = false
    public float speedMetersPerSecond = 10.5f;
    public float pauseAfterSeconds = 0f;
}

[System.Serializable]
public class Waypoint
{
    public Vector2 latLon;           // (lat, lon)
    public float speedToNext = 10.5f;
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

    private readonly Dictionary<string, GameObject> activeReticles = new();
    private readonly Dictionary<string, GameObject> activeIndicators = new();

    // CHANGED: cache from actor _ID -> Transform (found via TargetProxy)
    private readonly Dictionary<string, Transform> _idToTransform = new();

    private readonly Dictionary<string, Coroutine> _activeRoutes = new();
    private bool visualsEnabled = true;
    private readonly HashSet<string> groupedTargets = new();
    private int _missionVersion = 0;

    private static Vector2 FromTuple((double lat, double lon) t) => new Vector2((float)t.lat, (float)t.lon);

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }


    private void RegisterAndName(TargetActor t, string name)
    {
        if (!string.IsNullOrEmpty(name)) t._Name = name;

        // Make sure the 2D marker exists
        Ensure2DMarkerFor(t);

        // Register with your manager
        ActiveTargetManager.Instance.Register(t);

        // If it's dynamic and no route is active yet, give it a default patrol
        if ((TargetType)t._Type == TargetType.DYNAMIC && !_activeRoutes.ContainsKey(t._ID))
        {
            StartDefaultDynamicPatrol(t, rMeters: 120f, speed: 9f);
        }
    }

    private void StartDefaultDynamicPatrol(TargetActor actor, float rMeters = 120f, float speed = 9f)
    {
        // Triangle around the spawn
        Vector2 A = new Vector2((float)actor._Lat, (float)actor._Lon);
        Vector2 B = GeoUtils.OffsetLocation(A, 45f, rMeters * 0.9f);
        Vector2 C = GeoUtils.OffsetLocation(A, 200f, rMeters * 1.1f);

        var pts = new List<Waypoint>
    {
        new Waypoint(A, speed,   1.5f),
        new Waypoint(B, speed+2, 1.0f),
        new Waypoint(C, speed-1, 0.8f),
    };

        StartWaypointRoute(actor, pts, RouteMode.PingPong);
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
            RefreshAll();
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

    public void RefreshAll()
    {
        foreach (var t in TargetSceneManager.Instance.ActiveTargets) UpdateTargetUI(t);
    }

    public void OnMissionSelected(int index)
    {
        ClearHUD();

        switch (index)
        {
            case 1: LoadMission_SouthSingleDynamic(); break;
            case 2: LoadMission_WestAndSouthEastDynamics(); break;
            case 3: StartCoroutine(LoadMission_NorthGroupAndSplit()); break;
            case 4: LoadMission_NESW(); break;
            default: Debug.LogWarning("Invalid mission selected"); break;
        }
    }

    // ---------------- Missions ----------------

    private void LoadMission_WestAndSouthEastDynamics()
    {
        Vector2 userGeo = FromTuple(PlayerLocator.instance.GetCurrentLatLon());

        var west = TargetSceneManager.Instance.SpawnTarget(
            GeoUtils.OffsetLocation(userGeo, 270f, 220f), TargetType.DYNAMIC);
        RegisterAndName(west, "West Dynamic");

        var se = TargetSceneManager.Instance.SpawnTarget(
            GeoUtils.OffsetLocation(userGeo, 135f, 160f), TargetType.DYNAMIC);
        RegisterAndName(se, "Southeast Dynamic");

        FinalizeMissionUI();
    }

    private void LoadMission_SouthSingleDynamic()
    {
        Vector2 userGeo = FromTuple(PlayerLocator.instance.GetCurrentLatLon());

        TargetActor south = TargetSceneManager.Instance.SpawnTarget(
            GeoUtils.OffsetLocation(userGeo, 180f, 180f), TargetType.DYNAMIC);
        RegisterAndName(south, "South Dynamic");

        ActiveTargetManager.Instance.SetActiveTarget(south);
        RefreshAll();
        FinalizeMissionUI();

        Vector2 A = new Vector2((float)south._Lat, (float)south._Lon);
        Vector2 B = GeoUtils.OffsetLocation(A, 300f, 300f);
        Vector2 C = GeoUtils.OffsetLocation(A, 135f, 200f);

        var points = new List<Waypoint>
        {
            new Waypoint(A, 10.5f, 5f),
            new Waypoint(B, 8.0f, 10f),
            new Waypoint(C, 12.0f, 2f),
        };

        StartWaypointRoute(south, points, RouteMode.PingPong);
    }

    void LoadMission_NESW()
    {
        Vector2 userGeo = FromTuple(PlayerLocator.instance.GetCurrentLatLon());
        float[] distances = { 100f, 200f, 150f, 50f };

        CreateTargetFromOffset(userGeo, 0, distances[0], TargetType.STATIONARY);   // North
        CreateTargetFromOffset(userGeo, 90, distances[1], TargetType.DYNAMIC);     // East
        CreateTargetFromOffset(userGeo, 180, distances[2], TargetType.STATIONARY); // South
        CreateTargetFromOffset(userGeo, 270, distances[3], TargetType.DYNAMIC);    // West

        FinalizeMissionUI();
    }

    IEnumerator LoadMission_NorthGroupAndSplit()
    {
        Vector2 userGeo = FromTuple(PlayerLocator.instance.GetCurrentLatLon());

        TargetActor stationary = TargetSceneManager.Instance.SpawnTarget(
            GeoUtils.OffsetLocation(userGeo, 0f, 100f), TargetType.STATIONARY);
        stationary._Name = "North Stationary";

        TargetActor dynamic = TargetSceneManager.Instance.SpawnTarget(
            GeoUtils.OffsetLocation(userGeo, 0f, 110f), TargetType.DYNAMIC);
        dynamic._Name = "North Dynamic";

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

        FinalizeMissionUI();

        yield return new WaitForSeconds(5f);
        yield return StartCoroutine(MoveTargetByHeading(dynamic, 135f, 300f, 10.5f));
    }

    TargetActor CreateTargetFromOffset(Vector2 origin, float headingDegrees, float distanceMeters, TargetType type)
    {
        Vector2 newGeo = GeoUtils.OffsetLocation(origin, headingDegrees, distanceMeters);
        var target = TargetSceneManager.Instance.SpawnTarget(newGeo, type);

        target._Name = GetCardinalName(headingDegrees);

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
        float h = (heading % 360 + 360) % 360;
        if (h >= 315 || h < 45) return "North";
        if (h < 135) return "East";
        if (h < 225) return "South";
        return "West";
    }

    // ---------------- World→Canvas projection (CHANGED) ----------------

    bool WorldToCanvas(Vector3 world, out Vector2 canvasPos)
    {
        canvasPos = default;
        if (sceneCamera == null || canvas == null) return false;

        Vector3 screen = sceneCamera.WorldToScreenPoint(world);
        if (screen.z <= 0f) return false; // behind camera

        var cv = canvas.GetComponent<Canvas>();
        var camForCanvas = (cv != null && cv.renderMode == RenderMode.ScreenSpaceOverlay) ? null : sceneCamera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, camForCanvas, out canvasPos);
    }

    // Get the authoritative 3D position for this actor (CHANGED)
    Vector3 GetWorldPosForTarget(TargetActor t)
    {
        var tr = ResolveTargetTransform(t);
        if (tr != null) return tr.position;
        // Fallback if proxy not found (should be rare)
        return GeoUtils.GeoToWorld(new Vector2((float)t._Lat, (float)t._Lon));
    }

    // Resolve and cache Transform for an actor via TargetProxy (CHANGED)
    Transform ResolveTargetTransform(TargetActor t)
    {
        if (t == null || string.IsNullOrEmpty(t._ID)) return null;
        if (_idToTransform.TryGetValue(t._ID, out var tr) && tr != null) return tr;

        // Slow path: find by scanning proxies (typical target counts are small)
        var proxies = FindObjectsOfType<TargetProxy>();
        foreach (var p in proxies)
        {
            if (p != null && p.actor != null && p.actor._ID == t._ID)
            {
                _idToTransform[t._ID] = p.transform;
                return p.transform;
            }
        }
        return null;
    }

    // ---------------- Per-target UI ----------------

    public void UpdateTargetUI(TargetActor target)
    {
        if (!visualsEnabled) { HideReticle(target._ID); HideIndicator(target._ID); return; }
        if (groupedTargets.Contains(target._ID)) return;

        // CHANGED: use the actual 3D position (no Y flattening)
        Vector3 worldPos = GetWorldPosForTarget(target);

        // Project to canvas; if off-screen/behind camera, hide
        if (!WorldToCanvas(worldPos, out Vector2 anchoredPos))
        {
            HideReticle(target._ID);
            ShowDirectionIndicator(target._ID, worldPos); // still show direction pointer
            return;
        }

        var groupMembers = FindNearbyTargets(target);
        groupMembers.Add(target);

        bool isActive = IsActiveTarget(target._ID);

        if (groupMembers.Count > 1)
        {
            // Choose representative = closest to camera in world space (CHANGED)
            var closest = groupMembers
                .OrderBy(t => Vector3.Distance(sceneCamera.transform.position, GetWorldPosForTarget(t)))
                .First();

            groupedTargets.UnionWith(groupMembers.Select(t => t._ID));
            bool isRepresentative = target._ID == closest._ID;

            if (isRepresentative)
            {
                ShowReticle(target._ID, anchoredPos, worldPos, groupMembers.Count,
                            isActive ? activeHighlightColor : (Color?)null);
            }
            else HideReticle(target._ID);

            ShowDirectionIndicator(target._ID, worldPos);
            return;
        }

        // non-grouped visibility cone (uses world vectors)
        Vector3 toTarget = (worldPos - sceneCamera.transform.position).normalized;
        Vector3 forward = sceneCamera.transform.forward; forward.y = 0; toTarget.y = 0;
        float angleToTarget = Vector3.Angle(forward, toTarget);
        bool isVisible = angleToTarget <= 60f;

        if (isVisible)
            ShowReticle(target._ID, anchoredPos, worldPos, 0, isActive ? activeHighlightColor : (Color?)null);
        else
            HideReticle(target._ID);

        ShowDirectionIndicator(target._ID, worldPos);
    }

    // CHANGED: take anchored canvas pos directly
    private void ShowReticle(string id, Vector2 anchoredPos, Vector3 worldPos, int groupedCount = 0, Color? overrideColor = null)
    {
        if (!activeReticles.TryGetValue(id, out GameObject reticle))
        {
            reticle = Instantiate(reticlePrefab, canvas);
            activeReticles[id] = reticle;
        }
        reticle.SetActive(true);

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
                        mpImage.color = Color.white;
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
                .OrderBy(t => Vector3.Distance(sceneCamera.transform.position, GetWorldPosForTarget(t)))
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

            float absAngle = Mathf.Abs(relativeAngle);
            float fadeThreshold = 50f;
            var c = mpImage.color;
            c.a = Mathf.Clamp01(absAngle / fadeThreshold);
            mpImage.color = c;
        }

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

    // CHANGED: use transforms instead of recomputed geo → world when possible
    private bool IsWithinHeadingRange(TargetActor t1, TargetActor t2, float thresholdDegrees = 10f)
    {
        Vector3 userPos = sceneCamera.transform.position;

        Vector3 dir1 = GetWorldPosForTarget(t1) - userPos;
        Vector3 dir2 = GetWorldPosForTarget(t2) - userPos;

        dir1.y = 0;
        dir2.y = 0;

        float angle = Vector3.Angle(dir1.normalized, dir2.normalized);
        return angle <= thresholdDegrees;
    }

    public void ClearGroupingCache() { groupedTargets.Clear(); }

    public void ClearHUD()
    {
        _missionVersion++;

        foreach (var kv in _activeRoutes) { if (kv.Value != null) StopCoroutine(kv.Value); }
        _activeRoutes.Clear();

        foreach (var reticle in activeReticles.Values) Destroy(reticle);
        foreach (var indicator in activeIndicators.Values) Destroy(indicator);
        activeReticles.Clear();
        activeIndicators.Clear();

        if (multiTargetPopup) multiTargetPopup.SetActive(false);

        OnlineMapsMarkerManager.instance.RemoveAll();
        PlayerLocator.instance?.RestoreUserMarker();

        _idToTransform.Clear(); // CHANGED: also clear transform cache

        TargetSceneManager.Instance.ClearAllTargets();
    }

    private void FinalizeMissionUI()
    {
        bool multiple = TargetSceneManager.Instance.ActiveTargets.Count > 1;
        if (multiTargetPopup) multiTargetPopup.SetActive(multiple);
    }

    // ---------------- Movement helpers (unchanged except comments) ----------------

    public IEnumerator MoveTargetSmoothly(TargetActor actor, Vector2 destination, float duration = 2f)
    {
        OnlineMapsMarker marker = actor.GetMarker();
        int myVersion = _missionVersion;

        Vector2 start = new Vector2((float)actor._Lon, (float)actor._Lat); // (lon, lat)
        Vector2 end = new Vector2(destination.y, destination.x);           // (lon, lat)

        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (myVersion != _missionVersion) yield break;

            float t = Mathf.Clamp01(elapsed / duration);
            Vector2 current = Vector2.Lerp(start, end, t);

            actor._Lat = current.y;
            actor._Lon = current.x;

            if (IsMarkerUsable(marker))
            {
                marker.position = current;

                if ((TargetType)actor._Type == TargetType.DYNAMIC)
                {
                    SetMarkerRotationSafe(marker, GetBearing(start, current));
                }

                var map = OnlineMaps.instance;
                if (map != null && map.gameObject.activeInHierarchy) map.Redraw();
            }

            // CHANGED: prompt the binder’d GO to update immediately (if present)
            var tr = ResolveTargetTransform(actor);
            if (tr != null)
            {
                var binder = tr.GetComponent<TargetGeoBinder>();
                if (binder != null) binder.Apply(false);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (myVersion != _missionVersion) yield break;

        actor._Lat = destination.x;
        actor._Lon = destination.y;

        if (IsMarkerUsable(marker))
        {
            marker.position = new Vector2((float)actor._Lon, (float)actor._Lat);

            if ((TargetType)actor._Type == TargetType.DYNAMIC)
            {
                SetMarkerRotationSafe(marker, GetBearing(start, new Vector2((float)actor._Lon, (float)actor._Lat)));
            }

            var mapFinal = OnlineMaps.instance;
            if (mapFinal != null && mapFinal.gameObject.activeInHierarchy) mapFinal.Redraw();
        }

        actor._Alt = OnlineMapsElevationManagerBase.GetUnscaledElevationByCoordinate(actor._Lon, actor._Lat);
        actor._Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        // CHANGED: final binder apply
        var trFinal = ResolveTargetTransform(actor);
        if (trFinal != null)
        {
            var binder = trFinal.GetComponent<TargetGeoBinder>();
            if (binder != null) binder.Apply(false);
        }
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
        return (angle + 360f) % 360f;
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

    private float GetBearingFromHistoryOrRecentMove(TargetActor actor) { return actor._Dir; }

    private void SetMarkerRotationSafe(OnlineMapsMarker marker, float rotation)
    {
        if (marker == null) return;

        var map = OnlineMaps.instance;
        if (map == null || map.gameObject == null || !map.gameObject.activeInHierarchy || map.control == null) return;

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

        try
        {
            foreach (var m in mm.items)
                if (ReferenceEquals(m, marker)) return true;
        }
        catch { }

        return false;
    }

    private bool SafeSetMarkerPosition(OnlineMapsMarker marker, Vector2 pos)
    {
        if (IsMarkerUsable(marker)) marker.position = pos;
        return true;
    }

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
        StopRoute(actor);
        var co = StartCoroutine(RunWaypointRoute(actor, points, mode));
        _activeRoutes[actor._ID] = co;
    }

    private IEnumerator RunWaypointRoute(TargetActor actor, List<Waypoint> points, RouteMode mode)
    {
        int myVersion = _missionVersion;
        int n = points.Count;

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
                        yield return (n - 1, 0);
                    }
                case RouteMode.PingPong:
                case RouteMode.PingPongOnce:
                    {
                        var cycle = new List<(int, int)>();
                        for (int i = 0; i < n - 1; i++) cycle.Add((i, i + 1));
                        for (int i = n - 1; i >= 1; i--) cycle.Add((i, i - 1));

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

            float meters = HaversineMeters(from.latLon, to.latLon);
            float speed = Mathf.Max(0.01f, from.speedToNext);
            float duration = meters / speed;

            yield return StartCoroutine(MoveTargetSmoothly(actor, to.latLon, duration));

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
        }

        _activeRoutes.Remove(actor._ID);
    }

    public void StartRoute(TargetActor actor, IList<RouteStep> steps, bool loop = false)
    {
        if (actor == null || steps == null || steps.Count == 0) return;
        StopRoute(actor);
        var co = StartCoroutine(RunRoute(actor, steps, loop));
        _activeRoutes[actor._ID] = co;
    }

    private IEnumerator RunRoute(TargetActor actor, IList<RouteStep> steps, bool loop)
    {
        int myVersion = _missionVersion;

        while (true)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (myVersion != _missionVersion) yield break;

                var step = steps[i];
                Vector2 currentGeo = new Vector2((float)actor._Lat, (float)actor._Lon);
                Vector2 destination = step.useHeading
                    ? GeoUtils.OffsetLocation(currentGeo, step.headingDegrees, step.distanceMeters)
                    : step.toGeo;

                // ✅ meters-based duration (correct for absolute lat/lon legs)
                float metersToGo = step.useHeading
                    ? step.distanceMeters
                    : HaversineMeters(currentGeo, destination);

                float speed = Mathf.Max(0.01f, step.speedMetersPerSecond);
                float duration = Mathf.Max(0.01f, metersToGo / speed);

                yield return StartCoroutine(MoveTargetSmoothly(actor, destination, duration));

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
        }

        _activeRoutes.Remove(actor._ID);
    }


    private static float HaversineMeters(Vector2 aLatLon, Vector2 bLatLon)
    {
        const double R = 6371000.0;
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

    private OnlineMapsMarker Ensure2DMarkerFor(TargetActor t)
    {
        if (t == null) return null;

        // Try to find an existing marker that already carries this actor in its "data"
        var items = OnlineMapsMarkerManager.instance?.items;
        if (items != null)
        {
            foreach (var m in items)
            {
                if (m != null && m["data"] is TargetActor a && a._ID == t._ID)
                {
                    // refresh label + coords just in case
                    m.label = $"Target: {t._Name}";
                    m.position = new Vector2((float)t._Lon, (float)t._Lat); // (lon,lat)
                    return m;
                }
            }
        }

        // None found → create one with your icons
        Texture2D icon = AddTargetOnClick.GetIconForType((TargetType)t._Type);
        var marker = OnlineMapsMarkerManager.CreateItem(t._Lon, t._Lat, icon);
        marker.align = OnlineMapsAlign.Center;
        marker.scale = 0.4f;
        marker.rotationDegree = 0f;
        marker.label = $"Target: {t._Name}";

        // so GetMarker() + your other code paths can find it
        marker["data"] = t;
        marker["id"] = t._ID;

        OnlineMaps.instance?.Redraw();
        return marker;
    }

}
