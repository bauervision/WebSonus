using UnityEngine;
using OnlineMaps;
using Sonus.Core;

public class SonusMapSceneController : MonoBehaviour
{
    public enum Mode
    {
        Map2D,
        Scene3D
    }

    [Header("Mode Roots")]
    public GameObject mapModeRoot;   // assign "Map Mode" GO here
    public GameObject sceneModeRoot; // assign "Scene Mode" GO here

    [Header("Defaults")]
    public double defaultLatitude = 37.305373;
    public double defaultLongitude = -80.611872;

    [Header("2D Map (Map Mode)")]
    [Tooltip("Online Maps Map component for the 2D (UI) map")]
    public Map map2D;

    [Tooltip("Camera that renders the 2D map UI")]
    public Camera mapCamera;

    public int zoom2D = 17;

    [Header("3D Map (Scene Mode)")]
    [Tooltip("Online Maps Map component for the 3D tileset")]
    public Map map3D;

    [Tooltip("The 3D control component attached to the 3D map (TileSetControl / similar)")]
    public ControlBase3D map3DControl;

    [Tooltip("Camera that renders the 3D scene / tileset")]
    public Camera sceneCamera;

    [Tooltip("Player root / FPS controller transform")]
    public Transform playerRoot;

    public int zoom3D = 16;
    public float playerSpawnHeight = 2f;

    [Header("Raycast")]
    [Tooltip("LayerMask for the tileset collider. Leave  ~0  to hit everything.")]
    public LayerMask tilesetLayerMask = ~0;

    private Mode _mode = Mode.Map2D;

    private void Awake()
    {
        // Try to auto-wire the 3D control if not set
        if (map3D != null && map3DControl == null)
        {
            map3DControl = map3D.control3D;
        }
    }

    private void Start()
    {
        // For this scene, always start at our configured default
        SonusLocationState.Set(defaultLatitude, defaultLongitude);

        if (map3DControl != null && sceneCamera != null)
        {
            map3DControl.activeCamera = sceneCamera;
        }

        double lat = SonusLocationState.Lat;
        double lng = SonusLocationState.Lng;

        Init2DMap(lat, lng);
        Init3DMap(lat, lng);

        SetMode(Mode.Map2D);
    }


    // ------------------------------------------------------
    // Initialization helpers
    // ------------------------------------------------------

    private void Init2DMap(double lat, double lng)
    {
        if (map2D == null) return;

        // v4 way: center via MapView
        map2D.view.SetCenter((float)lng, (float)lat, zoom2D);
        map2D.Redraw();
    }

    private void Init3DMap(double lat, double lng)
    {
        if (map3D == null) return;

        map3D.view.SetCenter((float)lng, (float)lat, zoom3D);
        map3D.Redraw();
    }

    // ------------------------------------------------------
    // Mode switching
    // ------------------------------------------------------

    public void ToggleMode()
    {
        SetMode(_mode == Mode.Map2D ? Mode.Scene3D : Mode.Map2D);
    }

    private void SetMode(Mode next)
    {
        _mode = next;
        bool mapMode = _mode == Mode.Map2D;

        if (mapModeRoot != null) mapModeRoot.SetActive(mapMode);
        if (sceneModeRoot != null) sceneModeRoot.SetActive(!mapMode);

        if (mapMode) EnterMapMode();
        else EnterSceneMode();
    }


    // ------------------------------------------------------
    // Enter Map Mode: center 2D on current shared geo
    // ------------------------------------------------------

    private void EnterMapMode()
    {
        if (map2D == null) return;

        double lat = SonusLocationState.Lat;
        double lng = SonusLocationState.Lng;

        map2D.view.SetCenter((float)lng, (float)lat, zoom2D);
        map2D.Redraw();
    }

    // ------------------------------------------------------
    // Enter Scene Mode:
    // 1) Center 3D map
    // 2) Teleport player onto tileset at shared lat/lon
    // ------------------------------------------------------

    private void EnterSceneMode()
    {
        if (map3D == null || map3DControl == null || playerRoot == null || sceneCamera == null) return;

        double lat = SonusLocationState.Lat;
        double lng = SonusLocationState.Lng;

        // Center 3D tileset map
        map3D.view.SetCenter((float)lng, (float)lat, zoom3D);
        map3D.Redraw();

        // Convert geo -> screen using the control
        Vector2 screenPos = map3DControl.LocationToScreen(lng, lat);

        // Raycast from scene camera through that screen point onto the tileset
        Ray ray = sceneCamera.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, 10000f, tilesetLayerMask))
        {
            Vector3 worldPos = hit.point;
            worldPos.y += playerSpawnHeight;
            playerRoot.position = worldPos;
        }
        else
        {
            // Fallback: just place near map3D origin if raycast misses
            Vector3 fallback = map3D.transform.position + Vector3.up * playerSpawnHeight;
            playerRoot.position = fallback;
        }
    }

    // ------------------------------------------------------
    // Sync player position -> shared lat/lon
    // ------------------------------------------------------

    private void LateUpdate()
    {
        if (_mode != Mode.Scene3D) return;
        if (map3DControl == null || map3D == null || playerRoot == null || sceneCamera == null) return;

        // Convert world -> screen
        Vector3 world = playerRoot.position;
        Vector3 screenPos3 = sceneCamera.WorldToScreenPoint(world);

        // Convert screen -> geo using 3D control's ScreenToLocation
        Vector2 screenPos = new Vector2(screenPos3.x, screenPos3.y);
        GeoPoint g = map3D.control.ScreenToLocation(screenPos);

        SonusLocationState.Set(g.latitude, g.longitude);
    }
}
