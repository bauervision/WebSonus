using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class TipController : MonoBehaviour
{
    [Header("UI Element")]
    public CanvasGroup tipCanvasGroup; // Assign your text's CanvasGroup in Inspector

    [Header("Settings")]
    public int requiredEngagements = 3; // Number of RMB presses before hiding
    public float fadeDuration = 1.5f;   // Seconds to fade out

    private int engagementCount = 0;
    private bool fading = false;

    void Update()
    {
        if (fading) return; // Already fading, ignore inputs

        // Detect right mouse button press (only counts once per press)
        if (Input.GetMouseButtonDown(1))
        {
            engagementCount++;

            if (engagementCount >= requiredEngagements)
            {
                StartCoroutine(FadeOutTip());
            }
        }
    }

    private IEnumerator FadeOutTip()
    {
        fading = true;
        float startAlpha = tipCanvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            tipCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / fadeDuration);
            yield return null;
        }

        tipCanvasGroup.alpha = 0f;
        tipCanvasGroup.gameObject.SetActive(false); // Hide entirely after fade
    }
}
