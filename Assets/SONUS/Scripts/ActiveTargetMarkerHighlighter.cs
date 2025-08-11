using UnityEngine;

public class ActiveTargetMarkerHighlighter : MonoBehaviour
{
    [Header("Active Icons")]
    public Texture2D activeStationaryIcon;
    public Texture2D activeDynamicIcon;

    [Header("Normal Icons (optional)")]
    public Texture2D normalStationaryIcon;
    public Texture2D normalDynamicIcon;

    [Header("Marker Scale")]
    public float activeScale = 0.5f;
    public float normalScale = 0.4f;

    private string lastIdApplied;



    void LateUpdate()
    {
        var mgr = ActiveTargetManager.Instance;
        if (mgr == null) return;

        var current = mgr.ActiveTarget;
        var currentId = current?._ID;

        // change detection
        if (currentId != lastIdApplied)
        {
            ApplyHighlight(current);
            lastIdApplied = currentId;
        }
    }

    private void ApplyHighlight(TargetActor active)
    {
        // 1) Revert previous
        if (!string.IsNullOrEmpty(lastIdApplied))
        {
            var prev = TargetSceneManager.Instance.GetTargetById(lastIdApplied);
            var prevMarker = prev?.GetMarker();
            if (prevMarker != null)
            {
                Texture2D icon =
                    (prev != null && (TargetType)prev._Type == TargetType.STATIONARY)
                    ? (normalStationaryIcon ?? AddTargetOnClick.GetIconForType(TargetType.STATIONARY))
                    : (normalDynamicIcon ?? AddTargetOnClick.GetIconForType(TargetType.DYNAMIC));

                prevMarker.texture = icon ?? prevMarker.texture; // fallback to whatever it had
                prevMarker.scale = normalScale;
                if (!string.IsNullOrEmpty(prev?._Name)) prevMarker.label = $"Target: {prev._Name}";
            }
        }

        // 2) Highlight new
        if (active != null)
        {
            var m = active.GetMarker();
            if (m != null)
            {
                bool isStationary = (TargetType)active._Type == TargetType.STATIONARY;
                m.texture = isStationary ? activeStationaryIcon : activeDynamicIcon;
                m.scale = activeScale;
                if (!string.IsNullOrEmpty(active._Name)) m.label = $"Target: {active._Name} (active)";
            }
        }

        OnlineMaps.instance?.Redraw();
    }
}
