using System.Collections;
using OnlineMaps;
using UnityEngine;

public class TargetHuntController : MonoBehaviour
{
    [Header("Refs")]
    public OLMGeoMapper geoMapper;
    public Transform playerTransform;

    [Header("Visual")]
    public GameObject targetPrefab;          // simple sphere/capsule prefab
    public float targetExtraYOffset = 0.0f;  // if it clips, bump to 0.5–1.5

    [Header("Spawn tuning")]
    public float spawnDistanceMeters = 300f;
    public float spawnJitterMeters = 50f;

    [Header("Collect tuning")]
    public float collectRadiusMeters = 3f;

    [Header("Debug")]
    public bool debugLogs = true;

    public Target2DMarkerBridge markerBridge;



    // Current runtime target
    public TargetActor currentTarget;
    private GameObject _targetGo;

    private void Awake()
    {
        if (geoMapper == null) geoMapper = FindAny<OLMGeoMapper>();
        if (markerBridge == null) markerBridge = FindAny<Target2DMarkerBridge>();

    }

    private void Start()
    {
        StartCoroutine(BootAndSpawn());
    }


    private IEnumerator BootAndSpawn()
    {
        if (geoMapper == null)
        {
            Debug.LogError("[TargetHunt] geoMapper is null.");
            yield break;
        }
        if (playerTransform == null)
        {
            Debug.LogError("[TargetHunt] playerTransform is null.");
            yield break;
        }

        // 1) Wait for 2D marker system (optional but we want it)
        float t0 = Time.realtimeSinceStartup;
        while (Marker2DManager.instance == null && Time.realtimeSinceStartup - t0 < 8f)
        {
            if (debugLogs) Debug.Log("[TargetHunt] Waiting for Marker2DManager...");
            yield return new WaitForSeconds(0.1f);
        }

        // 2) Wait for 3D control active (because you start in 2D mode)
        t0 = Time.realtimeSinceStartup;
        while ((geoMapper.control3D == null || !geoMapper.control3D.gameObject.activeInHierarchy) &&
               Time.realtimeSinceStartup - t0 < 12f)
        {
            if (debugLogs) Debug.Log("[TargetHunt] Waiting for 3D control to become active...");
            yield return new WaitForSeconds(0.1f);
        }

        // 3) Now wait until feet sample works
        t0 = Time.realtimeSinceStartup;
        while (!geoMapper.TryFeetScreenToLatLon(out double playerLat, out double playerLon) &&
               Time.realtimeSinceStartup - t0 < 8f)
        {
            if (debugLogs) Debug.Log("[TargetHunt] Waiting for valid feet sample...");
            yield return new WaitForSeconds(0.1f);
        }

        if (!geoMapper.TryFeetScreenToLatLon(out double lat, out double lon))
        {
            Debug.LogError("[TargetHunt] Feet sample never became ready. No target spawned.");
            yield break;
        }

        SpawnAndPlace(lat, lon);
    }


    private void SpawnAndPlace(double playerLat, double playerLon)
    {
        float d = spawnDistanceMeters + Random.Range(-spawnJitterMeters, spawnJitterMeters);
        Vector2 latLon = GeoUtil.RandomPointAround(playerLat, playerLon, d);

        currentTarget = new TargetActor(TargetType.STATIONARY, latLon.x, latLon.y);
        currentTarget._Name = "Random Target";

        if (debugLogs)
            Debug.Log($"[TargetHunt] Spawned actor lat/lon=({currentTarget._Lat:F6},{currentTarget._Lon:F6})");

        // Ensure we have a GO
        if (_targetGo == null)
        {
            _targetGo = (targetPrefab != null) ? Instantiate(targetPrefab) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _targetGo.name = "Target_World";
        }

        // Place on surface
        Vector3 world = geoMapper.LatLonToWorld(currentTarget._Lat, currentTarget._Lon, targetExtraYOffset);
        StartCoroutine(PlaceWhenWorldReady());

        if (debugLogs)
            Debug.Log($"[TargetHunt] Placed GO world={world} (zero? {world == Vector3.zero})");

        // 2D marker
        markerBridge?.Upsert(currentTarget);
        if (debugLogs)
            Debug.Log($"[TargetHunt] Marker upsert attempted. Marker2DManager? {(Marker2DManager.instance != null)}");
    }

    private System.Collections.IEnumerator PlaceWhenWorldReady()
    {
        const float timeoutSec = 8f;
        float t0 = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - t0 < timeoutSec)
        {
            Vector3 w = geoMapper.LatLonToWorld(currentTarget._Lat, currentTarget._Lon, targetExtraYOffset);

            // Treat (0,0,0) as “not ready yet” (very common during tileset warmup)
            if (w != Vector3.zero)
            {
                _targetGo.transform.position = w;
                if (debugLogs) Debug.Log($"[TargetHunt] Placed target GO at {w}");
                yield break;
            }

            yield return new WaitForSeconds(0.10f);
        }

        Debug.LogWarning("[TargetHunt] Timed out waiting for non-zero LatLonToWorld. Target not placed.");
    }



    private void Update()
    {
        if (_targetGo == null || playerTransform == null) return;

        float d = Vector3.Distance(playerTransform.position, _targetGo.transform.position);
        if (d <= collectRadiusMeters)
        {
            if (debugLogs) Debug.Log($"[TargetHunt] COLLECTED target '{currentTarget?._ID}' at distance {d:F2}m");

            // Respawn
            SpawnNewTargetActorAroundPlayer();

            // Re-place
            StartCoroutine(EnsurePlacedWhen3DReady());
        }
    }

    // -------------------------
    // Spawn actor near player (geo)
    // -------------------------

    private void SpawnNewTargetActorAroundPlayer()
    {
        if (!geoMapper.TryFeetScreenToLatLon(out double playerLat, out double playerLon))
        {
            // If we can’t sample yet (2D mode), we still create the actor later after 3D comes online
            // but we need something deterministic to start with. So we defer.
            if (debugLogs) Debug.Log("[TargetHunt] Feet sample not ready. Deferring spawn until 3D ready.");
            currentTarget = null;
            return;
        }

        float d = spawnDistanceMeters + Random.Range(-spawnJitterMeters, spawnJitterMeters);
        Vector2 latLon = GeoUtil.RandomPointAround(playerLat, playerLon, d);

        currentTarget = new TargetActor(TargetType.STATIONARY, latLon.x, latLon.y);
        currentTarget._Name = "Random Target";
        markerBridge?.Upsert(currentTarget);


        if (debugLogs)
        {
            Debug.Log($"[TargetHunt] Spawned actor at lat/lon=({currentTarget._Lat:F6},{currentTarget._Lon:F6}) ~{d:F0}m");
        }
    }

    // -------------------------
    // Place in world (3D map must be active)
    // -------------------------

    private IEnumerator EnsurePlacedWhen3DReady()
    {
        // If we deferred actor spawn because 3D wasn’t ready yet, wait and spawn once we can sample
        yield return StartCoroutine(WaitFor3DReady());

        if (currentTarget == null)
        {
            // Now we should be able to sample player lat/lon
            SpawnNewTargetActorAroundPlayer();
            if (currentTarget == null)
            {
                Debug.LogWarning("[TargetHunt] Still unable to spawn actor after 3D ready.");
                yield break;
            }
        }

        // Create visual if needed
        if (_targetGo == null)
        {
            if (targetPrefab != null) _targetGo = Instantiate(targetPrefab);
            else _targetGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            _targetGo.name = "Target_World";
        }

        // Place it on the tileset surface
        Vector3 world = geoMapper.LatLonToWorld(currentTarget._Lat, currentTarget._Lon, targetExtraYOffset);
        _targetGo.transform.position = world;

        if (debugLogs)
        {
            Debug.Log($"[TargetHunt] Placed target GO at world={world}");
        }
    }

    private IEnumerator WaitFor3DReady()
    {
        const float timeoutSec = 8f;
        float t0 = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - t0 < timeoutSec)
        {
            // We consider “3D ready” as:
            // - mapper has a control
            // - control object is active in hierarchy
            // - we can call LatLonToWorld without returning Vector3.zero (pragmatic)
            if (geoMapper != null &&
                geoMapper.control3D != null &&
                geoMapper.control3D.gameObject.activeInHierarchy)
            {
                yield break;
            }

            yield return new WaitForSeconds(0.10f);
        }

        Debug.LogWarning("[TargetHunt] Timed out waiting for 3D control to become active.");
    }

    private static T FindAny<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        var obj = Object.FindFirstObjectByType<T>();
        if (obj != null) return obj;
        return Object.FindAnyObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}
