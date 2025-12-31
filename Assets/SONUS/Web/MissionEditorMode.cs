// Assets/SONUS/Web/MissionEditorMode.cs
using System.Collections.Generic;
using OnlineMaps;
using UnityEngine;

public class MissionEditorMode : MonoBehaviour
{
    [Header("Mode")]
    public bool editorMode = false;

    [Header("Refs")]
    public Map map; // assign your 2D map here

    [Header("Output")]
    public bool logToConsole = true;

    [Tooltip("Also keep a runtime list (not persisted). Useful for copy/paste from inspector during play.")]
    public bool keepRuntimeList = true;

    [SerializeField] private List<GeoPoint> runtimePoints = new();

    private bool _subscribed;

    private void Start()
    {
        if (!map && !(map = Map.instance))
        {
            Debug.LogError("[MissionEditor] Map not found");
            return;
        }

        SubscribeIfNeeded();
    }

    private void OnEnable() => SubscribeIfNeeded();

    private void OnDisable() => UnsubscribeIfNeeded();

    private void SubscribeIfNeeded()
    {
        if (_subscribed) return;
        if (map == null || map.control == null) return;

        map.control.OnClick += OnMapClick;
        _subscribed = true;

        Debug.Log("[MissionEditor] Subscribed to map.control.OnClick");
    }

    private void UnsubscribeIfNeeded()
    {
        if (!_subscribed) return;
        if (map == null || map.control == null) { _subscribed = false; return; }

        map.control.OnClick -= OnMapClick;
        _subscribed = false;
    }

    private void OnMapClick()
    {
        if (!editorMode) return;
        if (map == null || map.control == null) return;

        GeoPoint p = map.control.ScreenToLocation();

        // GeoPoint: x=lon, y=lat in Online Maps
        double lon = p.x;
        double lat = p.y;

        if (keepRuntimeList) runtimePoints.Add(p);

        if (logToConsole)
        {
            Debug.Log($"[MissionEditor] new PresetGeoPoint({lat:F6}, {lon:F6}),");
        }
    }

    // Optional: helper to clear during play
    [ContextMenu("Clear runtime points")]
    private void ClearRuntimePoints() => runtimePoints.Clear();
}
