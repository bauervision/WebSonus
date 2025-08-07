using System.Collections.Generic;
using UnityEngine;

public class TargetSceneManager : MonoBehaviour
{
    public static TargetSceneManager Instance { get; private set; }

    public List<TargetActor> ActiveTargets = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        Instance = this;
    }

    void Update()
    {
        if (TargetHUDManager.instance == null) return;

        TargetHUDManager.instance.ClearGroupingCache();
        foreach (TargetActor target in ActiveTargets)
            TargetHUDManager.instance.UpdateTargetUI(target);
    }

    public void RegisterTarget(TargetActor target)
    {
        if (!ActiveTargets.Exists(t => t._ID == target._ID))
            ActiveTargets.Add(target);
    }

    public TargetActor GetTargetById(string id)
    {
        return ActiveTargets.Find(t => t._ID == id);
    }

    public void ClearAllTargets()
    {
        ActiveTargets.Clear();

    }

    public TargetActor SpawnTarget(Vector2 latLon, TargetType type)
    {
        double lat = latLon.x;
        double lon = latLon.y;

        float alt = OnlineMapsElevationManagerBase.GetUnscaledElevationByCoordinate(lon, lat);

        TargetActor newTarget = new TargetActor(type, lat, lon)
        {
            _ID = System.Guid.NewGuid().ToString(),
            _Alt = alt
        };

        // Create marker
        Texture2D icon = AddTargetOnClick.GetIconForType(type);
        var marker = OnlineMapsMarkerManager.CreateItem(lon, lat, icon);
        marker.label = $"Target: {type}";
        marker.align = OnlineMapsAlign.Center;
        marker["data"] = newTarget;
        marker.OnClick += AddTargetOnClick.OnTargetClick;
        marker.scale = 0.4f;

        //AddTargetOnClick.selectedMarker = marker;

        // Register internally
        RegisterTarget(newTarget);

        return newTarget;
    }


}
