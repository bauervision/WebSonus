using UnityEngine;

public class PlayerLocator : MonoBehaviour
{
    public static PlayerLocator instance { get; private set; }
    [Header("Scene References")]
    public Camera SceneCam;
    public Texture2D userMarkerTexture;

    [Header("User Starting Position")]
    public double latitude = 43.83194;
    public double longitude = 11.3136;

    [Header("Mouse Look Settings")]
    public float mouseSensitivity = 3f;
    public float smoothing = 6f;
    public float minPitch = -80f;
    public float maxPitch = 80f;

    private OnlineMaps map;
    private OnlineMapsMarker userMarker;

    private Vector2 targetRotation;
    private Vector2 smoothedRotation;

    private bool mapCentered = false;

    private bool shouldApplySyncedRotation = false;


    private void Awake()
    {
        instance = this;
    }
    private void Start()
    {
        map = OnlineMaps.instance;

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
        map.position = new Vector2((float)longitude, (float)latitude);
        map.zoom = 17;
        map.Redraw();
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

    public void SyncCameraToMarker()
    {
        if (userMarker == null || SceneCam == null) return;

        float yaw = NormalizeAngle(userMarker.rotationDegree);

        currentRotation.x = yaw;

        // Sync to camera orbit controller instead
        var orbit = SceneCam.GetComponent<CameraOrbitController>();
        if (orbit != null) orbit.SetRotation(yaw);

        shouldApplySyncedRotation = false; // no longer needed
    }

    public void SyncMarkerToCamera()
    {
        if (userMarker == null) return;

        float camYaw = NormalizeAngle(SceneCam.transform.eulerAngles.y);
        currentRotation.x = camYaw;
        userMarker.rotationDegree = camYaw;


    }

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        return angle;
    }




}
