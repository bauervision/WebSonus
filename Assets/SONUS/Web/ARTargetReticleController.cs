// Assets/SONUS/Web/ARTargetReticleController.cs
using TMPro;
using UnityEngine;

public class ARTargetReticleController : MonoBehaviour
{
    [Header("Refs")]
    public TargetManager targetManager;
    public Camera arCamera;

    [Tooltip("Canvas root for AR HUD (Screen Space Overlay recommended).")]
    public Canvas canvas;

    [Header("Reticle UI")]
    public RectTransform reticlePrefab;   // simple UI prefab (Image + TMP_Text)
    public float reticleScale = 1f;

    [Header("Rules")]
    public bool arModeEnabled = false;
    public float foundRadiusMeters = 12f;
    public float maxShowDistanceMeters = 500f;

    [Header("Optional")]
    public bool showDistanceLabel = true;
    public string foundText = "FOUND";

    [Header("Debug/Display")]
    [Tooltip("Hard override to hide/show the reticle regardless of arModeEnabled.")]
    public bool forceVisible = true;

    private RectTransform _reticle;
    private TMP_Text _label;

    void Awake()
    {
        EnsureReticle();
        Hide();
    }

    void LateUpdate()
    {
        // Hard override: if forceVisible is off, always hide (even if arModeEnabled is true)
        if (!forceVisible) { Hide(); return; }

        if (!arModeEnabled) { Hide(); return; }
        if (targetManager == null || arCamera == null || canvas == null) { Hide(); return; }
        if (targetManager.currentTarget == null) { Hide(); return; }

        // Use world only for screen placement
        if (!targetManager.TryGetTargetWorldPos(out Vector3 targetWorld)) { Hide(); return; }

        // Use GEO for distance (meters)
        float d = targetManager.DistanceToTargetMeters();

#if UNITY_EDITOR
        // World-space distance (should generally go down as you walk closer in 3D if anchors are correct)
        float dw = Vector3.Distance(arCamera.transform.position, targetWorld);
        if (_label != null)
        {
            // show both to compare live
            _label.text = $"{FormatDistance(d)} | W:{Mathf.RoundToInt(dw)}m";
        }
#endif

        if (!float.IsFinite(d)) { Hide(); return; }
        if (d > maxShowDistanceMeters) { Hide(); return; }

        // Must be visible on-screen
        if (!WorldToCanvas(targetWorld, out Vector2 anchored)) { Hide(); return; }

        // Show
        EnsureReticle();
        _reticle.gameObject.SetActive(true);
        _reticle.anchoredPosition = anchored;
        _reticle.localScale = Vector3.one * reticleScale;

        // Found check (meters)
        if (d <= foundRadiusMeters)
        {
            if (_label != null) _label.text = foundText;
            return; // TargetManager handles respawn
        }

        if (_label != null && showDistanceLabel)
            _label.text = FormatDistance(d);
        else if (_label != null)
            _label.text = "";
    }

    // Public API for hotkeys / demo toggles
    public void SetVisible(bool on)
    {
        forceVisible = on;
        if (!forceVisible) Hide();
    }

    // --------------------------
    // Helpers
    // --------------------------

    void EnsureReticle()
    {
        if (_reticle != null) return;
        if (reticlePrefab == null) return;

        _reticle = Instantiate(reticlePrefab, canvas.transform);
        _label = _reticle.GetComponentInChildren<TMP_Text>(true);
    }

    void Hide()
    {
        if (_reticle != null) _reticle.gameObject.SetActive(false);
    }

    bool WorldToCanvas(Vector3 world, out Vector2 canvasPos)
    {
        canvasPos = default;

        Vector3 sp = arCamera.WorldToScreenPoint(world);

        if (sp.z <= 0f) return false;
        if (sp.x < 0f || sp.x > Screen.width) return false;
        if (sp.y < 0f || sp.y > Screen.height) return false;

        var canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) return false;

        Camera camForCanvas = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : arCamera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            new Vector2(sp.x, sp.y),
            camForCanvas,
            out canvasPos
        );
    }

    static string FormatDistance(float meters)
    {
        if (meters < 1000f) return $"{Mathf.RoundToInt(meters)} m";
        return $"{(meters / 1000f):F1} km";
    }
}
