using UnityEngine;

public class PlayerLocator : MonoBehaviour
{
    public static PlayerLocator instance { get; private set; }



    [Header("3D Tileset")]
    public OnlineMaps map;
    public OnlineMaps map3d;
    public OnlineMapsTileSetControl tileset3D;
    public float cameraEyeHeight = 1.7f; // eye level above terrain

    [Header("Scene References")]
    public GameObject SceneCam;
    public Texture2D userMarkerTexture;

    [Header("User Starting Position")]
    public double latitude = 37.306050;
    public double longitude = 80.611921;

    [Header("Mouse Look Settings")]
    public float mouseSensitivity = 3f;
    public float smoothing = 6f;
    public float minPitch = -80f;
    public float maxPitch = 80f;


    private OnlineMapsMarker userMarker;

    private Vector2 targetRotation;
    private Vector2 smoothedRotation;

    private bool mapCentered = false;

    private bool shouldApplySyncedRotation = false;

    int zoom3dLevel = 19;
    private void Awake()
    {
        instance = this;
    }
    private void Start()
    {


        // Place the camera at the world position of our start (lon, lat) on the 3D tileset
        if (tileset3D != null && SceneCam != null)
        {

            Vector3 world = tileset3D.GetWorldPositionWithElevation(longitude, latitude);
            world.y += cameraEyeHeight; // keep your eye height lift
            SceneCam.transform.position = world;

            // Initialize our rotation cache from the camera (FPSController will now drive look)
            currentRotation.x = NormalizeAngle(SceneCam.transform.eulerAngles.y);
            currentRotation.y = Mathf.Clamp(NormalizeAngle(SceneCam.transform.eulerAngles.x), minPitch, maxPitch);
        }
        else
        {
            Debug.LogWarning("PlayerLocator: tileset or SceneCam missing; cannot place camera at start.");
        }

        // Create user marker (lon, lat)
        userMarker = OnlineMapsMarkerManager.CreateItem(
            longitude, latitude, userMarkerTexture, "You"
        );
        userMarker.align = OnlineMapsAlign.Center;
        userMarker.scale = 0.66f;
        userMarker.rotationDegree = 0f;

        // Initial camera rotation sync
        targetRotation.x = SceneCam.transform.eulerAngles.y;
        targetRotation.y = SceneCam.transform.eulerAngles.x;
        smoothedRotation = targetRotation;

        // Set map position now, and again in LateUpdate (to ensure full center)
        map.SetPositionAndZoom(longitude, latitude, 17);
        map.Redraw();
        map3d.SetPositionAndZoom(longitude, latitude, zoom3dLevel);
        map3d.Redraw();

        // hide terrain now that it is set
        //map3d.gameObject.SetActive(false);
    }

    private Vector2 currentRotation;

    void Update()
    {
        // Apply camera rotation once after sync
        if (shouldApplySyncedRotation)
        {
            SceneCam.transform.rotation = Quaternion.Euler(currentRotation.y, currentRotation.x, 0f);
            shouldApplySyncedRotation = false;
            return; // Skip rest of update
        }

        // Rotate on right mouse drag
        if (Input.GetMouseButton(1))
        {
            currentRotation.x += Input.GetAxis("Mouse X") * mouseSensitivity;
            currentRotation.y -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            currentRotation.x = Mathf.Repeat(currentRotation.x, 360);
            currentRotation.y = Mathf.Clamp(currentRotation.y, minPitch, maxPitch);

            SceneCam.transform.rotation = Quaternion.Euler(currentRotation.y, currentRotation.x, 0f);
            SetUserMarkerRotation(currentRotation.x);
        }
    }


    public float unitsPerMeter = 1f;

    // Measure how many Unity units equal 1 meter at the current tileset size
    public void CalibrateUnitsPerMeter()
    {
        if (tileset3D == null) return;

        const double dMeters = 10.0;                  // small sample span
        double dLat = dMeters / 111111.0;            // ~1m per 1/111,111 degree latitude

        // Use either GetWorldPosition(...) or WithElevation(...). Both are fine; we'll ignore Y.
        Vector3 a = tileset3D.GetWorldPosition(longitude, latitude);
        Vector3 b = tileset3D.GetWorldPosition(longitude, latitude + dLat);

        float du = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
        unitsPerMeter = du / (float)dMeters;
    }

    // Apply real-world m/s to your controller using the calibrated scale
    public void ApplyMovementCalibration(FirstPersonController fpc, float walkMps = 1.6f, float sprintMps = 3.5f)
    {
        if (fpc == null) return;
        fpc.walkSpeed = walkMps * unitsPerMeter;   // e.g., 1.6 m/s walk
        fpc.sprintSpeed = sprintMps * unitsPerMeter;   // e.g., 3.5 m/s jog/run
    }



    private void LateUpdate()
    {
        // Force center after map has rendered (only once)
        if (!mapCentered)
        {
            map.position = new Vector2((float)longitude, (float)latitude);
            map.Redraw();
            mapCentered = true;
        }
    }

    public Vector2 GetCurrentLocation()
    {
        return new Vector2((float)latitude, (float)longitude);
    }

    public void RestoreUserMarker()
    {
        float lastRotation = currentRotation.x;

        if (userMarker != null)
        {
            OnlineMapsMarkerManager.RemoveItem(userMarker);
        }

        userMarker = OnlineMapsMarkerManager.CreateItem(
            longitude, latitude, userMarkerTexture, "You"
        );
        userMarker.align = OnlineMapsAlign.Center;
        userMarker.scale = 0.66f;
        userMarker.rotationDegree = lastRotation;
    }


    private void SetUserMarkerRotation(float rotation)
    {
        if (userMarker == null ||
            OnlineMaps.instance == null ||
            !OnlineMaps.instance.gameObject.activeInHierarchy ||
            OnlineMaps.instance.control == null)
        {
            return;
        }

        userMarker.rotationDegree = rotation;
    }



    public void SyncMarkerToCamera()
    {
        if (userMarker == null || SceneCam == null) return;
        float camYaw = NormalizeAngle(SceneCam.transform.eulerAngles.y);
        userMarker.rotationDegree = camYaw;
    }

    public void SyncCameraToMarker()
    {
        if (userMarker == null || SceneCam == null) return;

        float yaw = NormalizeAngle(userMarker.rotationDegree);

        // Prefer FPSController if present so its internal yaw stays in sync
        var fps = SceneCam.GetComponent<FPSController>();
        if (fps != null)
        {
            fps.SnapYaw(yaw);
        }
        else
        {
            // Fallback: directly rotate the camera’s parent object
            SceneCam.transform.parent?.Rotate(0f, yaw - SceneCam.transform.eulerAngles.y, 0f, Space.World);
        }
    }


    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        return angle;
    }

    public void CenterMapAndPlaceCamera()
    {
        if (map != null)
        {
            map.SetPositionAndZoom(longitude, latitude, 17);
            map.Redraw();
        }

        if (map3d != null) // keep active!
        {
            map3d.SetPositionAndZoom(longitude, latitude, zoom3dLevel);
            map3d.Redraw();
        }

        if (tileset3D != null && SceneCam != null)
        {
            Vector3 world = tileset3D.GetWorldPositionWithElevation(longitude, latitude);
            world.y += cameraEyeHeight;
            SceneCam.transform.position = world;
        }

        SyncCameraToMarker();
        SyncMarkerToCamera();
    }


    public bool HasGroundUnderCamera(float maxDistance = 200f)  // ⬅ bump to 200f
    {
        if (SceneCam == null) return false;
        return Physics.Raycast(
            SceneCam.transform.position + Vector3.up * 0.1f,
            Vector3.down,
            maxDistance
        );
    }

    public void SnapCameraToGroundOnce()
    {
        if (SceneCam == null) return;
        if (Physics.Raycast(SceneCam.transform.position + Vector3.up * 2f, Vector3.down, out var hit, 200f))
        {
            var p = SceneCam.transform.position;
            p.y = hit.point.y + cameraEyeHeight;
            SceneCam.transform.position = p;
        }
    }


}
