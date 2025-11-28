using System.Collections.Generic;
using UnityEngine;

public class ToastManager : MonoBehaviour
{
    public static ToastManager Instance { get; private set; }

    [Header("Spawn")]
    public RectTransform container;         // parent under your UI canvas
    public ToastView toastPrefab;           // assign prefab
    Vector2 startAnchorOffset = new(0f, 24f); // X=0 center, 24px from bottom
    public float verticalSpacing = 8f;

    private readonly List<ToastView> live = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Show a toast.
    /// durationSeconds: if <= 0, no auto-close (close button only).
    /// </summary>
    public void Show(string message, float durationSeconds = 4f, bool showCloseButton = false)
    {
        if (toastPrefab == null || container == null)
        {
            Debug.LogWarning("[Toast] Missing prefab or container.");
            return;
        }

        var tv = Instantiate(toastPrefab, container);
        live.Add(tv);

        // Position for stacking (top-right)
        LayoutStack();

        tv.Init(message, showCloseButton, () =>
        {
            live.Remove(tv);
            LayoutStack();
        });

        if (!showCloseButton && durationSeconds > 0f)
            tv.AutoClose(durationSeconds, () =>
            {
                live.Remove(tv);
                LayoutStack();
            });
    }

    private void LayoutStack()
    {
        float y = startAnchorOffset.y; // start near bottom
        for (int i = 0; i < live.Count; i++)
        {
            var rt = live[i].GetComponent<RectTransform>();

            // Bottom-center
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);

            rt.anchoredPosition = new Vector2(startAnchorOffset.x, y);

            // next toast sits above the previous
            y += rt.sizeDelta.y + verticalSpacing;
        }
    }
}
