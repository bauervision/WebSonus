using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public enum AppMode
{
    Map,        // 2D OnlineMaps view
    SceneAR,    // 3D OnlineMaps terrain + HUD reticles
    SceneSonic  // 3D OnlineMaps terrain + sonic cues only
}

public class UIManagerOLM : MonoBehaviour
{
    public static UIManagerOLM instance { get; private set; }

    [Header("Roots")]
    public GameObject mapCanvas;
    public GameObject mapRoot;
    public GameObject sceneCanvas;
    public GameObject sceneModeRoot;
    public GameObject HUDcanvas;
    public GameObject settingsPanel;
    public GameObject sonicStarterPanel;
    public GameObject sonicTools;
    public GameObject endMissionUI;

    [Header("Player")]
    public FirstPersonController player;

    [Header("Frequency UI")]
    public TextMeshProUGUI frequencyText;
    [SerializeField] private Slider frequencySlider;
    public float frequencySeconds = 10f;

    [Header("Target Selection")]
    public TargetType SelectedTargetType { get; private set; } = TargetType.STATIONARY;
    public Button stationaryButton;
    public Button dynamicButton;
    public bool HasChosenTargetType { get; private set; } = false;

    [Header("App Mode")]
    [SerializeField] private AppMode startMode = AppMode.Map;
    public AppMode CurrentMode { get; private set; } = AppMode.Map;

    [Header("Events")]
    public UnityEvent onFound = new();

    [Header("Debug")]
    [Tooltip("If true, auto-jumps into Scene + Sonic on play for quick testing.")]
    [SerializeField] private bool debugAutoStartSonic = false;

    #region Lifecycle

    private void Awake()
    {
        // Singleton guard
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        if (frequencySlider != null)
        {
            frequencySlider.minValue = 1;
            frequencySlider.maxValue = 3;
            frequencySlider.wholeNumbers = true;               // keep steps 1,2,3
            frequencySlider.onValueChanged.AddListener(SetUpdateFrequency);
        }

        // Ensure target buttons have an initial visual state
        HighlightSelectedButton();
    }

    private void Start()
    {
        // Apply start mode (usually Map)
        SetMode(startMode);

        // Optional debug auto-flow into Sonic scene
        if (debugAutoStartSonic)
        {
            StartCoroutine(DebugForceSceneAndSonic());
        }
    }

    #endregion

    #region Debug helper

    private IEnumerator DebugForceSceneAndSonic()
    {
        // wait a frame so everything initializes
        yield return null;

        // Jump directly into Scene + Sonic
        SetMode(AppMode.SceneSonic);

        // Optionally, auto-start the mission after 1 second:
        yield return new WaitForSeconds(1f);
        StartSonicHunting();
    }

    #endregion

    #region Mode management

    public void SetMode(AppMode newMode)
    {
        if (CurrentMode == newMode) return;

        CurrentMode = newMode;

        switch (CurrentMode)
        {
            case AppMode.Map:
                EnterMapMode_Internal();
                break;

            case AppMode.SceneAR:
                EnterSceneMode_Internal(isSonic: false);
                break;

            case AppMode.SceneSonic:
                EnterSceneMode_Internal(isSonic: true);
                break;
        }
    }

    // Button hooks

    public void OnEnterSceneModeButton_UI()
    {
        SetMode(AppMode.SceneAR);
    }

    public void OnEnterSceneARButton()
    {
        SetMode(AppMode.SceneAR);
    }

    public void OnEnterSceneSonicButton()
    {
        SetMode(AppMode.SceneSonic);
    }

    public void OnBackToMapButton_UI()
    {
        SetMode(AppMode.Map);
    }

    private void EnterSceneMode_Internal(bool isSonic)
    {
        // 1) Canvas switching
        if (mapCanvas != null) mapCanvas.SetActive(false);
        if (mapRoot != null) mapRoot.SetActive(false);
        if (sceneModeRoot != null) sceneModeRoot.SetActive(true);
        if (sceneCanvas != null) sceneCanvas.SetActive(true);

        // 2) Player + terrain mapping (OnlineMaps via PlayerLocator)
        if (PlayerLocator.instance != null)
        {
            // Note: if you want to move to mission center, do it before EnterSceneMapping
            PlayerLocator.instance.liveSyncFromPlayer = true;
            PlayerLocator.instance.EnterSceneMapping();
        }

        // 3) HUD vs Sonic
        bool isAR = !isSonic;

        if (HUDcanvas != null) HUDcanvas.SetActive(isAR);
        TargetHUDManager.instance?.SetVisualsEnabled(isAR);

        if (isAR)
        {
            AudioManager.Instance?.StopSonic();
        }
        else
        {
            // Option A: show starter panel first, then user hits "Start"
            if (sonicStarterPanel != null)
                sonicStarterPanel.SetActive(true);

            // Option B (if you prefer): uncomment to auto-start sonic immediately
            // AudioManager.Instance?.StartSonic(frequencySeconds);

            if (sonicTools != null)
                sonicTools.SetActive(true);
        }

        // 4) Cursor & FPC
        StartCoroutine(ForceCursorVisibleForFrames(2));
        if (player != null)
        {
            // If SetUIMode(true) means "UI focus" (cursor visible)
            player.SetUIMode(true);
        }
    }

    private void EnterMapMode_Internal()
    {
        // show map, hide scene
        if (mapCanvas != null) mapCanvas.SetActive(true);
        if (mapRoot != null) mapRoot.SetActive(true);
        if (sceneModeRoot != null) sceneModeRoot.SetActive(false);
        if (sceneCanvas != null) sceneCanvas.SetActive(false);

        if (PlayerLocator.instance != null)
        {
            PlayerLocator.instance.liveSyncFromPlayer = false;
            TargetHUDManager.instance?.SyncAllMarkersToTargetPositions();

            PlayerLocator.instance.RestoreUserMarker();
            StartCoroutine(DelayedMarkerSyncToSceneCam());
        }

        // No sonic / no AR HUD when in pure map mode
        if (HUDcanvas != null) HUDcanvas.SetActive(false);
        TargetHUDManager.instance?.SetVisualsEnabled(false);
        AudioManager.Instance?.StopSonic();

        if (sonicStarterPanel != null) sonicStarterPanel.SetActive(false);
        if (sonicTools != null) sonicTools.SetActive(false);
    }

    #endregion

    #region Target Type

    public void SetTargetType(TargetType type)
    {
        SelectedTargetType = type;
        HasChosenTargetType = true;
        HighlightSelectedButton();
    }

    private void HighlightSelectedButton()
    {
        if (stationaryButton == null || dynamicButton == null) return;

        Color selectedColor = Color.cyan;
        Color normalColor = new Color(1, 1, 1, 0.6f); // slightly dim to hint not selected

        bool noneChosen = !HasChosenTargetType;

        var sColors = stationaryButton.colors;
        sColors.normalColor =
            (!noneChosen && SelectedTargetType == TargetType.STATIONARY)
                ? selectedColor
                : normalColor;
        stationaryButton.colors = sColors;

        var dColors = dynamicButton.colors;
        dColors.normalColor =
            (!noneChosen && SelectedTargetType == TargetType.DYNAMIC)
                ? selectedColor
                : normalColor;
        dynamicButton.colors = dColors;
    }

    #endregion

    #region UI helpers

    private IEnumerator ForceCursorVisibleForFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            Cursor.lockState = CursorLockMode.None; // unlock first
            Cursor.visible = true;
        }
    }

    private IEnumerator DelayedMarkerSyncToSceneCam()
    {
        yield return new WaitForEndOfFrame();
        if (PlayerLocator.instance != null)
            PlayerLocator.instance.SyncMarkerToCamera();
    }

    public void ToggleSettingsPanel()
    {
        if (settingsPanel == null) return;
        settingsPanel.SetActive(!settingsPanel.activeInHierarchy);
    }

    #endregion

    #region Sonic / Audio

    public void SetUpdateFrequency(float sliderValue)
    {
        frequencySeconds = sliderValue switch
        {
            1 => 10f,
            2 => 20f,
            3 => 30f,
            _ => 30f
        };

        if (frequencyText != null)
            frequencyText.text = frequencySeconds.ToString() + " seconds";

        AudioManager.Instance?.ApplyFrequency(frequencySeconds);
        AudioCueSlider.instance?.SetInterval(frequencySeconds);
    }

    public void HearNow() // hook this to your button
    {
        AudioManager.Instance?.PlayForActiveTargetNow();
    }

    public void StartSonicHunting()
    {
        if (sonicStarterPanel != null)
            sonicStarterPanel.SetActive(false);

        MissionLoader.Instance?.ActivateMission();
    }

    public void ShowSonicCompletionDialog()
    {
        onFound.Invoke();
    }

    public void ShowMissionCompleteDialog()
    {
        if (endMissionUI != null)
            endMissionUI.SetActive(true);
    }

    #endregion
}
