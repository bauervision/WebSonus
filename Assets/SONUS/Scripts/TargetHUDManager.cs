using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MPUIKIT;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    private bool visualsEnabled = true; // default AR on

    private HashSet<string> groupedTargets = new();

    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
        missionDropdown.onValueChanged.AddListener(OnMissionSelected);
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
        multiTargetPopup.SetActive(true);
        ClearHUD();
        switch (index)
        {
            case 1:
                LoadMission_NESW();
                break;
            case 2:
                StartCoroutine(LoadMission_NorthGroupAndSplit());
                break;
            default:
                Debug.LogWarning("Invalid mission selected");
                break;
        }
    }

    void LoadMission_NESW()
    {
        Vector2 userGeo = PlayerLocator.instance.GetCurrentLocation();
        float[] distances = { 100f, 200f, 150f, 50f }; // meters

        CreateTargetFromOffset(userGeo, 0, distances[0], TargetType.STATIONARY);  // North
        CreateTargetFromOffset(userGeo, 90, distances[1], TargetType.DYNAMIC);    // East
        CreateTargetFromOffset(userGeo, 180, distances[2], TargetType.STATIONARY); // South
        CreateTargetFromOffset(userGeo, 270, distances[3], TargetType.DYNAMIC);   // West
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
        foreach (var reticle in activeReticles.Values)
            Destroy(reticle);
        foreach (var indicator in activeIndicators.Values)
            Destroy(indicator);

        activeReticles.Clear();
        activeIndicators.Clear();

        // Remove all map markers
        OnlineMapsMarkerManager.instance.RemoveAll();
        // add the player back
        PlayerLocator.instance?.RestoreUserMarker();

        TargetSceneManager.Instance.ClearAllTargets();
    }


    public IEnumerator MoveTargetSmoothly(TargetActor actor, Vector2 destination, float duration = 2f)
    {
        OnlineMapsMarker marker = actor.GetMarker();
        if (marker == null)
        {
            Debug.LogWarning("No marker found for target.");
            yield break;
        }

        Vector2 start = new Vector2((float)actor._Lon, (float)actor._Lat); // (lon, lat)
        Vector2 end = new Vector2(destination.y, destination.x);           // (lon, lat)

        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            Vector2 current = Vector2.Lerp(start, end, t);

            actor._Lat = current.y;
            actor._Lon = current.x;
            marker.position = current;

            // Calculate and apply rotation (only for dynamic targets)
            if ((TargetType)actor._Type == TargetType.DYNAMIC)
            {
                SetMarkerRotationSafe(marker, GetBearing(start, current));
            }

            if (OnlineMaps.instance != null && OnlineMaps.instance.gameObject.activeInHierarchy)
                OnlineMaps.instance.Redraw();

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Final position and rotation
        actor._Lat = destination.x;
        actor._Lon = destination.y;
        marker.position = new Vector2((float)actor._Lon, (float)actor._Lat);

        if ((TargetType)actor._Type == TargetType.DYNAMIC)
        {
            marker.rotationDegree = GetBearing(start, new Vector2((float)actor._Lon, (float)actor._Lat));
        }

        OnlineMaps.instance.Redraw();

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



}
