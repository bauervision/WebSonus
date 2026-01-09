using UnityEngine;
using Sonus.Core;

public class SonusLocationDriver : MonoBehaviour
{
    [Tooltip("Assign the mapper wired to the 3D map/control.")]
    public OLMGeoMapper geoMapper;

    [Tooltip("Scene controller that owns mode switching.")]
    public SonusMapSceneController scene;

    [Header("Heading Source")]
    [Tooltip("If null, we'll use scene.playerRoot.")]
    public Transform headingSource;

    [Tooltip("Apply a constant offset to heading. If map is flipped north/south, set to 180.")]
    public float headingOffsetDeg = 0f;

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
        if (headingSource == null && scene != null) headingSource = scene.playerRoot;
    }

    private void Update()
    {
        if (scene == null || geoMapper == null) return;

        // Only drive from 3D when we're actually in 3D.
        if (!scene.IsIn2DMode)
        {
            bool ok = geoMapper.TryFeetScreenToLatLon(out double lat, out double lon);

            // Heading is independent of geo sampling; compute whenever we can.
            double heading = double.NaN;
            var src = headingSource != null ? headingSource : scene.playerRoot;
            if (src != null)
            {
                Vector3 fwd = src.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f)
                {
                    fwd.Normalize();

                    // Compass heading: 0=N, 90=E
                    float raw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
                    float h = raw % 360f;
                    if (h < 0f) h += 360f;
                    heading = h;
                }
            }

            if (ok)
            {
                if (double.IsNaN(heading))
                    SonusPlayerGeoState.Set(lat, lon);
                else
                    SonusPlayerGeoState.Set(lat, lon, heading);

                if (debugLogs)
                    Debug.Log($"[Loc] 3D feet lat/lon=({lat:F6},{lon:F6}) heading={heading:F1}");
            }
            else
            {
                if (!double.IsNaN(heading))
                    SonusPlayerGeoState.SetHeading(heading);
            }

        }
        // In 2D: your existing GPS / 2D input pipeline should set SonusLocationState.
    }
}
