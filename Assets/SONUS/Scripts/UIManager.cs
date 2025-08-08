using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager instance;

    public GameObject mapCanvas, mapRoot;
    public GameObject sceneCanvas, sceneModeRoot; // Contains SceneCam + HUD + Compass

    public TargetType SelectedTargetType { get; private set; } = TargetType.STATIONARY;

    [Header("Target Selection Buttons")]
    public Button stationaryButton;
    public Button dynamicButton;

    public bool HasChosenTargetType { get; private set; } = false;

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        stationaryButton.onClick.AddListener(() => SetTargetType(TargetType.STATIONARY));
        dynamicButton.onClick.AddListener(() => SetTargetType(TargetType.DYNAMIC));

        // Visually indicate “no selection yet”
        HighlightSelectedButton();

        ToastManager.Instance.Show($"Welcome to SONUS! Add targets, enter Scene view and track them, or play SONUS Hunt where you can tracking targets only by listening to them!", 10f, true);
    }

    public void SetTargetType(TargetType type)
    {
        SelectedTargetType = type;
        HasChosenTargetType = true;
        HighlightSelectedButton();

        ToastManager.Instance.Show($"Active target set!", 1.8f, false);
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

    public void EnterSceneMode()
    {
        mapCanvas.SetActive(false);
        mapRoot.SetActive(false);
        sceneModeRoot.SetActive(true);
        sceneCanvas.SetActive(true);

        // 3. Wait one frame before syncing rotation
        StartCoroutine(DelayedSceneCameraSync());
    }

    private IEnumerator DelayedSceneCameraSync()
    {
        yield return null; // wait for Online Maps input to fire
        yield return new WaitForEndOfFrame(); // wait for Online Maps rotation to apply

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

}
