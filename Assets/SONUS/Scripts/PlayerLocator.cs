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
        // Rotate on right mouse drag
        if (Input.GetMouseButton(1))
        {
            currentRotation.x += Input.GetAxis("Mouse X") * mouseSensitivity;
            currentRotation.y -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            currentRotation.x = Mathf.Repeat(currentRotation.x, 360);
            currentRotation.y = Mathf.Clamp(currentRotation.y, minPitch, maxPitch);
        }

        // Apply rotation to SceneCam
        SceneCam.transform.rotation = Quaternion.Euler(currentRotation.y, currentRotation.x, 0f);

        // Set marker rotation based on same X value (clamped)
        if (userMarker != null)
        {
            userMarker.rotationDegree = currentRotation.x;
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
}
