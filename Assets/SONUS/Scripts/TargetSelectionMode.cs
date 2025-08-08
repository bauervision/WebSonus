using System.Collections.Generic;
using UnityEngine;

public class TargetSelectionMode : MonoBehaviour
{
    public static TargetSelectionMode Instance { get; private set; }

    [Header("Refs")]
    public OnlineMaps map;                    // assign
    public GameObject selectionHintUI;        // little “Click a target…” banner
    public HUDActiveTargetLabel hudLabel;     // shows Active Target: <name>

    private bool _isSelecting;

    private void Awake() => Instance = this;

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
        ActiveTargetManager.Instance.SetActiveTarget(actor);
        hudLabel?.SetName(string.IsNullOrEmpty(actor._Name) ? "Unknown" : actor._Name);
        return true;
    }

    private bool SetActiveById(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        ActiveTargetManager.Instance.SetActiveTargetById(id);
        var a = ActiveTargetManager.Instance.ActiveTarget;
        hudLabel?.SetName(a != null && !string.IsNullOrEmpty(a._Name) ? a._Name : "Unknown");
        return a != null;
    }
}
