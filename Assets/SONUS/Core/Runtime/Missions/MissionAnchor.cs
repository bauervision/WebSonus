// MissionAnchor.cs (core-safe)
// - Detects proximity arrival
// - Notifies MissionLoader
// - NO audio, NO UI, NO FPC control

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MissionAnchor : MonoBehaviour
{
    [Header("Mission linkage (auto)")]
    [Tooltip("Auto-set from MissionLoader.missions; do not edit.")]
    public string missionName = "";

    [Tooltip("Auto-set from MissionLoader.missions; mirrors missionName unless you later add explicit slugs.")]
    public string missionId = "";

    /// <summary>Called by MissionLoader to stamp this anchor with its owning mission metadata.</summary>
    public void AssignMissionMeta(string nameOrNull)
    {
        missionName = nameOrNull ?? "";
        missionId = missionName;
    }

    public MissionTargetType targetType = MissionTargetType.Stationary;

    [Tooltip("Object toggled when mission activates (defaults to this).")]
    public GameObject targetObject;

    [Header("Dynamic route (optional)")]
    public List<Transform> routePoints = new();  // 2+ for movement
    public float moveSpeed = 10f;                // units/sec in your scene scale
    public float dwellSeconds = 1.5f;
    public bool loop = true;

    [Header("End when player is close?")]
    public bool endOnProximity = true;
    [Tooltip("Meters")] public float endDistanceMeters = 8f;

    private Coroutine _proximityCo;
    private bool _arrivalFired;

    private void OnEnable()
    {
        _arrivalFired = false;
        if (endOnProximity) _proximityCo = StartCoroutine(CoProximityWatch());
    }

    private void OnDisable()
    {
        if (_proximityCo != null)
        {
            StopCoroutine(_proximityCo);
            _proximityCo = null;
        }
    }

    /// <summary>Re-arms the arrival gate even if GO stays active (EnableAnchor() calls this).</summary>
    public void ResetArrivalGate() => _arrivalFired = false;

    private IEnumerator CoProximityWatch()
    {
        // Dependencies
        var cam = Camera.main;
        var mapper = FindFirstObjectByType<OLMGeoMapper>();
        if (!cam || !mapper) yield break;

        var go = targetObject ? targetObject : gameObject;

        while (go && go.activeInHierarchy)
        {
            // var (plat, plon) = mapper.TryWorldToLatLon(cam.transform.position);
            // var (tlat, tlon) = mapper.WorldToLatLon(go.transform.position);

            // float meters = HaversineMeters(plat, plon, tlat, tlon);

            // if (meters <= endDistanceMeters && !_arrivalFired)
            // {
            //     _arrivalFired = true;

            //     // ✅ Core responsibility: notify mission system only.
            //     if (MissionLoader.Instance != null)
            //         MissionLoader.Instance.NotifyAnchorArrived(this);
            // }

            yield return new WaitForSeconds(0.15f);
        }
    }

    private void Reset()
    {
        targetObject = gameObject;
    }

    // Great-circle distance in meters
    private static float HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double dLat = (lat2 - lat1) * Mathf.Deg2Rad;
        double dLon = (lon2 - lon1) * Mathf.Deg2Rad;

        double a = Mathf.Sin((float)(dLat / 2)) * Mathf.Sin((float)(dLat / 2)) +
                   Mathf.Cos((float)(lat1 * Mathf.Deg2Rad)) * Mathf.Cos((float)(lat2 * Mathf.Deg2Rad)) *
                   Mathf.Sin((float)(dLon / 2)) * Mathf.Sin((float)(dLon / 2));

        double c = 2 * Mathf.Atan2(Mathf.Sqrt((float)a), Mathf.Sqrt((float)(1.0 - a)));
        return (float)(R * c);
    }
}
