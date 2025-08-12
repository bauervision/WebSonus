using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

public class AudioCueSlider : MonoBehaviour
{
    public static AudioCueSlider instance;

    [Header("UI")]
    public Slider countdownSlider;           // Set Min=0 in Inspector (we’ll set Max at runtime)
    public TextMeshProUGUI timeLabel;        // Optional "23s" readout

    [Header("Timing")]
    public float intervalSeconds = 10f;      // Default cadence
    public bool useUnscaledTime = false;     // If you want it to tick during pauses

    [Header("Output")]
    public UnityEvent OnCue;                 // Hook your audio cue here (SonicHuntManager.PlayCue)

    private float _remaining;

    private void Awake() { instance = this; }

    void Start()
    {
        ApplyIntervalBounds();
        ResetTimer();
    }

    void Update()
    {
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        _remaining -= dt;

        // Update UI
        if (countdownSlider) countdownSlider.value = Mathf.Max(_remaining, 0f);
        if (timeLabel) timeLabel.text = Mathf.CeilToInt(Mathf.Max(_remaining, 0f)) + "s";

        if (_remaining <= 0f)
        {
            FireCueAndReset();
        }
    }

    public void SetInterval(float seconds)
    {
        intervalSeconds = Mathf.Max(1f, seconds);
        ApplyIntervalBounds();
        ResetTimer();
    }

    public void HearNow() => FireCueAndReset();

    public void ResetTimer()
    {
        _remaining = intervalSeconds;
        if (countdownSlider) countdownSlider.value = intervalSeconds;
    }

    private void ApplyIntervalBounds()
    {
        if (!countdownSlider) return;
        countdownSlider.minValue = 0f;
        countdownSlider.maxValue = intervalSeconds;
        countdownSlider.wholeNumbers = false;
        countdownSlider.value = intervalSeconds;
    }

    private void FireCueAndReset()
    {
        OnCue?.Invoke();   // play AudioManager.HearNow which fires off the sound
        ResetTimer();      // restart countdown
        if (countdownSlider) countdownSlider.value = intervalSeconds;
    }
}
