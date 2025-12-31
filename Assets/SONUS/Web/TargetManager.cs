// Assets/SONUS/Web/TargetManager.cs
using System.Collections;
using System.Reflection;
using UnityEngine;
using OnlineMaps;
using Sonus.Core;

[System.Serializable]
public struct PresetGeoPoint
{
    public double lat;
    public double lon;

    public PresetGeoPoint(double lat, double lon)
    {
        this.lat = lat;
        this.lon = lon;
    }

    public override string ToString() => $"({lat:F6},{lon:F6})";
}

public class TargetManager : MonoBehaviour
{
    [Header("Refs (from SonusMapSceneController)")]
    public SonusMapSceneController sceneController;
    public Transform playerRoot;

    [Header("2D Wiring (assign in Inspector)")]
    [Tooltip("Drag the Marker2DManager that belongs to the 2D map (Map Mode).")]
    public Marker2DManager markerManager2D;

    public Texture2D targetMarkerTexture;
    public float targetMarkerScale = 1f;

    [Header("3D Wiring")]
    [Tooltip("Prefab used by OnlineMaps Marker3D. Keep it small/simple.")]
    public GameObject targetPrefab;

    public float targetExtraYOffset = 0.2f;

    [Header("Spawn")]
    public float spawnDistanceMeters = 80f;
    public float spawnJitterMeters = 10f;


    [Header("Preset Targets")]
    public bool usePresetTargets = true;

    public PresetGeoPoint[] presetTargets = new PresetGeoPoint[]
    {
    new(37.305458, -80.612394),
    new(37.306329, -80.610859),
    new (37.306515, -80.613209),
    new (37.303756, -80.612456),
    new (37.304914, -80.611766),
    new (37.304215, -80.610174),
    new (37.306193, -80.609963),
    new (37.305381, -80.610454),
    new (37.307287, -80.610910),
    new (37.306878, -80.614156),
    new (37.305340, -80.614054),
    new (37.306148, -80.608554),
    };


    private int[] _presetBag;
    private int _presetBagIndex;

    [Header("Debug")]
    public bool debugLogs = true;

    // ✅ Do NOT let Unity serialize a default TargetActor (0,0) into the component
    [System.NonSerialized] public TargetActor currentTarget;

    private Marker2D _marker2D;
    private Marker3D _marker3D;

    private Coroutine _sync3DRoutine;
    private bool _isRespawning;

    private void Awake()
    {
        if (sceneController == null) sceneController = FindAny<SonusMapSceneController>();

        // ✅ Kill any inspector-serialized ghost target
        currentTarget = null;
    }

    private void Update()
    {
        if (_isRespawning) return;
        if (playerRoot == null) return;

        if (_marker3D == null || !_marker3D.enabled || _marker3D.transform == null) return;

        Vector3 p = _marker3D.transform.position;
        if (p == Vector3.zero) return;

        p = new Vector3(p.x, p.y + targetExtraYOffset, p.z);

        float d = Vector3.Distance(playerRoot.position, p);

    }


    // ---------------------------
    // Mode hooks
    // ---------------------------

    public void OnEnter2D(Map map2D)
    {
        Ensure2DManagerSingleton();

        EnsureTarget(map2D);
        Recreate2DMarker();

        Set3DEnabled(false);
        map2D?.Redraw();

        if (debugLogs && map2D != null)
        {
            var c = map2D.view.center;
            Debug.Log($"[TargetHunt] Enter2D center=({c.y:F6},{c.x:F6}) target=({currentTarget._Lat:F6},{currentTarget._Lon:F6})");
        }
    }

    public void OnEnter3D()
    {
        if (debugLogs) Debug.Log("[TargetHunt] Enter3D");

        EnsureTarget(sceneController != null ? sceneController.map2D : null);

        if (_sync3DRoutine != null) StopCoroutine(_sync3DRoutine);
        _sync3DRoutine = StartCoroutine(Ensure3DMarkerAndSync());
    }

    // ---------------------------
    // Target
    // ---------------------------

    private void EnsureTarget(Map map2D)
    {
        if (HasValidTarget()) return;

        // Base from state / center / defaults
        double baseLat = SonusLocationState.Lat;
        double baseLon = SonusLocationState.Lng;

        if ((baseLat == 0 && baseLon == 0) && map2D != null)
        {
            baseLon = map2D.view.center.x;
            baseLat = map2D.view.center.y;
        }

        if ((baseLat == 0 && baseLon == 0) && sceneController != null)
        {
            baseLat = sceneController.defaultLatitude;
            baseLon = sceneController.defaultLongitude;
        }

        double tLat, tLon;

        if (TryPickNextPreset(out tLat, out tLon))
        {
            if (debugLogs)
                Debug.Log($"[TargetHunt] Using preset target=({tLat:F6},{tLon:F6})");
        }
        else
        {
            // Fallback: deterministic offset so we ALWAYS get a visible target nearby.
            tLat = baseLat + 0.001;
            tLon = baseLon + 0.001;

            if (debugLogs)
                Debug.Log($"[TargetHunt] Using fallback target=({tLat:F6},{tLon:F6}) from base=({baseLat:F6},{baseLon:F6})");
        }



        currentTarget = new TargetActor(TargetType.STATIONARY, tLat, tLon)
        {
            _Name = "Target"
        };

        if (debugLogs)
            Debug.Log($"[TargetHunt] Seed target=({currentTarget._Lat:F6},{currentTarget._Lon:F6}) from base=({baseLat:F6},{baseLon:F6})");
    }

    private bool HasValidTarget()
    {
        if (currentTarget == null) return false;
        return !(System.Math.Abs(currentTarget._Lat) < 1e-9 && System.Math.Abs(currentTarget._Lon) < 1e-9);
    }

    // ---------------------------
    // 2D marker (create-only)
    // ---------------------------

    private void Recreate2DMarker()
    {
        if (targetMarkerTexture == null || currentTarget == null) return;

        // ✅ Only create 2D markers when the 2D map stack is actually active/ready.
        if (!Is2DReady())
        {
            if (debugLogs)
                Debug.Log("[TargetHunt] Skip 2D marker recreate: 2D map not active/ready.");
            return;
        }

        Ensure2DManagerSingleton();

        SafeRemove2DMarker();

        _marker2D = Marker2DManager.CreateItem(currentTarget._Lon, currentTarget._Lat, targetMarkerTexture, "target");
        if (_marker2D == null)
        {
            Debug.LogWarning("[TargetHunt] 2D CreateItem returned null.");
            return;
        }

        _marker2D.align = Align.Center;
        _marker2D.scale = targetMarkerScale;
        _marker2D.enabled = true;
        _marker2D["data"] = currentTarget;
    }


    private bool Is2DReady()
    {
        if (markerManager2D == null) return false;
        if (!markerManager2D.gameObject.activeInHierarchy) return false;

        // OnlineMaps 2D manager must have a map reference and it must be active
        if (markerManager2D.map == null) return false;
        if (!markerManager2D.map.gameObject.activeInHierarchy) return false;

        return true;
    }



    private void SafeRemove2DMarker()
    {
        if (_marker2D == null) return;

        Ensure2DManagerSingleton();

        try { Marker2DManager.RemoveItem(_marker2D); }
        catch { try { _marker2D.enabled = false; } catch { } }

        _marker2D = null;
    }

    // ---------------------------
    // 3D marker
    // ---------------------------

    private IEnumerator Ensure3DMarkerAndSync()
    {
        if (currentTarget == null) yield break;

        const float timeout = 10f;
        float t0 = Time.realtimeSinceStartup;

        while (Marker3DManager.instance == null && Time.realtimeSinceStartup - t0 < timeout)
            yield return null;

        if (Marker3DManager.instance == null)
        {
            Debug.LogWarning("[TargetHunt] Marker3DManager.instance is null.");
            yield break;
        }

        if (_marker3D == null)
        {
            if (targetPrefab == null)
            {
                Debug.LogWarning("[TargetHunt] targetPrefab is null.");
                yield break;
            }

            _marker3D = Marker3DManager.CreateItem(0, 0, targetPrefab, "target-3d");
            if (_marker3D == null)
            {
                Debug.LogWarning("[TargetHunt] 3D CreateItem returned null.");
                yield break;
            }

            _marker3D.sizeType = Marker3D.SizeType.scene;
        }

        _marker3D.location = new GeoPoint(currentTarget._Lon, currentTarget._Lat);
        _marker3D.enabled = true;
        _marker3D.Update();

        // Let OM resolve transform
        for (int i = 0; i < 10; i++) yield return null;

        if (debugLogs && _marker3D.transform != null)
            Debug.Log($"[TargetHunt] 3D marker pos={_marker3D.transform.position} target=({currentTarget._Lat:F6},{currentTarget._Lon:F6})");
    }

    private void Set3DEnabled(bool enabled)
    {
        if (_marker3D == null) return;
        _marker3D.enabled = enabled;
    }

    // ---------------------------
    // Singleton forcing (2D)
    // ---------------------------

    private void Ensure2DManagerSingleton()
    {
        if (!Is2DReady()) return;

        var t = typeof(Marker2DManager);
        var f = t.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        f?.SetValue(null, markerManager2D);
    }


    // ---------------------------
    // Respawn (simple)
    // ---------------------------

    private IEnumerator RespawnFlow()
    {
        Set3DEnabled(false);

        currentTarget = null;
        EnsureTarget(sceneController != null ? sceneController.map2D : null);

        // ✅ Only when 2D is active (Map Mode)
        Recreate2DMarker();

        yield return StartCoroutine(Ensure3DMarkerAndSync());

        _isRespawning = false;
    }





    public bool TryGetTargetWorldPos(out Vector3 pos)
    {
        pos = default;
        if (_marker3D == null || !_marker3D.enabled || _marker3D.transform == null) return false;

        pos = _marker3D.transform.position;
        return pos != Vector3.zero;
    }

    public void RequestRespawn()
    {
        if (_isRespawning) return;
        _isRespawning = true;
        StartCoroutine(RespawnFlow());
    }




    private void ResetPresetBag()
    {
        if (presetTargets == null || presetTargets.Length == 0)
        {
            _presetBag = null;
            _presetBagIndex = 0;
            return;
        }

        _presetBag = new int[presetTargets.Length];
        for (int i = 0; i < _presetBag.Length; i++) _presetBag[i] = i;

        // Fisher–Yates shuffle
        for (int i = _presetBag.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_presetBag[i], _presetBag[j]) = (_presetBag[j], _presetBag[i]);
        }

        _presetBagIndex = 0;

        if (debugLogs)
            Debug.Log($"[TargetHunt] Preset bag reset ({presetTargets.Length} targets).");
    }

    private bool TryPickNextPreset(out double tLat, out double tLon)
    {
        tLat = tLon = 0;

        if (!usePresetTargets) return false;
        if (presetTargets == null || presetTargets.Length == 0) return false;

        if (_presetBag == null || _presetBag.Length != presetTargets.Length) ResetPresetBag();
        if (_presetBag == null || _presetBag.Length == 0) return false;

        if (_presetBagIndex >= _presetBag.Length) ResetPresetBag();

        int idx = _presetBag[_presetBagIndex++];
        var p = presetTargets[idx];

        tLat = p.lat;
        tLon = p.lon;

        return true;
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
