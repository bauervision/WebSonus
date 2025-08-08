using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ToastView : MonoBehaviour
{
    [Header("Refs")]
    public CanvasGroup group;
    public RectTransform root;
    public TextMeshProUGUI messageText;
    public Button closeButton;

    [Header("Anim")]
    public float slidePixels = 40f;
    public float fadeInTime = 0.18f;
    public float fadeOutTime = 0.15f;

    private Coroutine anim;
    private bool isClosing;

    public void Init(string message, bool showClose, System.Action onClosed)
    {
        messageText.text = message;
        closeButton.gameObject.SetActive(showClose);
        closeButton.onClick.RemoveAllListeners();
        closeButton.onClick.AddListener(() => Close(onClosed));

        group.alpha = 0f;

        // Slide from below (Y axis)
        Vector2 from = root.anchoredPosition + new Vector2(0, -slidePixels);
        Vector2 to = root.anchoredPosition;

        root.anchoredPosition = from;
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(FadeSlide(from, to, 0f, 1f, fadeInTime));
    }

    public void AutoClose(float delay, System.Action onClosed)
    {
        if (delay <= 0f) return;
        StartCoroutine(AutoCloseRoutine(delay, onClosed));
    }

    public void Close(System.Action onClosed)
    {
        if (isClosing) return;
        isClosing = true;
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(FadeOutAndDestroy(onClosed));
    }

    IEnumerator AutoCloseRoutine(float delay, System.Action onClosed)
    {
        yield return new WaitForSeconds(delay);
        Close(onClosed);
    }

    IEnumerator FadeSlide(Vector2 fromPos, Vector2 toPos, float fromA, float toA, float t)
    {
        float e = 0f;
        while (e < t)
        {
            float k = e / t;
            // smoothstep
            k = k * k * (3f - 2f * k);
            root.anchoredPosition = Vector2.Lerp(fromPos, toPos, k);
            group.alpha = Mathf.Lerp(fromA, toA, k);
            e += Time.unscaledDeltaTime;
            yield return null;
        }
        root.anchoredPosition = toPos;
        group.alpha = toA;
        anim = null;
    }

    IEnumerator FadeOutAndDestroy(System.Action onClosed)
    {
        // Slide back down on close
        Vector2 from = root.anchoredPosition;
        Vector2 to = root.anchoredPosition + new Vector2(0, -slidePixels);
        float t = fadeOutTime, e = 0f;

        while (e < t)
        {
            float k = e / t; k = k * k * (3f - 2f * k);
            root.anchoredPosition = Vector2.Lerp(from, to, k);
            group.alpha = Mathf.Lerp(1f, 0f, k);
            e += Time.unscaledDeltaTime;
            yield return null;
        }
        onClosed?.Invoke();
        Destroy(gameObject);
    }
}
