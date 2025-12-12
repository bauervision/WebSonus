using UnityEngine;

public enum ArrowDir
{
    None,
    Up,
    Right,
    Down,
    Left
}

[System.Serializable]
public class GuideStep
{
    [Tooltip("Optional unique key if you want per-step tracking later")]
    public string id;

    [TextArea(2, 6)]
    public string text;

    [Tooltip("AnchoredPosition for the guide panel (Canvas local space, matches the RectTransform X/Y you see in the Inspector)")]
    public Vector2 position;

    [Tooltip("Which arrow to show; None means no arrow")]
    public ArrowDir arrow = ArrowDir.Down;

    [Tooltip("If overlayGroup is assigned, dim this step (optional)")]
    public bool dimBackground = true;
}
