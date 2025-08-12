using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager instance;

    public GameObject mapCanvas, mapRoot;
    public GameObject sceneCanvas, sceneModeRoot, HUDcanvas, settingsPanel; // Contains SceneCam + HUD + Compass
    public TextMeshProUGUI frequencyText;
    public TargetType SelectedTargetType { get; private set; } = TargetType.STATIONARY;

    [Header("Target Selection Buttons")]
    public Button stationaryButton;
    public Button dynamicButton;

    [Header("Config")]
    public float frequencySeconds = 10f;
    [SerializeField] private Slider frequencySlider;

    [Header("Scene Submode (0=AR, 1=Sonic)")]
    [SerializeField] private int defaultSubmode = 0; // optional: default AR
    private enum SceneSubmode { AR = 0, SONIC = 1 }
    private SceneSubmode submode = SceneSubmode.AR;
    public void SetSonicModeOn() => SetSceneSubmode(1); // OnActive
    public void SetSonicModeOff() => SetSceneSubmode(0); // OnDeactive
    public bool HasChosenTargetType { get; private set; } = false;




    private void Awake()
    {
        instance = this;
        if (frequencySlider != null)
        {
            frequencySlider.minValue = 1;
            frequencySlider.maxValue = 3;
            frequencySlider.wholeNumbers = true;               // keep steps 1,2,3
            frequencySlider.onValueChanged.AddListener(SetUpdateFrequency);
        }
    }

    private void Start()
    {
        stationaryButton.onClick.AddListener(() => SetTargetType(TargetType.STATIONARY));
        dynamicButton.onClick.AddListener(() => SetTargetType(TargetType.DYNAMIC));

        // Visually indicate “no selection yet”
        HighlightSelectedButton();

        // ToastManager.Instance.Show($"Welcome to Sonus, this is our demo web application. A mixture of Augmented Reality and Sonic Targeting, Sonus is built to allow hands free, and headset free target tracking.", 10f, true);

        // Initialize submode
        SetSceneSubmode(defaultSubmode);

    }

    public void SetTargetType(TargetType type)
    {
        SelectedTargetType = type;
        HasChosenTargetType = true;
        HighlightSelectedButton();


    }

    private void HighlightSelectedButton()
    {
        Color selectedColor = Color.cyan;
        Color normalColor = new Color(1, 1, 1, 0.6f); // slightly dim to hint not selected

        // If nothing chosen yet, both look "normal"
        bool noneChosen = !HasChosenTargetType;

        var sColors = stationaryButton.colors;
        sColors.normalColor = (!noneChosen && SelectedTargetType == TargetType.STATIONARY) ? selectedColor : normalColor;
        stationaryButton.colors = sColors;

        var dColors = dynamicButton.colors;
        dColors.normalColor = (!noneChosen && SelectedTargetType == TargetType.DYNAMIC) ? selectedColor : normalColor;
        dynamicButton.colors = dColors;
    }

    public void SetSceneSubmode(int mode) // hook from UI buttons/tabs: AR=0, Sonic=1
    {
        submode = (SceneSubmode)Mathf.Clamp(mode, 0, 1);
        ApplySceneSubmode();
    }

    private void ApplySceneSubmode()
    {
        bool isAR = submode == SceneSubmode.AR;

        // Visual HUD
        if (HUDcanvas != null) HUDcanvas.SetActive(isAR);
        TargetHUDManager.instance?.SetVisualsEnabled(isAR);

        if (isAR)
        {
            AudioManager.Instance?.StopSonic();
        }
        else
        {
            AudioManager.Instance?.StartSonic(frequencySeconds);
        }


    }

    public void EnterSceneMode()
    {
        mapCanvas.SetActive(false);
        mapRoot.SetActive(false);
        sceneModeRoot.SetActive(true);
        sceneCanvas.SetActive(true);

        StartCoroutine(DelayedSceneCameraSync());

        // ensure current submode is applied whenever we enter
        ApplySceneSubmode();
    }

    private IEnumerator DelayedSceneCameraSync()
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        PlayerLocator.instance.SyncCameraToMarker();
    }

    public void EnterMapMode()
    {
        mapCanvas.SetActive(true);
        mapRoot.SetActive(true);
        sceneModeRoot.SetActive(false);
        sceneCanvas.SetActive(false);


        PlayerLocator.instance.RestoreUserMarker();
        StartCoroutine(DelayedMarkerSyncToSceneCam());
    }

    private IEnumerator DelayedMarkerSyncToSceneCam()
    {
        yield return new WaitForEndOfFrame();
        PlayerLocator.instance.SyncMarkerToCamera();
    }

    public void ToggleSettingsPanel()
    {
        settingsPanel.SetActive(!settingsPanel.activeInHierarchy);
    }

    public void SetUpdateFrequency(float sliderValue)
    {
        frequencySeconds = sliderValue switch
        {
            1 => 10f,
            2 => 20f,
            3 => 30f,
            _ => 30f
        };

        frequencyText.text = frequencySeconds.ToString() + " seconds";
        AudioManager.Instance?.ApplyFrequency(frequencySeconds);
        AudioCueSlider.instance.SetInterval(frequencySeconds);
    }



    public void HearNow() // hook this to your button
    {
        AudioManager.Instance.PlayForActiveTargetNow();
    }





}
