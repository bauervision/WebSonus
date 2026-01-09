// Assets/SONUS/Web/Debug/HeadingCalibrator.cs
using UnityEngine;

public class HeadingCalibrator : MonoBehaviour
{
    public SonusMapSceneController scene;
    public TargetManager targetManager;
    public Transform headingSource; // playerRoot

    [Tooltip("Press this key while you are looking directly at the target in 3D.")]
    public KeyCode sampleKey = KeyCode.C;

    void Awake()
    {
        if (scene == null) scene = FindFirstObjectByType<SonusMapSceneController>();
        if (targetManager == null) targetManager = FindFirstObjectByType<TargetManager>();
        if (headingSource == null && scene != null) headingSource = scene.playerRoot;
    }

    void Update()
    {
        if (!Input.GetKeyDown(sampleKey)) return;
        if (headingSource == null || targetManager == null || targetManager.currentTarget == null)
        {
            Debug.Log("[HeadingCal] Missing refs or currentTarget.");
            return;
        }

        // player geo (prefer SonusPlayerGeoState if you have it, otherwise SonusLocationState)
        if (!Sonus.Core.SonusLocationState.HasValue)
        {
            Debug.Log("[HeadingCal] SonusLocationState has no value.");
            return;
        }

        double lat0 = Sonus.Core.SonusLocationState.Lat;
        double lon0 = Sonus.Core.SonusLocationState.Lng;

        double lat1 = targetManager.currentTarget._Lat;
        double lon1 = targetManager.currentTarget._Lon;

        float yaw = Normalize360(headingSource.eulerAngles.y);
        float bearing = BearingDeg(lat0, lon0, lat1, lon1); // 0=N, 90=E

        // Candidate mappings:
        // A) heading = yaw + K
        float kA = Normalize360(bearing - yaw);
        float errA = Mathf.Abs(Mathf.DeltaAngle(Normalize360(yaw + kA), bearing));

        // B) heading = -yaw + K
        float kB = Normalize360(bearing - Normalize360(-yaw));
        float errB = Mathf.Abs(Mathf.DeltaAngle(Normalize360(-yaw + kB), bearing));

        Debug.Log(
            $"[HeadingCal] SAMPLE yaw={yaw:F1} bearingToTarget={bearing:F1} | " +
            $"A: heading=yaw+K -> K={kA:F1} err={errA:F2} | " +
            $"B: heading=-yaw+K -> K={kB:F1} err={errB:F2}"
        );
    }

    static float Normalize360(float deg)
    {
        deg %= 360f;
        if (deg < 0f) deg += 360f;
        return deg;
    }

    // Initial bearing (great-circle), in degrees 0..360 (0=N)
    static float BearingDeg(double lat0, double lon0, double lat1, double lon1)
    {
        double phi1 = lat0 * Mathf.Deg2Rad;
        double phi2 = lat1 * Mathf.Deg2Rad;
        double dLam = (lon1 - lon0) * Mathf.Deg2Rad;

        double y = System.Math.Sin(dLam) * System.Math.Cos(phi2);
        double x = System.Math.Cos(phi1) * System.Math.Sin(phi2) -
                   System.Math.Sin(phi1) * System.Math.Cos(phi2) * System.Math.Cos(dLam);

        double brng = System.Math.Atan2(y, x) * Mathf.Rad2Deg;
        if (brng < 0) brng += 360.0;
        return (float)brng;
    }
}
