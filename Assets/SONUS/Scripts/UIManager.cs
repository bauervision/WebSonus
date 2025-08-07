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


    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        stationaryButton.onClick.AddListener(() => SetTargetType(TargetType.STATIONARY));
        dynamicButton.onClick.AddListener(() => SetTargetType(TargetType.DYNAMIC));
    }

    public void SetTargetType(TargetType type)
    {
        SelectedTargetType = type;
        // Optional: highlight selected button
        HighlightSelectedButton();
    }

    private void HighlightSelectedButton()
    {
        Color selectedColor = Color.cyan;
        Color normalColor = Color.white;

        var colors = stationaryButton.colors;
        colors.normalColor = SelectedTargetType == TargetType.STATIONARY ? selectedColor : normalColor;
        stationaryButton.colors = colors;

        colors = dynamicButton.colors;
        colors.normalColor = SelectedTargetType == TargetType.DYNAMIC ? selectedColor : normalColor;
        dynamicButton.colors = colors;
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
