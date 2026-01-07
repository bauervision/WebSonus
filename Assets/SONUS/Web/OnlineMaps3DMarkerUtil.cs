// Assets/SONUS/Web/OnlineMaps3DMarkerUtil.cs
using System.Collections;
using UnityEngine;
using OnlineMaps;
using Sonus.Core;

public static class OnlineMaps3DMarkerUtil
{
    public static void Set3DEnabled(ref Marker3D marker3D, bool enabled, ref bool marker3DReady)
    {
        if (marker3D == null) return;

        try
        {
            if (marker3D.transform != null)
                marker3D.transform.gameObject.SetActive(enabled);

            // Only toggle enabled when turning ON (turning OFF via enabled can NRE in some lifecycles)
            if (enabled)
                marker3D.enabled = true;
        }
        catch
        {
            // OM may be mid-teardown; ignore and let next Enter3D resync.
        }

        if (!enabled) marker3DReady = false;
    }

    public static IEnumerator EnsureProbeMarker3D(GameObject targetPrefab, System.Action<string> warn, System.Func<bool> debugOn, System.Action<string> log, System.Func<Marker3D> get, System.Action<Marker3D> set)
    {
        if (get() != null) yield break;

        const float timeout = 10f;
        float t0 = Time.realtimeSinceStartup;

        while (Marker3DManager.instance == null && Time.realtimeSinceStartup - t0 < timeout)
            yield return null;

        if (Marker3DManager.instance == null)
        {
            warn?.Invoke("[TargetHunt] Marker3DManager.instance is null (probe).");
            yield break;
        }

        if (targetPrefab == null)
        {
            warn?.Invoke("[TargetHunt] targetPrefab is null (probe).");
            yield break;
        }

        var m = Marker3DManager.CreateItem(0, 0, targetPrefab, "target-3d");
        if (m == null)
        {
            warn?.Invoke("[TargetHunt] 3D CreateItem returned null (probe).");
            yield break;
        }

        m.sizeType = Marker3D.SizeType.scene;
        set(m);

        for (int i = 0; i < 6; i++) yield return null;
    }

    public static IEnumerator Ensure3DMarkerAndSync(
        TargetActor currentTarget,
        GameObject targetPrefab,
        System.Action<string> warn,
        System.Func<bool> debugOn,
        System.Action<string> log,
        System.Func<Marker3D> get,
        System.Action<Marker3D> set,
        System.Action<bool> setReady
    )
    {
        if (currentTarget == null) yield break;

        setReady(false);

        const float timeout = 10f;
        float t0 = Time.realtimeSinceStartup;

        while (Marker3DManager.instance == null && Time.realtimeSinceStartup - t0 < timeout)
            yield return null;

        if (Marker3DManager.instance == null)
        {
            warn?.Invoke("[TargetHunt] Marker3DManager.instance is null.");
            yield break;
        }

        var marker3D = get();

        if (marker3D == null)
        {
            if (targetPrefab == null)
            {
                warn?.Invoke("[TargetHunt] targetPrefab is null.");
                yield break;
            }

            marker3D = Marker3DManager.CreateItem(0, 0, targetPrefab, "target-3d");
            if (marker3D == null)
            {
                warn?.Invoke("[TargetHunt] 3D CreateItem returned null.");
                yield break;
            }

            marker3D.sizeType = Marker3D.SizeType.scene;
            set(marker3D);
        }

        if (marker3D.transform != null)
            marker3D.transform.gameObject.SetActive(true);

        marker3D.location = new GeoPoint(currentTarget._Lon, currentTarget._Lat);

        try { marker3D.enabled = true; } catch { }

        try
        {
            marker3D.Update();
        }
        catch
        {
            if (debugOn()) warn?.Invoke("[TargetHunt] Marker3D.Update threw during sync; will retry next Enter3D.");
            yield break;
        }

        for (int i = 0; i < 10; i++) yield return null;

        bool ready = (marker3D != null && marker3D.enabled && marker3D.transform != null);
        setReady(ready);

        if (debugOn() && marker3D != null && marker3D.transform != null)
            log?.Invoke($"[TargetHunt] 3D marker pos={marker3D.transform.position} target=({currentTarget._Lat:F6},{currentTarget._Lon:F6})");
    }
}
