using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimal, demo-focused UI manager for Sonus:
/// - User starts a mission
/// - Per-target "Target found" dialog
/// - Mission complete dialog + return to map
/// </summary>
public class SonusUIManager : MonoBehaviour
{
    public static SonusUIManager Instance { get; private set; }

    [Header("Scene roots")]
    [Tooltip("Root for the 2D map view (can be a canvas or world-space root).")]
    [SerializeField] private GameObject mapRoot;

    [Tooltip("Root for the 3D scene view (terrain + player + anchors).")]
    [SerializeField] private GameObject sceneRoot;

    [Header("Panels")]
    [Tooltip("Shown at the start, lets the user begin the sonic hunt.")]
    [SerializeField] private GameObject startPanel;

    [Tooltip("Shown when a target is found but mission is not complete.")]
    [SerializeField] private GameObject targetFoundPanel;

    [Tooltip("Shown when all targets in the mission are complete.")]
    [SerializeField] private GameObject missionCompletePanel;

    [Header("Core references")]
    [SerializeField] private MissionLoader missionLoader;
    [SerializeField] private FirstPersonController player;

    [Header("Mission selection")]
    [Tooltip("Which mission index in MissionLoader.missions to run.")]
    [SerializeField] private int defaultMissionIndex = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // 🔁 Start directly in scene mode for the demo
        EnterSceneMode();

        // Lock player into UI mode until they hit Start
        if (player) player.SetUIMode(true);

        // Show only the Start panel
        ShowPanel(startPanel);
    }


    // ─────────────────────────────────────────────────────────────────────────────
    // Public UI hooks (wire from Buttons)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>UI: Called by the Start button to begin the mission.</summary>
    public void UI_BeginMission()
    {
        if (!missionLoader)
        {
            Debug.LogError("[SonusUIManager] No MissionLoader assigned.");
            return;
        }

        // Switch into the 3D scene view
        EnterSceneMode();

        // Stage + start mission
        missionLoader.SelectMissionByIndex(defaultMissionIndex);
        missionLoader.ActivateMission();

        // Hide all panels while hunting
        HideAllPanels();

        // Put player into play mode
        if (player) player.SetUIMode(false);

        Debug.Log("[SonusUIManager] Mission started.");
    }

    /// <summary>Called by MissionAnchor when a target is reached (but mission is not complete).</summary>
    public void OnTargetArrived()
    {
        // Pause gameplay and show the per-target dialog
        if (player) player.SetUIMode(true);

        ShowPanel(targetFoundPanel);
        Debug.Log("[SonusUIManager] Target arrived – showing targetFoundPanel.");
    }

    /// <summary>UI: Try another target in the same mission.</summary>
    public void UI_TryAnotherTarget()
    {
        if (!missionLoader)
        {
            Debug.LogError("[SonusUIManager] UI_TryAnotherTarget: no MissionLoader.");
            return;
        }

        HideAllPanels();

        // Let MissionLoader pick another, then resume play
        missionLoader.UI_TryAnotherAndResume();

        // Back to play mode
        if (player) player.SetUIMode(false);
    }

    /// <summary>Called by MissionLoader when all anchors are complete.</summary>
    public void OnMissionComplete()
    {
        // Pause gameplay, show completion panel
        if (player) player.SetUIMode(true);

        ShowPanel(missionCompletePanel);
        Debug.Log("[SonusUIManager] Mission complete – showing missionCompletePanel.");
    }

    /// <summary>UI: User chooses to end the mission and go back to the map.</summary>
    public void UI_EndMissionAndReturnToMap()
    {
        if (missionLoader)
        {
            missionLoader.EndMission();
        }

        // Back to map view for the demo
        EnterMapMode();

        // Reset panels so user can start again if needed
        HideAllPanels();
        ShowPanel(startPanel);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // View switching helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private void EnterMapMode()
    {
        if (mapRoot) mapRoot.SetActive(true);
        if (sceneRoot) sceneRoot.SetActive(false);
    }

    private void EnterSceneMode()
    {
        if (mapRoot) mapRoot.SetActive(false);
        if (sceneRoot) sceneRoot.SetActive(true);
    }

    private void HideAllPanels()
    {
        if (startPanel) startPanel.SetActive(false);
        if (targetFoundPanel) targetFoundPanel.SetActive(false);
        if (missionCompletePanel) missionCompletePanel.SetActive(false);
    }

    private void ShowPanel(GameObject panel)
    {
        HideAllPanels();
        if (panel) panel.SetActive(true);
    }
}
