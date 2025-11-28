using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GuideTipWidget : MonoBehaviour
{
    [Header("Refs")]
    public RectTransform Rect;
    public CanvasGroup canvasGroup;
    public TextMeshProUGUI body;
    public Button nextBtn;
    public Button prevBtn;
    public Button skipBtn;
    public Toggle dontShowToggle;

    [Header("Arrow Parts")]
    public RectTransform arrowUp;
    public RectTransform arrowDown;
    public RectTransform arrowLeft;
    public RectTransform arrowRight;

    [Header("Layout")]
    public float Margin = 24f; // nudge from anchor

    public Action onStateChange; // used by manager to proceed in sequences

    void Awake()
    {
        if (canvasGroup) { canvasGroup.alpha = 0f; }
    }

    public void Setup(string message, Action onNext, Action onSkip, bool showPrev, bool showDontShow, Action onPrev = null, Action<bool> onDontShow = null)
    {
        if (body) body.text = message;

        if (nextBtn) { nextBtn.onClick.RemoveAllListeners(); nextBtn.onClick.AddListener(() => onNext?.Invoke()); }
        if (skipBtn) { skipBtn.onClick.RemoveAllListeners(); skipBtn.onClick.AddListener(() => onSkip?.Invoke()); }
        if (prevBtn)
        {
            prevBtn.gameObject.SetActive(showPrev);
            prevBtn.onClick.RemoveAllListeners();
            if (showPrev) prevBtn.onClick.AddListener(() => onPrev?.Invoke());
        }
        if (dontShowToggle)
        {
            dontShowToggle.gameObject.SetActive(showDontShow);
            dontShowToggle.onValueChanged.RemoveAllListeners();
            if (showDontShow) dontShowToggle.onValueChanged.AddListener(v => onDontShow?.Invoke(v));
        }

        FadeIn(0.2f);
    }

    public void SetArrow(ArrowDir dir)
    {
        arrowUp?.gameObject.SetActive(dir == ArrowDir.Up);
        arrowDown?.gameObject.SetActive(dir == ArrowDir.Down);
        arrowLeft?.gameObject.SetActive(dir == ArrowDir.Left);
        arrowRight?.gameObject.SetActive(dir == ArrowDir.Right);
    }

    public void FadeIn(float duration)
    {
        StopAllCoroutines();
        StartCoroutine(FadeTo(1f, duration));
    }

    public void FadeOut(float duration)
    {
        StopAllCoroutines();
        StartCoroutine(FadeTo(0f, duration));
    }

    private System.Collections.IEnumerator FadeTo(float target, float dur)
    {
        if (!canvasGroup) yield break;
        float start = canvasGroup.alpha;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(start, target, t / dur);
            yield return null;
        }
        canvasGroup.alpha = target;
    }

    // Called by manager to break wait loops
    public void SignalStateChange() => onStateChange?.Invoke();
}
