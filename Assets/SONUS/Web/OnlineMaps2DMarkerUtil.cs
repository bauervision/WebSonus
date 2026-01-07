// Assets/SONUS/Web/OnlineMaps2DMarkerUtil.cs
using System.Collections;
using System.Reflection;
using UnityEngine;
using OnlineMaps;
using Sonus.Core;

public static class OnlineMaps2DMarkerUtil
{
    public static bool Is2DReady(Marker2DManager markerManager2D)
    {
        if (markerManager2D == null) return false;
        if (!markerManager2D.gameObject.activeInHierarchy) return false;

        var map = markerManager2D.map;
        if (map == null) return false;
        if (!map.gameObject.activeInHierarchy) return false;

        var ctrl = map.control;
        if (ctrl == null) return false;
        if (!ctrl.enabled) return false;
        if (!ctrl.gameObject.activeInHierarchy) return false;

        return true;
    }

    public static void Ensure2DManagerSingleton(Marker2DManager markerManager2D)
    {
        if (markerManager2D == null) return;

        var t = typeof(Marker2DManager);
        var f = t.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        f?.SetValue(null, markerManager2D);
    }

    public static void Update2DMarkerLocationSafe(
        ref Marker2D marker2D,
        bool in2DMode,
        Marker2DManager markerManager2D,
        double lon,
        double lat,
        float map2DRedrawHz,
        ref float next2DRedrawTime
    )
    {
        if (marker2D == null) return;
        if (!in2DMode) return;
        if (!Is2DReady(markerManager2D)) return;

        Ensure2DManagerSingleton(markerManager2D);

        marker2D.location = new GeoPoint(lon, lat);

        if (markerManager2D != null && markerManager2D.map != null && Time.time >= next2DRedrawTime)
        {
            next2DRedrawTime = Time.time + (1f / Mathf.Max(1f, map2DRedrawHz));
            markerManager2D.map.Redraw();
        }
    }

    public static void SafeRemove2DMarker(ref Marker2D marker2D, Marker2DManager markerManager2D)
    {
        if (marker2D == null) return;

        try
        {
            Ensure2DManagerSingleton(markerManager2D);
            Marker2DManager.RemoveItem(marker2D);
        }
        catch
        {
            try { marker2D.enabled = false; } catch { }
        }

        marker2D = null;
    }

    public static IEnumerator Recreate2DMarkerWhenReady(
        System.Func<bool> isStillIn2DMode,
        Marker2DManager markerManager2D,
        Texture2D targetMarkerTexture,
        float targetMarkerScale,
        TargetActor currentTarget,
        System.Action<Marker2D> setMarker2D,
        System.Func<Marker2D> getMarker2D,
        bool debugLogs
    )
    {
        if (!isStillIn2DMode()) yield break;

        // OnlineMaps often needs EndOfFrame to initialize marker buffers after re-activation
        yield return null;
        yield return new WaitForEndOfFrame();
        yield return null;
        yield return new WaitForEndOfFrame();

        float timeout = 3.0f;
        float t0 = Time.realtimeSinceStartup;

        while (isStillIn2DMode() && !Is2DReady(markerManager2D) && (Time.realtimeSinceStartup - t0) < timeout)
            yield return null;

        if (!isStillIn2DMode()) yield break;

        if (!Is2DReady(markerManager2D))
        {
            if (debugLogs) Debug.LogWarning("[TargetHunt] 2D not ready; skipping marker create.");
            yield break;
        }

        if (targetMarkerTexture == null || currentTarget == null)
        {
            if (debugLogs) Debug.LogWarning("[TargetHunt] Missing texture or target; cannot create 2D marker.");
            yield break;
        }

        Ensure2DManagerSingleton(markerManager2D);

        // Remove prior marker
        var m = getMarker2D();
        SafeRemove2DMarker(ref m, markerManager2D);
        setMarker2D(m); // will now be null

        try
        {
            Marker2D created = Marker2DManager.CreateItem(
                currentTarget._Lon,
                currentTarget._Lat,
                targetMarkerTexture,
                "target"
            );

            if (created == null)
            {
                Debug.LogWarning("[TargetHunt] 2D CreateItem returned null.");
                yield break;
            }

            created.align = Align.Center;
            created.scale = targetMarkerScale;
            created.enabled = true;
            created["data"] = currentTarget;

            setMarker2D(created);

            markerManager2D.map?.Redraw();

            if (debugLogs)
                Debug.Log($"[TargetHunt] 2D marker created @ ({currentTarget._Lat:F6},{currentTarget._Lon:F6})");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[TargetHunt] 2D CreateItem exception: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
