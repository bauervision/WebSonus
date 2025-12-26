using UnityEngine;

public class RandomTargetSpawner : MonoBehaviour
{
    [Header("Refs")]
    public OLMGeoMapper geoMapper;

    [Header("Spawn tuning")]
    public float distanceMeters = 300f;
    public float jitterMeters = 50f;

    [Header("Target defaults")]
    public TargetType type = TargetType.STATIONARY;
    public string targetName = "Random Target";

    [Header("Debug")]
    public bool debugLogs = true;

    public TargetActor spawned; // inspect in play mode

    private void Awake()
    {
        if (geoMapper == null) geoMapper = FindAny<OLMGeoMapper>();
    }

    private void Start()
    {
        StartCoroutine(SpawnWhenReady());
    }


    private System.Collections.IEnumerator SpawnWhenReady()
    {
        const float timeoutSec = 5.0f;     // give tiles a moment
        const float tickSec = 0.10f;        // don’t spam hit-test every frame

        float t0 = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - t0 < timeoutSec)
        {
            if (geoMapper != null && geoMapper.TryFeetScreenToLatLon(out double playerLat, out double playerLon))
            {
                float d = distanceMeters + Random.Range(-jitterMeters, jitterMeters);
                Vector2 latLon = GeoUtil.RandomPointAround(playerLat, playerLon, d);

                spawned = new TargetActor(type, latLon.x, latLon.y);
                spawned._Name = targetName;

                if (debugLogs)
                    Debug.Log($"[RandomTargetSpawner] Spawned target at ({spawned._Lat:F6},{spawned._Lon:F6}) ~{d:F0}m");

                yield break;
            }

            if (debugLogs)
                Debug.Log("[RandomTargetSpawner] Waiting for valid feet sample...");

            yield return new WaitForSeconds(tickSec);
        }

        Debug.LogWarning("[RandomTargetSpawner] Timed out waiting for TryFeetScreenToLatLon().");
    }


    private static T FindAny<T>() where T : UnityEngine.Object
    {
#if UNITY_2023_1_OR_NEWER
        var obj = UnityEngine.Object.FindFirstObjectByType<T>();
        if (obj != null) return obj;
        return UnityEngine.Object.FindAnyObjectByType<T>();
#else
        return UnityEngine.Object.FindObjectOfType<T>();
#endif
    }
}
