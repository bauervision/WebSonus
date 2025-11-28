using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SonicHUDController : MonoBehaviour
{
    [Header("UI References")]
    public Slider frequencySlider;   // 1 = 30s, 2 = 60s, 3 = 90s
    public Button hearNowButton;
    public TextMeshProUGUI targetCountText;

    [Header("Clue Playback")]
    public AudioSource clueAudioSource;
    public AudioClip directionalClueClip;  // "East", "Continue straight", etc.

    private float clueInterval = 30f;
    private float clueTimer = 0f;

    private void Start()
    {
        if (frequencySlider != null)
        {
            frequencySlider.onValueChanged.AddListener(OnFrequencyChanged);
            OnFrequencyChanged(frequencySlider.value); // initialize
        }

        if (hearNowButton != null)
        {
            hearNowButton.onClick.AddListener(PlayClueNow);
        }
    }

    private void Update()
    {
        if (!SonicHuntManager.Instance.IsHuntActive()) return;

        clueTimer += Time.deltaTime;
        if (clueTimer >= clueInterval)
        {
            PlayClueNow();
            clueTimer = 0f;
        }
    }

    private void OnFrequencyChanged(float value)
    {
        switch ((int)value)
        {
            case 1: clueInterval = 30f; break;
            case 2: clueInterval = 60f; break;
            case 3: clueInterval = 90f; break;
            default: clueInterval = 60f; break;
        }

        Debug.Log($"[SONIC] Clue interval set to {clueInterval} seconds");
        clueTimer = 0f; // reset timer
    }

    public void PlayClueNow()
    {
        if (clueAudioSource != null && directionalClueClip != null)
        {
            clueAudioSource.PlayOneShot(directionalClueClip);
            Debug.Log("[SONIC] Playing clue now");
        }
    }

    public void SetTargetCount(int count)
    {
        if (targetCountText != null)
        {
            targetCountText.text = $"Targets: {count}";
        }
    }
}
