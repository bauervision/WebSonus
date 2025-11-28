using System.Collections.Generic;
using UnityEngine;

public class TargetSelectionMode : MonoBehaviour
{
    public static TargetSelectionMode Instance { get; private set; }

    [Header("Refs")]
    public OnlineMaps map;                    // assign
    public GameObject selectionHintUI;        // little “Click a target…” banner
    public HUDActiveTargetLabel hudLabel;     // shows Active Target: <name>

    [Header("Behavior")]
    [SerializeField] private bool showHUDOnlyAfterMissionLoaded = true;

    private bool _isSelecting;
    private bool _missionLoaded;

    private void Awake() => Instance = this;

    private void Start()
    {
        // start hidden so we don’t show “None” on app boot
        if (showHUDOnlyAfterMissionLoaded && hudLabel)
            hudLabel.gameObject.SetActive(false);
    }

    /// <summary>Call this once your mission is actually loaded.</summary>
    public void MarkMissionLoaded()
    {
        _missionLoaded = true;

        // now we can safely reveal the HUD label
        if (hudLabel) hudLabel.gameObject.SetActive(true);

        // optional: refresh from currently active target (if one exists)
        var a = ActiveTargetManager.Instance?.ActiveTarget;
        if (a != null) UpdateHUDName(a);
    }

    public void Enter()
    {
        if (_isSelecting) return;
        _isSelecting = true;
        if (selectionHintUI) selectionHintUI.SetActive(true);
        SubscribeMarkerClicks(true);
    }

    public void Exit()
    {
        if (!_isSelecting) return;
        _isSelecting = false;
        if (selectionHintUI) selectionHintUI.SetActive(false);
        SubscribeMarkerClicks(false);
    }

    public void ToggleSelectionMode() => (_isSelecting ? (System.Action)Exit : Enter)();

    private void SubscribeMarkerClicks(bool enable)
    {
        // Texture markers
        var mm = OnlineMapsMarkerManager.instance;
        if (mm != null)
        {
            foreach (var m in mm.items)
            {
                m.OnClick -= OnTextureMarkerClick;
                if (enable) m.OnClick += OnTextureMarkerClick;
            }
        }


    }

    private void OnTextureMarkerClick(OnlineMapsMarkerBase marker)
    {
        if (!_isSelecting || marker == null) return;

        if (TrySelectFromMarker(marker)) Exit();
    }


    private bool TrySelectFromMarker(object markerObj)
    {
        // Texture markers support indexer marker["key"] directly
        if (markerObj is OnlineMapsMarker markerTex)
        {
            var actor = markerTex["data"] as TargetActor;
            if (actor != null) return SetActive(actor);
            // fallback: id stored as marker["id"]
            if (markerTex["id"] is string sid && !string.IsNullOrEmpty(sid)) return SetActiveById(sid);

            return false;
        }
        return false;
    }

    private bool SetActive(TargetActor actor)
    {
        if (actor == null) return false;
        MarkMissionLoaded();
        ActiveTargetManager.Instance.SetActiveTarget(actor);
        UpdateHUDName(actor); // gated inside
        TargetHUDManager.instance?.RefreshAll();
        return true;
    }

    private bool SetActiveById(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;

        ActiveTargetManager.Instance.SetActiveTargetById(id);
        var a = ActiveTargetManager.Instance.ActiveTarget;
        UpdateHUDName(a); // gated inside
        TargetHUDManager.instance?.RefreshAll();
        return a != null;
    }

    private void UpdateHUDName(TargetActor actor)
    {
        // HARD GATE: do not set (or show) until a mission is loaded
        if (showHUDOnlyAfterMissionLoaded && !_missionLoaded) return;

        var name = (actor != null && !string.IsNullOrEmpty(actor._Name)) ? actor._Name : "Unknown";
        hudLabel?.SetName(name);
    }

}
