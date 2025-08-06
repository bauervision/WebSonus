using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;

public class AddTargetOnClick : MonoBehaviour
{
    public Texture2D stationaryIcon;
    public Texture2D dynamicIcon;

    public static Text targetTypeText;
    public static Text targetUpdateText;
    public static Text targetLatText;
    public static Text targetLonText;

    private static TargetActor currentTarget;
    public static OnlineMapsMarker selectedMarker = null;

    private double lng, lat;

    private void Awake()
    {
        // targetTypeText = GameObject.Find("TargetTypeText").GetComponent<Text>();
        // targetUpdateText = GameObject.Find("TargetUpdateText").GetComponent<Text>();
        // targetLatText = GameObject.Find("TargetLatText").GetComponent<Text>();
        // targetLonText = GameObject.Find("TargetLonText").GetComponent<Text>();
    }

    private void Start()
    {
        OnlineMapsControlBase.instance.OnMapClick += OnMapClick;
    }

    private void OnMapClick()
    {
        OnlineMapsControlBase.instance.GetCoords(out lng, out lat);
        if (lat == 0 || lng == 0) return;

        ClearMarkerData();
        currentTarget = null;

        float alt = OnlineMapsElevationManagerBase.GetUnscaledElevationByCoordinate(lng, lat);
        TargetType type = UIManager.instance.SelectedTargetType;

        TargetActor newTarget = new TargetActor(type, lat, lng)
        {
            _ID = Guid.NewGuid().ToString(),
            _Alt = alt
        };

        Texture2D icon = GetIconForType(type);

        var marker = OnlineMapsMarkerManager.CreateItem(lng, lat, icon);
        marker.label = $"Target: {type}";
        marker.align = OnlineMapsAlign.Center;
        marker["data"] = newTarget;
        marker.OnClick += OnTargetClick;
        marker.scale = 0.4f;

        selectedMarker = marker;

        // Store it for scene view
        TargetSceneManager.Instance.RegisterTarget(newTarget); // see next step
    }


    private Texture2D GetIconForType(TargetType type)
    {
        return type switch
        {
            TargetType.STATIONARY => (Texture2D)stationaryIcon,
            TargetType.DYNAMIC => (Texture2D)dynamicIcon,
            _ => null,
        };
    }

    public static void OnTargetClick(OnlineMapsMarkerBase marker)
    {
        //UIManager.instance.selectedTargetPanel.SetActive(true);
        currentTarget = marker["data"] as TargetActor;

        if (currentTarget != null)
        {
            targetTypeText.text = ((TargetType)currentTarget._Type).ToString();
            targetUpdateText.text = $"Last Update: {GetTime(currentTarget._Time)}";
            targetLatText.text = $"Lat: {currentTarget._Lat}";
            targetLonText.text = $"Lon: {currentTarget._Lon}";
        }
    }

    public static DateTime GetTime(string timestamp)
    {
        double ticks = Convert.ToInt64(timestamp);
        TimeSpan time = TimeSpan.FromMilliseconds(ticks);
        return new DateTime(1970, 1, 1) + time;
    }

    public void ClearTargets()
    {
        ClearMarkerData();
        selectedMarker = null;
        OnlineMapsMarkerManager.RemoveAllItems();
        //OnlineMapsMarkerManager.CreateItem(SURGE_GPS.Instance._UserCoords, "User");
    }

    private void ClearMarkerData()
    {
        //UIManager.instance.selectedTargetPanel.SetActive(false);
        // targetTypeText.text = "";
        // targetUpdateText.text = "";
        // targetLatText.text = "";
        // targetLonText.text = "";
    }

    public static void Remove_SelectedTarget()
    {
        var list = OnlineMapsMarkerManager.instance.items;
        int index = list.FindIndex(m => (m["data"] as TargetActor)?._ID == currentTarget?._ID);
        if (index != -1) OnlineMapsMarkerManager.RemoveItem(list[index]);
    }
}
