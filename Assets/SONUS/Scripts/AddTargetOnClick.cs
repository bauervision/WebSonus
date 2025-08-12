using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;

public class AddTargetOnClick : MonoBehaviour
{
    public static AddTargetOnClick instance;

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
        instance = this;
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
        // Block until user explicitly chooses a type
        if (UIManager.instance == null || !UIManager.instance.HasChosenTargetType)
        {
            GuideManager.Instance.ShowOneShot(
                "Choose a target type first.",
                anchoredPosition: new Vector2(-333f, 69f),  // center of the canvas; use any X/Y you like
                arrow: ArrowDir.Left,
                dimBackground: true
            );

            return;
        }

        OnlineMapsControlBase.instance.GetCoords(out lng, out lat);
        if (lat == 0 || lng == 0) return;

        ClearMarkerData();
        currentTarget = null;

        float alt = OnlineMapsElevationManagerBase.GetUnscaledElevationByCoordinate(lng, lat);
        TargetType type = UIManager.instance.SelectedTargetType;

        TargetActor newTarget = new TargetActor(type, lat, lng)
        {
            _ID = Guid.NewGuid().ToString(),
            _Alt = alt,
            _Name = type == TargetType.STATIONARY ? "Stationary Target" : "Dynamic Target"
        };

        Texture2D icon = GetIconForType(type);

        var marker = OnlineMapsMarkerManager.CreateItem(lng, lat, icon);
        marker.label = $"Target: {type}";
        marker.align = OnlineMapsAlign.Center;
        marker["data"] = newTarget;
        marker.scale = 0.4f;
        selectedMarker = marker;

        // Store it for scene view
        TargetSceneManager.Instance.RegisterTarget(newTarget); // see next step
    }


    public static Texture2D GetIconForType(TargetType type)
    {
        return type switch
        {
            TargetType.STATIONARY => instance.stationaryIcon,
            TargetType.DYNAMIC => instance.dynamicIcon,
            _ => null,
        };
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
