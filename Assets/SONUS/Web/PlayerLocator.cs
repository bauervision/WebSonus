using UnityEngine;

public class PlayerLocator : MonoBehaviour
{
    public static PlayerLocator instance { get; private set; }

    [Header("2D Map (UI)")]
    public OnlineMaps map2D; // assign your 2D map OnlineMaps component

    [Header("3D Terrain Map (Tileset)")]
    public OnlineMaps terrainMap; // assign your terrain OnlineMaps component

    [Header("Mapping")]
    public OnlineMapsGeoMapper mapper; // <-- assign in Inspector
    public Terrain terrainRef;         // optional: ground height sampler

    [Header("Scene References")]
    public Texture2D userMarkerTexture;
    public Transform playerRoot;

    [Header("User Starting Position")]
    public double latitude = 37.305373;
    public double longitude = -80.611872;

    private OnlineMapsMarker userMarker;

    [Header("Sync")]
    public bool liveSyncFromPlayer = true; // true in scene, false in pure 2D map

    private bool mapCentered = false;
    private bool shouldApplySyncedRotation = false;

    [Header("Zoom")]
    public int zoom2DLevel = 17;
    public int zoom3DLevel = 19;

    private Vector2 currentRotation;

    // Diagnostics
    Vector3 _lastWorld;
    float _diagTimer;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        if (playerRoot == null)
        {
            var fpc = FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) playerRoot = fpc.transform;
            else
            {
                var rb = FindFirstObjectByType<Rigidbody>();
                if (rb != null) playerRoot = rb.transform;
            }
        }
    }

    private void Start()
    {
        // Create user marker on the 2D map
        if (map2D != null && userMarkerTexture != null)
        {
            userMarker = OnlineMapsMarkerManager.CreateItem(
                longitude,
                latitude,
                userMarkerTexture,
                "You"
            );
            userMarker.align = OnlineMapsAlign.Center;
            userMarker.scale = 0.66f;
            userMarker.rotationDegree = 0f;
        }

        CenterMaps();
        if (playerRoot != null) _lastWorld = playerRoot.position;
    }

    private void Update()
    {
        // Apply camera rotation once after sync if you ever set currentRotation
        if (shouldApplySyncedRotation && playerRoot != null)
        {
            playerRoot.rotation = Quaternion.Euler(currentRotation.y, currentRotation.x, 0f);
            shouldApplySyncedRotation = false;
            return; // Skip rest of update this frame
        }
    }

    private void LateUpdate()
    {
        // First-time center both maps
        if (!mapCentered)
        {
            CenterMaps();
            mapCentered = true;
        }

        if (!liveSyncFromPlayer || playerRoot == null || mapper == null) return;

        Vector3 wp = playerRoot.position;

        if ((wp - _lastWorld).sqrMagnitude > 0.000001f)
        {
            var (lat, lon) = mapper.WorldToLatLon(wp);

            // Guardrail: ignore wild jumps (> ~0.5°)
            if (Mathf.Abs((float)(lat - latitude)) < 0.5f &&
                Mathf.Abs((float)(lon - longitude)) < 0.5f)
            {
                latitude = lat;
                longitude = lon;

                if (userMarker != null)
                {
                    userMarker.position = new Vector2((float)longitude, (float)latitude); // (lon, lat)
                    userMarker.rotationDegree = NormalizeAngle(playerRoot.eulerAngles.y);
                }

                UpdateMapsFromLatLon();
            }
            // else: skip this frame; mapper likely not initialized yet

            _lastWorld = wp;
        }
    }

    // ----------------------
    // Map helpers
    // ----------------------

    private void CenterMaps()
    {
        // Center 2D map
        if (map2D != null)
        {
            map2D.SetPositionAndZoom(longitude, latitude, zoom2DLevel);
            map2D.Redraw();
        }

        // Center terrain map
        if (terrainMap != null)
        {
            terrainMap.SetPositionAndZoom(longitude, latitude, zoom3DLevel);
            terrainMap.Redraw();
        }
    }

    private void UpdateMapsFromLatLon()
    {
        // Keep both maps following the current lat/lon
        if (map2D != null)
        {
            map2D.position = new Vector2((float)longitude, (float)latitude);
            map2D.Redraw();
        }

        if (terrainMap != null)
        {
            terrainMap.position = new Vector2((float)longitude, (float)latitude);
            terrainMap.Redraw();
        }
    }

    /// <summary>
    /// Call this when entering Scene mode to align the terrain map with the current 2D map.
    /// </summary>
    public void SyncTerrainTo2D()
    {
        if (map2D == null || terrainMap == null) return;

        // Use our current lat/lon as the single source of truth
        terrainMap.SetPositionAndZoom(longitude, latitude, zoom3DLevel);
        terrainMap.Redraw();
    }

    // ----------------------
    // Scene mapping
    // ----------------------

    public void EnterSceneMapping()
    {
        // 1) Pause live sync so nothing rewrites lat/lon mid-setup
        liveSyncFromPlayer = false;

        // 2) Ensure mapper is fresh
        var force = mapper as IGeoMapperReinit;
        force?.ForceReinit();

        // 3) Move player to the current geo on the terrain
        MovePlayerToLatLon(latitude, longitude);

        // 4) Sync the marker heading to camera once
        SyncMarkerToCamera();

        // 5) Now it’s safe to start live sync
        liveSyncFromPlayer = true;
    }

    public void MovePlayerToLatLon(double lat, double lon)
    {
        latitude = lat;
        longitude = lon;
        if (mapper == null || playerRoot == null) return;

        // lat/lon -> world via OnlineMapsGeoMapper
        Vector3 pos = mapper.LatLonToWorld(lat, lon);

        // Optional: adjust Y using a physical terrain if present
        if (terrainRef != null)
        {
            float groundY = terrainRef.SampleHeight(pos) + terrainRef.transform.position.y;
            pos.y = groundY;
        }

        playerRoot.position = pos;

        // Keep the 2D marker in sync on move
        if (userMarker != null)
        {
            userMarker.position = new Vector2((float)longitude, (float)latitude);
        }

        UpdateMapsFromLatLon();
    }

    // ----------------------
    // Marker / camera sync
    // ----------------------

    public void SyncMarkerToCamera()
    {
        if (userMarker == null || playerRoot == null) return;
        float camYaw = NormalizeAngle(playerRoot.eulerAngles.y);
        userMarker.rotationDegree = camYaw;

        if (map2D != null) map2D.Redraw();
    }

    public void SyncCameraToMarker()
    {
        if (userMarker == null || playerRoot == null) return;

        float yaw = NormalizeAngle(userMarker.rotationDegree);
        playerRoot.Rotate(0f, yaw - playerRoot.eulerAngles.y, 0f, Space.World);
    }

    public void CenterMapAndPlaceCamera()
    {
        CenterMaps();
        SyncCameraToMarker();
        SyncMarkerToCamera();
    }

    public void RestoreUserMarker()
    {
        float lastRotation = userMarker != null ? userMarker.rotationDegree : 0f;
        if (userMarker != null) OnlineMapsMarkerManager.RemoveItem(userMarker);

        if (map2D != null && userMarkerTexture != null)
        {
            userMarker = OnlineMapsMarkerManager.CreateItem(longitude, latitude, userMarkerTexture, "You");
            userMarker.align = OnlineMapsAlign.Center;
            userMarker.scale = 0.66f;
            userMarker.rotationDegree = lastRotation;
            map2D.Redraw();
        }
    }

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        return angle;
    }

    public (double lat, double lon) GetCurrentLatLon() => (latitude, longitude);
}
