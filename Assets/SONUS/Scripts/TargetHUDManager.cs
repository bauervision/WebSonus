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

    private Dictionary<string, GameObject> activeReticles = new();
    private Dictionary<string, GameObject> activeIndicators = new();

    private HashSet<string> groupedTargets = new();

    private void Awake()
    {
        instance = this;
    }
    public void UpdateTargetUI(TargetActor target)
    {
        if (groupedTargets.Contains(target._ID))
            return;

        Vector3 worldPos = GeoUtils.GeoToWorld(new Vector2((float)target._Lat, (float)target._Lon));
        worldPos.y = sceneCamera.transform.position.y;
        Vector3 screenPos = sceneCamera.WorldToViewportPoint(worldPos);

        var groupMembers = FindNearbyTargets(target);
        groupMembers.Add(target); // Always include self

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
                Color groupColor;
                bool allSameType = groupMembers.All(t => t._Type == groupMembers[0]._Type);

                if (allSameType)
                    groupColor = groupMembers[0]._Type == (int)TargetType.STATIONARY ? Color.red : Color.green;
                else
                    groupColor = Color.white;

                if (screenPos.z > 0f) // Only show reticle if in front of camera
                    ShowReticle(target._ID, screenPos, worldPos, groupMembers.Count, groupColor);
            }
            else
            {
                HideReticle(target._ID);
            }

            ShowDirectionIndicator(target._ID, worldPos); // Always show regardless of screenPos.z
            return;
        }

        // 🧠 Non-grouped logic
        Vector3 toTarget = (worldPos - sceneCamera.transform.position).normalized;
        Vector3 forward = sceneCamera.transform.forward;
        forward.y = 0;
        toTarget.y = 0;
        float angleToTarget = Vector3.Angle(forward, toTarget);
        bool isVisible = angleToTarget <= 60f && screenPos.z > 0;

        if (isVisible)
        {
            ShowReticle(target._ID, screenPos, worldPos);
        }
        else
        {
            HideReticle(target._ID);
        }

        ShowDirectionIndicator(target._ID, worldPos); // ✅ Always allow full 360° rotation
    }




    private void ShowReticle(string id, Vector3 viewportPos, Vector3 worldPos, int groupedCount = 0, Color? overrideColor = null)
    {
        if (!activeReticles.TryGetValue(id, out GameObject reticle))
        {
            reticle = Instantiate(reticlePrefab, canvas);
            activeReticles[id] = reticle;
        }

        reticle.SetActive(true);

        Vector2 anchoredPos = new(
            (viewportPos.x - 0.5f) * canvas.rect.width,
            (viewportPos.y - 0.5f) * canvas.rect.height
        );

        reticle.GetComponent<RectTransform>().anchoredPosition = anchoredPos;

        var target = TargetSceneManager.Instance.GetTargetById(id);
        if (target != null)
        {
            var mpImage = reticle.GetComponentInChildren<MPImage>();
            var text = reticle.GetComponentInChildren<TextMeshProUGUI>();

            if (groupedCount > 1)
            {
                if (mpImage != null)
                {
                    mpImage.DrawShape = DrawShape.Circle;

                    var circle = mpImage.Circle;
                    circle.Radius = 50f;
                    mpImage.Circle = circle;

                    // Check if this group is all same type
                    var groupMembers = FindNearbyTargets(target);
                    groupMembers.Add(target);
                    bool allSameType = groupMembers.All(t => t._Type == groupMembers[0]._Type);

                    var effect = mpImage.GradientEffect;

                    if (allSameType)
                    {
                        // Disable gradient, use solid color
                        effect.Enabled = false;
                        mpImage.GradientEffect = effect;

                        mpImage.color = groupMembers[0]._Type == (int)TargetType.STATIONARY
                            ? Color.red
                            : Color.green;
                    }
                    else
                    {
                        // Enable gradient
                        effect.Enabled = true;
                        effect.GradientType = GradientType.Linear;
                        effect.Rotation = 90f;

                        var colorKeys = new GradientColorKey[] {
                new GradientColorKey(Color.red, 0f),
                new GradientColorKey(Color.green, 1f)
            };
                        var alphaKeys = new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            };

                        effect.Gradient.SetKeys(colorKeys, alphaKeys);
                        mpImage.GradientEffect = effect;

                        // Required for gradient to render correctly
                        mpImage.color = Color.white;
                    }

                    mpImage.SetAllDirty();
                }

                if (text != null)
                    text.text = $"{groupedCount}";
            }
            else
            {
                if (mpImage != null)
                {
                    mpImage.DrawShape = DrawShape.Rectangle;
                    mpImage.color = target._Type == (int)TargetType.STATIONARY ? Color.red : Color.green;
                }

                if (text != null)
                {
                    float distance = Vector3.Distance(sceneCamera.transform.position, worldPos);
                    text.text = distance < 1000f
                        ? $"{Mathf.RoundToInt(distance)} m"
                        : $"{(distance / 1000f):F1} km";
                }
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
        var target = TargetSceneManager.Instance.GetTargetById(id);
        if (target == null) return;

        // --- GROUPING CHECK ---
        var groupMembers = FindNearbyTargets(target);
        groupMembers.Add(target); // Always include self

        bool isGrouped = groupMembers.Count > 1;
        bool isRepresentative = true;

        if (isGrouped)
        {
            // Only show for closest
            var closest = groupMembers
                .OrderBy(t => Vector3.Distance(sceneCamera.transform.position,
                    GeoUtils.GeoToWorld(new Vector2((float)t._Lat, (float)t._Lon))))
                .First();

            groupedTargets.UnionWith(groupMembers.Select(t => t._ID));
            isRepresentative = (target._ID == closest._ID);

            if (!isRepresentative)
            {
                HideIndicator(id);
                return;
            }
        }

        // --- PREP UI ---
        if (!activeIndicators.TryGetValue(id, out GameObject indicatorGO))
        {
            indicatorGO = Instantiate(directionIndicatorPrefab, canvas);
            activeIndicators[id] = indicatorGO;
        }

        indicatorGO.SetActive(true);

        // --- ROTATION LOGIC ---
        Vector3 toTarget = worldPos - sceneCamera.transform.position;
        toTarget.y = 0;
        float angleToTarget = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        float cameraYaw = sceneCamera.transform.eulerAngles.y;
        float relativeAngle = Mathf.DeltaAngle(cameraYaw, angleToTarget);

        RectTransform rt = indicatorGO.GetComponent<RectTransform>();
        rt.localEulerAngles = new Vector3(0, 0, -relativeAngle);

        // --- SHAPE & COLOR ---
        var mpImage = indicatorGO.GetComponentInChildren<MPImage>();
        Transform triangle = indicatorGO.transform.Find("Triangle");

        if (mpImage != null)
        {
            if (isGrouped)
            {
                mpImage.DrawShape = DrawShape.Circle;

                var circle = mpImage.Circle;
                circle.Radius = 50f;
                mpImage.Circle = circle;

                // Set fill color to white to show gradient properly
                mpImage.color = Color.white;

                // Determine group color
                bool allSameType = groupMembers.All(t => t._Type == groupMembers[0]._Type);
                var effect = mpImage.GradientEffect;

                if (!allSameType)
                {
                    // Red → Green gradient
                    effect.Enabled = true;
                    effect.GradientType = GradientType.Linear;
                    effect.Rotation = 90f;

                    var colorKeys = new GradientColorKey[] {
                    new GradientColorKey(Color.red, 0f),
                    new GradientColorKey(Color.green, 1f)
                };
                    var alphaKeys = new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                };
                    effect.Gradient.SetKeys(colorKeys, alphaKeys);
                }
                else
                {
                    // Solid red or green
                    effect.Enabled = false;
                    mpImage.color = groupMembers[0]._Type == (int)TargetType.STATIONARY ? Color.red : Color.green;
                }

                mpImage.GradientEffect = effect;

            }
            else
            {
                mpImage.DrawShape = DrawShape.Triangle;
                mpImage.color = target._Type == (int)TargetType.STATIONARY ? Color.red : Color.green;

                // Disable any lingering gradient
                var effect = mpImage.GradientEffect;
                effect.Enabled = false;
                mpImage.GradientEffect = effect;


            }

            // --- FADE ALPHA ---
            float absAngle = Mathf.Abs(relativeAngle);
            float fadeThreshold = 50f;
            float alpha = Mathf.Clamp01(absAngle / fadeThreshold);

            Color current = mpImage.color;
            current.a = alpha;
            mpImage.color = current;
        }

        // --- SIZE THROB FOR TRIANGLE ---
        if (triangle != null && !isGrouped)
        {
            float distance = Vector3.Distance(sceneCamera.transform.position, worldPos);
            float scale = 0.2f;

            if (distance < 100f) scale = 0.7f;
            else if (distance < 500f) scale = 0.5f;
            else if (distance < 1000f) scale = 0.3f;

            if (distance < 100f)
            {
                float pulse = Mathf.Sin(Time.time * 5f) * 0.1f + 1.0f;
                triangle.localScale = pulse * scale * Vector3.one;
            }
            else
            {
                triangle.localScale = Vector3.one * scale;
            }
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


}
