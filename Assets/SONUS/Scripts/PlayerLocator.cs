using UnityEngine;

public class PlayerLocator : MonoBehaviour
{
    public static PlayerLocator instance { get; private set; }



    [Header("2D Map")]
    public OnlineMaps map;

    [Header("Mapping")]
    public GeoMapper mapper;               // <-- assign in Inspector
    public Terrain terrainRef;             // <-- assign your baked Terrain

    [Header("Scene References")]

    public Texture2D userMarkerTexture;
    public Transform playerRoot;

    [Header("User Starting Position")]
    public double latitude = 37.305373;
    public double longitude = -80.611872;

    private OnlineMapsMarker userMarker;

    [Header("Sync")]
    public bool liveSyncFromPlayer = true; // set true in Scene mode, false in 2D map mode



    private bool mapCentered = false;

    private bool shouldApplySyncedRotation = false;

    int zoom3dLevel = 19;
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
        // Create user marker (lon, lat)
        // userMarker = OnlineMapsMarkerManager.CreateItem(
        //     longitude, latitude, userMarkerTexture, "You"
        // );
        // userMarker.align = OnlineMapsAlign.Center;
        // userMarker.scale = 0.66f;
        // userMarker.rotationDegree = 0f;

        // // Set map position now, and again in LateUpdate (to ensure full center)
        // map.SetPositionAndZoom(longitude, latitude, 17);
        // map.Redraw();

        // if (playerRoot != null) _lastWorld = playerRoot.position;
    }

    private Vector2 currentRotation;

    void Update()
    {
        // Apply camera rotation once after sync
        if (shouldApplySyncedRotation)
        {
            playerRoot.rotation = Quaternion.Euler(currentRotation.y, currentRotation.x, 0f);
            shouldApplySyncedRotation = false;
            return; // Skip rest of update
        }


    }


    // 3) Lightweight diagnostics to confirm movement + mapping
    Vector3 _lastWorld;
    float _diagTimer;

    void LateUpdate()
    {
        if (!mapCentered)
        {
            map.position = new Vector2((float)longitude, (float)latitude);
            map.Redraw();
            mapCentered = true;
        }

        if (liveSyncFromPlayer && playerRoot != null && mapper != null)
        {
            Vector3 wp = playerRoot.position;

            if ((wp - _lastWorld).sqrMagnitude > 0.000001f)
            {
                var (lat, lon) = mapper.WorldToLatLon(wp);

                // Guardrail: ignore wild jumps (> ~0.5° ≈ 55 km lat)
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
                    map.Redraw();
                }
                // else: skip this frame; mapper likely not initialized yet

                _lastWorld = wp;
            }
        }
    }




    void OnValidate()
    {
        if (mapper != null && terrainRef != null)
        {
            if (mapper.terrainOrigin == null) mapper.terrainOrigin = terrainRef.transform;
            var sz = terrainRef.terrainData != null ? terrainRef.terrainData.size : Vector3.zero;
            if (sz.x > 0 && sz.z > 0) mapper.worldSizeXZ = new Vector2(sz.x, sz.z);
        }
    }



    public void EnterSceneMapping()
    {
        // 1) Pause live sync so nothing rewrites lat/lon mid-setup
        liveSyncFromPlayer = false;

        // 2) If your GeoMapper has a cache, force it to rebuild (see Step 3 below)
        var force = mapper as IGeoMapperReinit;  // optional interface (Step 3)
        force?.ForceReinit();

        // 3) Move player to the current geo on the baked terrain (world gets set correctly)
        MovePlayerToLatLon(latitude, longitude);

        // 4) Sync the marker heading once
        SyncMarkerToCamera();

        // 5) Now it’s safe to start live sync
        liveSyncFromPlayer = true;
    }



    public void MovePlayerToLatLon(double lat, double lon)
    {
        latitude = lat; longitude = lon;
        if (mapper == null || playerRoot == null) return;

        // lat/lon -> world
        Vector3 pos = mapper.LatLonToWorld(lat, lon);
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
    }

    public void SyncMarkerToCamera()
    {
        if (userMarker == null || playerRoot == null) return;
        float camYaw = NormalizeAngle(playerRoot.eulerAngles.y);
        userMarker.rotationDegree = camYaw;
        map.Redraw();
    }

    public void SyncCameraToMarker()
    {
        if (userMarker == null || playerRoot == null) return;

        float yaw = NormalizeAngle(userMarker.rotationDegree);
        playerRoot.Rotate(0f, yaw - playerRoot.eulerAngles.y, 0f, Space.World);
    }

    public void CenterMapAndPlaceCamera()
    {
        if (map != null)
        {
            map.SetPositionAndZoom(longitude, latitude, 17);
            map.Redraw();
        }
        SyncCameraToMarker();
        SyncMarkerToCamera();
    }

    public void RestoreUserMarker()
    {
        float lastRotation = userMarker != null ? userMarker.rotationDegree : 0f;
        if (userMarker != null) OnlineMapsMarkerManager.RemoveItem(userMarker);

        userMarker = OnlineMapsMarkerManager.CreateItem(longitude, latitude, userMarkerTexture, "You");
        userMarker.align = OnlineMapsAlign.Center;
        userMarker.scale = 0.66f;
        userMarker.rotationDegree = lastRotation;
        map.Redraw();
    }

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        return angle;
    }




    public (double lat, double lon) GetCurrentLatLon() => (latitude, longitude);















}
