// MissionAnchor.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum MissionTargetType { Stationary, Dynamic }

public class MissionAnchor : MonoBehaviour
{
    [Header("Mission linkage")]
    public string missionId = "Sonic_Hunt_Stationary";
    public string targetName = "Hidden Target";
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

    void OnEnable()
    {
        if (endOnProximity) _proximityCo = StartCoroutine(CoProximityWatch());
    }
    void OnDisable()
    {
        if (_proximityCo != null) { StopCoroutine(_proximityCo); _proximityCo = null; }
    }

    IEnumerator CoProximityWatch()
    {
        // Dependencies
        var cam = AudioManager.Instance ? AudioManager.Instance.sceneCamera : Camera.main;
        var mapper = FindFirstObjectByType<GeoMapper>();
        if (!cam || !mapper) yield break;

        // Cache our mission id / target go
        var go = targetObject ? targetObject : gameObject;


        // If you have a TargetProxy on this object, update its lat/lon continuously (see section C)
        while (go.activeInHierarchy)
        {
            // Player lat/lon
            var (plat, plon) = mapper.WorldToLatLon(cam.transform.position);

            // Target lat/lon (from world — avoids any stale actor data)
            var (tlat, tlon) = mapper.WorldToLatLon(go.transform.position);

            float meters = HaversineMeters(plat, plon, tlat, tlon);



            if (meters <= endDistanceMeters)
            {
                var goActor = targetObject ? targetObject : gameObject;
                var proxy = goActor.GetComponent<TargetProxy>();
                var actor = proxy != null ? proxy.actor : null;
                // 1) Arrival VO (randomized per your SONUS.arrival buckets)
                AudioManager.Instance?.PlayArrival(actor);

                // 2) Quiet things down while deciding
                AudioManager.Instance?.StopSonic();

                // 3) Enter UI mode for the dialog
                var fpc = FindFirstObjectByType<FirstPersonController>();
                fpc.SetUIMode(true);

                MissionLoader.Instance.EndMission();
                UIManager.instance.ShowSonicCompletionDialog();
            }
            yield return new WaitForSeconds(0.15f);
        }
    }


    void Reset() { targetObject = gameObject; }

    // Great-circle distance in meters
    static float HaversineMeters(double lat1, double lon1, double lat2, double lon2)
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
