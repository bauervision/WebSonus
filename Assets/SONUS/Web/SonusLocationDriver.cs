// Assets/SONUS/Web/SonusLocationDriver.cs
using UnityEngine;
using Sonus.Core;

public class SonusLocationDriver : MonoBehaviour
{
    [Tooltip("Assign the mapper wired to the 3D map/control.")]
    public OLMGeoMapper geoMapper;

    [Tooltip("Scene controller that owns mode switching.")]
    public SonusMapSceneController scene;

    [Header("Debug")]
    public bool debugLogs;

    private void Awake()
    {
#if UNITY_2023_1_OR_NEWER
        if (scene == null) scene = FindFirstObjectByType<SonusMapSceneController>();
        if (geoMapper == null) geoMapper = FindFirstObjectByType<OLMGeoMapper>();
#else
        if (scene == null) scene = FindObjectOfType<SonusMapSceneController>();
        if (geoMapper == null) geoMapper = FindObjectOfType<OLMGeoMapper>();
#endif
    }

    private void Update()
    {
        if (scene == null || geoMapper == null) return;

        // Only drive from 3D when we're actually in 3D.
        if (!scene.IsIn2DMode)
        {
            if (geoMapper.TryFeetScreenToLatLon(out double lat, out double lon))
            {
                SonusLocationState.Set(lat, lon);

                if (debugLogs)
                    Debug.Log($"[Loc] 3D feet sample lat/lon=({lat:F6},{lon:F6})");
            }
        }
        // In 2D: your existing GPS / 2D input pipeline should set SonusLocationState.
    }
}
