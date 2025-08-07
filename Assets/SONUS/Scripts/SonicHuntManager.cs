using UnityEngine;

public class SonicHuntManager : MonoBehaviour
{
    public static SonicHuntManager Instance;

    [Header("Skybox Materials")]
    public Material defaultSkybox;
    public Material nightSkybox;

    [Header("Scene Elements")]
    public GameObject mapHUD, mapContainer;        // Compass, indicators, etc.
    public GameObject sonicHUD, SonicCamera;        // The simplified hunt HUD
    public GameObject SettingsPanel;   // Optional: disable or dim lights for darkness

    public SonicHUDController hudController;
    private bool huntActive = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void StartSonicHunt()
    {
        Debug.Log("[SONIC] Hunt started");
        huntActive = true;

        RenderSettings.skybox = nightSkybox;
        DynamicGI.UpdateEnvironment();


        if (mapHUD != null) mapHUD.SetActive(false);
        if (mapContainer != null) mapContainer.SetActive(false);
        if (sonicHUD != null) sonicHUD.SetActive(true);
        if (SonicCamera != null) SonicCamera.SetActive(true);

        // TODO: Start audio loop, enable movement/controls, etc.
    }

    public void EndSonicHunt()
    {
        Debug.Log("[SONIC] Hunt ended");
        huntActive = false;

        RenderSettings.skybox = defaultSkybox;
        DynamicGI.UpdateEnvironment();


        if (mapHUD != null) mapHUD.SetActive(true);
        if (mapContainer != null) mapContainer.SetActive(true);
        if (sonicHUD != null) sonicHUD.SetActive(false);
        if (SonicCamera != null) SonicCamera.SetActive(false);

        // TODO: Stop audio loop, restore full UI
    }

    public bool IsHuntActive()
    {
        return huntActive;
    }

    public void ToggleSettings() { SettingsPanel.SetActive(!SettingsPanel.activeInHierarchy); }

    public void UpdateTargetCount(int count)
    {
        if (hudController != null)
            hudController.SetTargetCount(count);
    }

}
