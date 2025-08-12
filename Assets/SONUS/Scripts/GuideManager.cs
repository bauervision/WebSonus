using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum ArrowDir { None, Up, Right, Down, Left }

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

public class GuideManager : MonoBehaviour
{
    public static GuideManager Instance;

    [Header("UI (assign in Inspector)")]
    public RectTransform guidePanel;          // floating tip panel
    public TextMeshProUGUI guideText;         // tip body text

    public Button nextButton;
    public Button prevButton;
    public Button skipButton;
    public Toggle dontShowAgainToggle;        // shown on step 0 if ShowOnce is enabled

    [Header("Arrows (pre-oriented sprites)")]
    public GameObject arrowTop;
    public GameObject arrowRight;
    public GameObject arrowBottom;
    public GameObject arrowLeft;

    [Header("Optional overlay fade (leave null to disable)")]
    public CanvasGroup overlayGroup;          // full-screen overlay CanvasGroup (your dimmer lives elsewhere & will show when we show)

    [Header("Authoring (Inspector)")]
    public List<GuideStep> steps = new();

    [Header("Behavior")]
    public bool showOnStart = true;
    [Tooltip("If set, the whole sequence shows once per install")]
    public string showOnceKey = "FTUE_Main";
    public float fadeDuration = 0.25f;
    public bool debugLogs = false;

    // runtime
    private int _index = -1;
    private bool _running = false;

    private const string PrefsPrefix = "GuideSeen_";

    // Active list for the current run (defaults to inspector “steps” when null)
    private List<GuideStep> _activeSteps;
    private string _activeOnceKey;



    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (overlayGroup != null)
        {
            overlayGroup.alpha = 0f;
            overlayGroup.gameObject.SetActive(false);
        }
        if (guidePanel != null) guidePanel.gameObject.SetActive(false);
        ShowArrow(ArrowDir.None); // hide all arrows at boot
    }

    private void Start()
    {
        if (nextButton) nextButton.onClick.AddListener(Next);
        if (prevButton) prevButton.onClick.AddListener(Prev);
        if (skipButton) skipButton.onClick.AddListener(Skip);

        if (showOnStart) StartSequenceRespectingOnce();
    }

    // ----- Public API -----

    public void StartSequenceRespectingOnce()
    {
        // if (!string.IsNullOrEmpty(showOnceKey) && HasSeen(showOnceKey)) return;
        StartSequence();
    }

    public void StartSequence()
    {
        if (_running) return;
        if (steps == null || steps.Count == 0) return;

        _activeSteps = steps;            // use inspector-authored list
        _activeOnceKey = showOnceKey;    // respect field
        _running = true;
        _index = -1;

        FadeOverlay(true, _activeSteps[0].dimBackground);
        Next();
    }
    public void EndSequence(bool markSeen = true)
    {
        if (!_running) return;
        _running = false;

        guidePanel.gameObject.SetActive(false);
        FadeOverlay(false, false);

        _activeSteps = null;
        _activeOnceKey = null;
    }


    public void Next()
    {
        if (!_running) return;

        int newIndex = Mathf.Clamp(_index + 1, 0, steps.Count);
        if (newIndex >= steps.Count) { EndSequence(true); return; }
        _index = newIndex;
        ShowCurrentStep();
    }

    public void Prev()
    {
        if (!_running) return;
        int newIndex = Mathf.Clamp(_index - 1, 0, steps.Count - 1);
        if (newIndex == _index) return;
        _index = newIndex;
        ShowCurrentStep();
    }

    public void Skip()
    {
        if (debugLogs) Debug.Log("[Guide] Skip pressed");

        // If 'Don't show again' is ticked, mark sequence as seen.
        if (dontShowAgainToggle && dontShowAgainToggle.isOn && !string.IsNullOrEmpty(showOnceKey))
            MarkSeen(showOnceKey);

        EndSequence(dontShowAgainToggle && dontShowAgainToggle.isOn);
    }

    // ----- Persistence -----

    public bool HasSeen(string key) => PlayerPrefs.GetInt(PrefsPrefix + key, 0) == 1;

    public void MarkSeen(string key)
    {
        PlayerPrefs.SetInt(PrefsPrefix + key, 1);
        PlayerPrefs.Save();
    }

    // ----- Internals -----

    private void ShowCurrentStep()
    {
        if (_index < 0 || _index >= _activeSteps.Count) return;
        var s = _activeSteps[_index];

        // dim/backdrop, text, placement, arrows, etc.
        if (guideText) guideText.text = s.text;
        if (guidePanel)
        {
            guidePanel.anchoredPosition = s.position;  // <-- pure Vector2 placement
            guidePanel.gameObject.SetActive(true);
        }
        ShowArrow(s.arrow);

        if (prevButton) prevButton.gameObject.SetActive(_index > 0);
        if (nextButton) nextButton.gameObject.SetActive(true);
        if (dontShowAgainToggle) dontShowAgainToggle.gameObject.SetActive(false); // one-shot: no toggle
    }


    private void ShowArrow(ArrowDir dir)
    {
        if (arrowTop) arrowTop.SetActive(dir == ArrowDir.Up);
        if (arrowRight) arrowRight.SetActive(dir == ArrowDir.Right);
        if (arrowBottom) arrowBottom.SetActive(dir == ArrowDir.Down);
        if (arrowLeft) arrowLeft.SetActive(dir == ArrowDir.Left);
        // None => all hidden
        if (dir == ArrowDir.None)
        {
            if (arrowTop) arrowTop.SetActive(false);
            if (arrowRight) arrowRight.SetActive(false);
            if (arrowBottom) arrowBottom.SetActive(false);
            if (arrowLeft) arrowLeft.SetActive(false);
        }
    }

    private void FadeOverlay(bool show, bool dim)
    {
        if (!overlayGroup)
        {
            // No overlay assigned—just toggle the panel and let your existing dimmer handle itself.
            return;
        }
        StopAllCoroutines();
        StartCoroutine(CoFadeOverlay(show, dim));
    }

    private System.Collections.IEnumerator CoFadeOverlay(bool show, bool dim)
    {
        overlayGroup.gameObject.SetActive(true);
        overlayGroup.blocksRaycasts = dim;

        float start = overlayGroup.alpha;
        float end = show ? 1f : 0f;
        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            overlayGroup.alpha = Mathf.Lerp(start, end, t / fadeDuration);
            yield return null;
        }

        overlayGroup.alpha = end;
        if (!show) overlayGroup.gameObject.SetActive(false);
    }

    public void ShowOneShot(string message, Vector2 anchoredPosition, ArrowDir arrow = ArrowDir.None, bool dimBackground = true)
    {
        var single = new List<GuideStep>
    {
        new GuideStep { text = message, position = anchoredPosition, arrow = arrow, dimBackground = dimBackground }
    };
        StartSequence(single, respectOnce: false, onceKeyOverride: null);
    }


    public void StartSequence(List<GuideStep> customSteps, bool respectOnce = false, string onceKeyOverride = null)
    {
        if (_running) return;
        if (customSteps == null || customSteps.Count == 0) return;

        _activeSteps = customSteps;
        _activeOnceKey = respectOnce ? onceKeyOverride : null;

        if (respectOnce && !string.IsNullOrEmpty(_activeOnceKey) /* && HasSeen(_activeOnceKey) */)  // if you kept persistence
            return;

        _running = true;
        _index = -1;

        FadeOverlay(true, _activeSteps[0].dimBackground);
        Next();
    }



}
