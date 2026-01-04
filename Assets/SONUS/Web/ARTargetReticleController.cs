// Assets/SONUS/Web/ARTargetReticleController.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    public float foundRadiusMeters = 20f;
    public float maxShowDistanceMeters = 2000f;

    [Header("Optional")]
    public bool showDistanceLabel = true;
    public string foundText = "FOUND";

    RectTransform _reticle;
    TMP_Text _label;

    void Awake()
    {
        EnsureReticle();
        Hide();
    }

    void LateUpdate()
    {
        if (!arModeEnabled) { Hide(); return; }
        if (targetManager == null || arCamera == null || canvas == null) { Hide(); return; }

        if (targetManager.currentTarget == null) { Hide(); return; }

        if (!targetManager.TryGetTargetWorldPos(out Vector3 targetWorld)) { Hide(); return; }

        if (targetManager.playerRoot == null) { Hide(); return; }

        float d = Vector3.Distance(targetManager.playerRoot.position, targetWorld);
        if (d > maxShowDistanceMeters) { Hide(); return; }

        // Must be visible on-screen
        if (!WorldToCanvas(targetWorld, out Vector2 anchored)) { Hide(); return; }

        // Show
        EnsureReticle();
        _reticle.gameObject.SetActive(true);
        _reticle.anchoredPosition = anchored;
        _reticle.localScale = Vector3.one * reticleScale;

        // Found check: within radius AND visible (already visible if we got here)
        if (d <= foundRadiusMeters)
        {
            if (_label != null) _label.text = foundText;
            return; // TargetManager handles respawn now
        }


        if (_label != null && showDistanceLabel)
            _label.text = FormatDistance(d);
        else if (_label != null)
            _label.text = "";
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
