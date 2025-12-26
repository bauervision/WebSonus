// Assets/SONUS/Web/TargetManager.cs
using System.Collections;
using UnityEngine;
using OnlineMaps;

public class TargetManager : MonoBehaviour
{
    [Header("Refs (from SonusMapSceneController)")]
    public SonusMapSceneController sceneController;
    public OLMGeoMapper geoMapperOL;

    [Tooltip("Player root used for distance checks in 3D")]
    public Transform playerRoot;

    [Header("3D Visual")]
    public GameObject targetPrefab;
    public float targetExtraYOffset = 0.2f;

    [Header("2D Visual")]
    public Texture2D targetMarkerTexture;
    public float targetMarkerScale = 1f;

    [Header("Spawn / Collect")]
    public float spawnDistanceMeters = 300f;
    public float spawnJitterMeters = 50f;
    public float collectRadiusMeters = 3f;

    [Header("3D Placement Probe (OnlineMaps Marker3D)")]
    [Tooltip("Recommended for tileset: use a hidden Marker3D to get elevation-aware world placement.")]
    public bool useHiddenTargetProbe = true;

    [Header("Debug")]
    public bool debugLogs = true;

    public TargetActor currentTarget;

    private Marker2D _marker2D;
    private GameObject _targetGO;

    private Marker3D _targetProbeMarker3D;
    private GameObject _targetProbePrefab;

    private Coroutine _spawnRoutine;
    private bool _isRespawning;

    private void Awake()
    {
        if (sceneController == null) sceneController = FindAny<SonusMapSceneController>();
        if (geoMapperOL == null) geoMapperOL = FindAny<OLMGeoMapper>();

        if (useHiddenTargetProbe && _targetProbePrefab == null)
        {
            _targetProbePrefab = new GameObject("TargetProbePrefab");
            _targetProbePrefab.hideFlags = HideFlags.HideAndDontSave;
            _targetProbePrefab.SetActive(false);
        }
    }

    private void Update()
    {
        // Only collect when 3D GO exists (i.e., in Scene3D)
        if (_targetGO == null || playerRoot == null) return;
        if (_isRespawning) return;

        float d = Vector3.Distance(playerRoot.position, _targetGO.transform.position);
        if (d <= collectRadiusMeters)
        {
            _isRespawning = true;
            if (debugLogs) Debug.Log($"[TargetHunt] Collected at {d:F2}m");
            StartCoroutine(RespawnFlow());
        }
    }

    // Called by SonusMapSceneController when switching modes
    public void OnEnter2D(Map map2D)
    {
        if (debugLogs) Debug.Log("[TargetHunt] Enter 2D");

        Ensure2DMarker(map2D);
        Sync2D(map2D);

        // Optional: if you want targets ONLY in 3D physically, keep this.
        Hide3D();
    }

    public void OnEnter3D()
    {
        Debug.Log($"[TargetHunt] OnEnter3D called. geoMapperOL={(geoMapperOL != null)} control={(geoMapperOL != null && geoMapperOL.control3D != null)} active={(geoMapperOL != null && geoMapperOL.control3D != null && geoMapperOL.control3D.gameObject.activeInHierarchy)}");

        if (debugLogs) Debug.Log("[TargetHunt] Enter 3D");

        // Do NOT touch Marker2D here.
        // Switching roots can invalidate marker internals; manage 2D marker only in OnEnter2D.

        if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
        _spawnRoutine = StartCoroutine(EnsureSpawnedAndPlaced3D());
    }

    // ---------------------------
    // Spawn / Respawn
    // ---------------------------

    private IEnumerator RespawnFlow()
    {
        // Clear 3D GO immediately (feels responsive)
        Hide3D();

        // Spawn a new target around the player (needs feet sample)
        yield return StartCoroutine(SpawnAroundPlayerFeet());

        // Place it in 3D (if still in 3D mode)
        yield return StartCoroutine(EnsureSpawnedAndPlaced3D());

        _isRespawning = false;
    }

    private IEnumerator EnsureSpawnedAndPlaced3D()
    {
        // Wait until player has been placed by the scene controller (prevents early probe zeros)
        const float warmTimeout = 5f;
        float tWarm0 = Time.realtimeSinceStartup;
        while (playerRoot != null && playerRoot.position == Vector3.zero && Time.realtimeSinceStartup - tWarm0 < warmTimeout)
            yield return null;


        // Need a target first
        if (currentTarget == null)
            yield return StartCoroutine(SpawnAroundPlayerFeet());

        if (currentTarget == null)
        {
            Debug.LogWarning("[TargetHunt] No target to place (spawn failed).");
            yield break;
        }

        Ensure3DGO();

        // Preferred: elevation-aware placement via Marker3D probe
        if (useHiddenTargetProbe)
        {
            yield return StartCoroutine(Place3DUsingMarkerProbe(currentTarget._Lon, currentTarget._Lat));
            yield break;
        }

        // Fallback: try LatLonToWorld (best-effort)
        if (geoMapperOL == null)
        {
            Debug.LogWarning("[TargetHunt] Missing geoMapperOL; cannot place target.");
            yield break;
        }

        const float timeoutSec = 10f;
        float t0 = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - t0 < timeoutSec)
        {
            Vector3 w = geoMapperOL.LatLonToWorld(currentTarget._Lat, currentTarget._Lon, targetExtraYOffset);
            if (w != Vector3.zero)
            {
                _targetGO.transform.position = w;
                if (debugLogs) Debug.Log($"[TargetHunt] Placed 3D target at {w} (LatLonToWorld)");
                yield break;
            }

            yield return new WaitForSeconds(0.1f);
        }

        Debug.LogWarning("[TargetHunt] Timed out placing target in 3D (LatLonToWorld stayed zero).");
    }

    private IEnumerator SpawnAroundPlayerFeet()
    {
        if (geoMapperOL == null)
        {
            Debug.LogWarning("[TargetHunt] SpawnAroundPlayerFeet: geoMapperOL is null.");
            yield break;
        }

        const float timeoutSec = 8f;
        float t0 = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - t0 < timeoutSec)
        {
            if (geoMapperOL.TryFeetScreenToLatLon(out double playerLat, out double playerLon))
            {
                float d = spawnDistanceMeters + Random.Range(-spawnJitterMeters, spawnJitterMeters);
                Vector2 latLon = GeoUtil.RandomPointAround(playerLat, playerLon, d);

                currentTarget = new TargetActor(TargetType.STATIONARY, latLon.x, latLon.y);
                currentTarget._Name = "Random Target";

                if (debugLogs)
                    Debug.Log($"[TargetHunt] Spawned target lat/lon=({currentTarget._Lat:F6},{currentTarget._Lon:F6}) ~{d:F0}m");

                yield break;
            }

            yield return new WaitForSeconds(0.1f);
        }

        if (debugLogs) Debug.LogWarning("[TargetHunt] Could not get feet sample to spawn target.");
    }

    // ---------------------------
    // 3D placement via Marker3D probe
    // ---------------------------

    private IEnumerator Place3DUsingMarkerProbe(double lng, double lat)
    {
        const float timeoutSec = 10f;
        float t0 = Time.realtimeSinceStartup;

        while (Marker3DManager.instance == null && Time.realtimeSinceStartup - t0 < timeoutSec)
            yield return null;

        if (Marker3DManager.instance == null)
        {
            Debug.LogWarning("[TargetHunt] Marker3DManager.instance is null; cannot probe place target.");
            yield break;
        }

        CreateOrMoveTargetProbe(lng, lat);

        // Give OnlineMaps a couple frames to apply elevation/placement.
        yield return null;
        yield return null;

        if (_targetProbeMarker3D == null || _targetProbeMarker3D.transform == null)
        {
            Debug.LogWarning("[TargetHunt] Target probe marker missing transform.");
            yield break;
        }

        Vector3 p = _targetProbeMarker3D.transform.position;

        // If still not ready, wait a few more frames.
        if (p == Vector3.zero)
        {
            if (debugLogs) Debug.LogWarning("[TargetHunt] Probe returned zero; waiting more frames...");
            for (int i = 0; i < 15; i++)
            {
                yield return null;
                p = _targetProbeMarker3D.transform.position;
                if (p != Vector3.zero) break;
            }
        }

        if (p == Vector3.zero)
        {
            Debug.LogWarning("[TargetHunt] Probe never resolved a valid world position.");
            CleanupTargetProbe();
            yield break;
        }

        _targetGO.transform.position = new Vector3(p.x, p.y + targetExtraYOffset, p.z);

        if (debugLogs) Debug.Log($"[TargetHunt] Placed 3D target at {_targetGO.transform.position} (Marker3D probe)");

        CleanupTargetProbe();
    }

    private void CreateOrMoveTargetProbe(double lng, double lat)
    {
        if (_targetProbePrefab == null)
        {
            _targetProbePrefab = new GameObject("TargetProbePrefab");
            _targetProbePrefab.hideFlags = HideFlags.HideAndDontSave;
            _targetProbePrefab.SetActive(false);
        }

        if (_targetProbeMarker3D == null)
        {
            _targetProbeMarker3D = Marker3DManager.CreateItem(lng, lat, _targetProbePrefab, "target-probe");
            if (_targetProbeMarker3D == null)
            {
                Debug.LogWarning("[TargetHunt] Failed to create Marker3D probe.");
                return;
            }

            _targetProbeMarker3D.sizeType = Marker3D.SizeType.scene;
        }

        _targetProbeMarker3D.enabled = true;
        _targetProbeMarker3D.location = new GeoPoint(lng, lat);
        _targetProbeMarker3D.Update();
    }

    private void CleanupTargetProbe()
    {
        if (_targetProbeMarker3D == null) return;
        _targetProbeMarker3D.enabled = false;
    }

    // ---------------------------
    // 2D marker
    // ---------------------------

    private void Ensure2DMarker(Map map2D)
    {
        if (map2D == null) return;
        if (_marker2D != null) return;
        if (targetMarkerTexture == null) return;

        if (!targetMarkerTexture.isReadable)
        {
            Debug.LogWarning($"[TargetHunt] targetMarkerTexture '{targetMarkerTexture.name}' is not readable. Enable Read/Write in import settings.");
            return;
        }

        try
        {
            _marker2D = Marker2DManager.CreateItem(0, 0, targetMarkerTexture, "target");
            if (_marker2D == null) return;

            _marker2D.align = Align.Center;
            _marker2D.scale = targetMarkerScale;
            _marker2D.enabled = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[TargetHunt] Ensure2DMarker failed: {ex.GetType().Name}: {ex.Message}");
            _marker2D = null;
        }
    }

    private void Sync2D(Map map2D)
    {
        if (_marker2D == null) return;

        if (currentTarget == null)
        {
            // Hide marker until we have a spawned target
            SafeSetMarkerEnabled(false);
            map2D?.Redraw();
            return;
        }

        SafeSetMarkerEnabled(true);

        _marker2D.location = new GeoPoint(currentTarget._Lon, currentTarget._Lat);

        // Attach actor so your TargetActor.GetMarker() pattern can still work.
        _marker2D["data"] = currentTarget;

        map2D?.Redraw();
    }

    private void SafeSetMarkerEnabled(bool enabled)
    {
        if (_marker2D == null) return;
        try
        {
            _marker2D.enabled = enabled;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[TargetHunt] Marker2D became invalid; dropping ref. {ex.GetType().Name}: {ex.Message}");
            _marker2D = null;
        }
    }

    // ---------------------------
    // 3D GO
    // ---------------------------

    private void Ensure3DGO()
    {
        if (_targetGO != null) return;

        if (targetPrefab != null)
        {
            _targetGO = Instantiate(targetPrefab, transform); // ✅ child of TargetManager
        }
        else
        {
            _targetGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _targetGO.transform.SetParent(transform, worldPositionStays: true);
        }

        _targetGO.name = "Target_World";
    }

    private void Hide3D()
    {
        if (_targetGO != null) Destroy(_targetGO);
        _targetGO = null;
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
