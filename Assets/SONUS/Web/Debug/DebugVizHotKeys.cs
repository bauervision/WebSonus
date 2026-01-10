using UnityEngine;

public class DebugVizHotkeys : MonoBehaviour
{
    [Header("Refs")]
    public AudioGuidanceLineVizLR guidanceViz;
    public ARTargetReticleController reticle;

    [Header("Hotkey")]
    public KeyCode toggleKey = KeyCode.UpArrow;

    [Header("State")]
    public bool startVisible = true;

    bool _visible;

    void Awake()
    {
        _visible = startVisible;
        Apply();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            _visible = !_visible;
            Apply();
        }
    }

    void Apply()
    {
        if (guidanceViz != null) guidanceViz.SetVisible(_visible);
        if (reticle != null) reticle.SetVisible(_visible);
    }
}
