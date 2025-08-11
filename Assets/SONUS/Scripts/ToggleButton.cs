using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class ToggleButton : MonoBehaviour
{
    [Header("State")]
    [SerializeField] private bool isOn = false;

    [Header("Wiring")]
    [SerializeField] private Button button;               // auto-fills if null
    [SerializeField] private Image iconImage;             // optional icon swap
    [SerializeField] private RectTransform content;       // visual to nudge (use a child if using LayoutGroup)
    [SerializeField] private TMP_Text stateText;          // optional label to show state

    [Header("Icons (optional)")]
    [SerializeField] private Sprite activeIcon;
    [SerializeField] private Sprite inactiveIcon;

    [Header("Icon Tint (optional)")]
    [SerializeField] private bool tintIcon = false;
    [SerializeField] private Color activeIconTint = Color.white;
    [SerializeField] private Color inactiveIconTint = new Color(1f, 1f, 1f, 0.7f);

    [Header("Depressed Visual (position only)")]
    [SerializeField] private float depressedY = -2f;      // added to baseline Y
    [SerializeField] private float tweenDuration = 0.08f;

    [Header("State Label (optional)")]
    [SerializeField] private string activeText = "On";
    [SerializeField] private string inactiveText = "Off";
    [SerializeField] private bool tintStateText = false;
    [SerializeField] private Color activeTextColor = Color.white;
    [SerializeField] private Color inactiveTextColor = new Color(1f, 1f, 1f, 0.7f);

    [Header("Events")]
    public UnityEvent onActive;
    public UnityEvent onDeactive;

    private Coroutine tween;
    private Vector2 basePos = Vector2.zero;
    private bool baselined = false;

    public bool IsOn => isOn;

    private void Reset()
    {
        button = GetComponent<Button>();
        content = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        if (!button) button = GetComponent<Button>();
        if (!content) content = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        if (content)
        {
            basePos = content.anchoredPosition; // capture baseline so "off" returns to designed position
            baselined = true;
        }

        if (button) button.onClick.AddListener(Toggle);
        ApplyVisuals(false);
    }

    private void OnDisable()
    {
        if (button) button.onClick.RemoveListener(Toggle);
    }

    /// Re-capture baseline after runtime layout changes (optional).
    public void RebaselineFromCurrent()
    {
        if (!content) return;
        basePos = content.anchoredPosition;
        baselined = true;
        ApplyVisuals(false);
    }

    public void Toggle() => SetIsOn(!isOn, true);

    public void SetIsOn(bool on, bool invokeEvents = true)
    {
        if (isOn == on)
        {
            ApplyVisuals(false);
            return;
        }

        isOn = on;
        ApplyVisuals(true);

        if (invokeEvents)
        {
            if (isOn) onActive?.Invoke();
            else onDeactive?.Invoke();
        }
    }

    private void ApplyVisuals(bool animate)
    {
        // Icon sprite/tint
        if (iconImage)
        {
            if (isOn && activeIcon) iconImage.sprite = activeIcon;
            else if (!isOn && inactiveIcon) iconImage.sprite = inactiveIcon;

            if (tintIcon)
                iconImage.color = isOn ? activeIconTint : inactiveIconTint;
        }

        // State label
        UpdateStateLabel();

        // Position-only nudge
        if (!content || !baselined) return;

        Vector2 targetPos = isOn ? basePos + new Vector2(0f, depressedY) : basePos;

        if (!animate || tweenDuration <= 0f)
        {
            if (tween != null) StopCoroutine(tween);
            content.anchoredPosition = targetPos;
        }
        else
        {
            if (tween != null) StopCoroutine(tween);
            tween = StartCoroutine(TweenTo(targetPos, tweenDuration));
        }
    }

    private void UpdateStateLabel()
    {
        if (!stateText) return;

        stateText.text = isOn ? activeText : inactiveText;

        if (tintStateText)
            stateText.color = isOn ? activeTextColor : inactiveTextColor;
    }

    private IEnumerator TweenTo(Vector2 targetPos, float dur)
    {
        float t = 0f;
        Vector2 startPos = content.anchoredPosition;

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / dur);
            content.anchoredPosition = Vector2.Lerp(startPos, targetPos, a);
            yield return null;
        }

        content.anchoredPosition = targetPos;
        tween = null;
    }

    // Optional helpers to change label text at runtime
    public void SetLabelTexts(string active, string inactive)
    {
        activeText = active;
        inactiveText = inactive;
        UpdateStateLabel();
    }
}
