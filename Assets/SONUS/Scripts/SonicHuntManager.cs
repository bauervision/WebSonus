using UnityEngine;
using System.Collections;
using TMPro;
using System.Linq;

public class SonicHuntManager : MonoBehaviour
{
    public static SonicHuntManager Instance;

    [Header("Skybox Materials")]
    public Material defaultSkybox;
    public Material nightSkybox;

    [Header("Scene Elements")]
    public GameObject mapHUD, mapContainer;
    public GameObject sonicHUD, SonicCamera;
    public GameObject SettingsPanel;

    [Header("Audio")]
    public Transform audioSourceParent; // Rotated to point at target
    public AudioSource clueAudioSource;
    public AudioClip directionalClip;
    public AudioClip straightAheadClip;
    public AudioClip directlyBehindClip;
    public AudioClip behindYouClip;

    [Header("Config")]
    public float frequencySeconds = 30f;
    public float headingTolerance = 15f;
    public Transform playerTransform;

    public SonicHUDController hudController;

    [Header("Debug")]
    public TextMeshProUGUI debugText; // <-- Add this for debugging

    private bool huntActive = false;
    private bool isLoopRunning = false;
    private TargetActor activeTarget;
    private Coroutine huntLoop;
    private float clueTimer = 0f;
    private float clueInterval = 30f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Update()
    {
        UpdateDebugText(); // ← Call this every frame
        if (!huntActive || activeTarget == null) return;

        clueTimer += Time.deltaTime;


        if (clueTimer >= clueInterval)
        {
            PlayDirectionalClue();
            clueTimer = 0f;
        }
    }


    public void PlayDirectionalClue()
    {
        if (activeTarget == null || clueAudioSource == null) return;

        Vector3 playerPos = PlayerLocator.instance.SceneCam.transform.position;
        Vector3 targetWorldPos = GeoUtils.GeoToWorld(new Vector2((float)activeTarget._Lat, (float)activeTarget._Lon));
        Vector3 directionToTarget = (targetWorldPos - playerPos).normalized;

        float playerYaw = PlayerLocator.instance.SceneCam.transform.eulerAngles.y;
        float headingToTarget = Quaternion.LookRotation(directionToTarget).eulerAngles.y;
        float headingDiff = Mathf.DeltaAngle(playerYaw, headingToTarget);

        // Rotate the clue audio source to simulate direction
        clueAudioSource.transform.parent.rotation = Quaternion.Euler(0, headingToTarget, 0);

        // Play primary directional clip
        clueAudioSource.Play();

        // Queue additional cue if needed
        if (Mathf.Abs(headingDiff) < 20f)
        {
            clueAudioSource.PlayOneShot(straightAheadClip);
        }
        else if (Mathf.Abs(headingDiff) > 160f)
        {
            clueAudioSource.PlayOneShot(directlyBehindClip);
        }
        else if (Mathf.Abs(headingDiff) > 90f)
        {
            clueAudioSource.PlayOneShot(behindYouClip);
        }
    }


    public void StartSonicHunt()
    {
        huntActive = true;
        isLoopRunning = false;

        RenderSettings.skybox = nightSkybox;
        DynamicGI.UpdateEnvironment();

        if (mapHUD != null) mapHUD.SetActive(false);
        if (mapContainer != null) mapContainer.SetActive(false);
        if (sonicHUD != null) sonicHUD.SetActive(true);
        if (SonicCamera != null) SonicCamera.SetActive(true);

        // Auto-select first target if none is active
        if (activeTarget == null)
        {
            if (TargetSceneManager.Instance.ActiveTargets.Count == 1)
            {
                activeTarget = TargetSceneManager.Instance.ActiveTargets[0];
                Debug.Log("[SONIC] Automatically selected the only available target.");
            }
            else if (TargetSceneManager.Instance.ActiveTargets.Count > 1)
            {
                Debug.LogWarning("[SONIC] Multiple targets found but none selected. Use SetActiveTarget.");
            }
            else
            {
                // SPAWN demo target at a fixed offset
                Vector3 playerPos = playerTransform.position;
                Vector3 forward = playerTransform.forward;
                Vector3 demoWorldPos = playerPos + forward * 100f; // 100m ahead
                Vector2 demoLatLon = GeoUtils.WorldToGeo(demoWorldPos);

                TargetActor demoTarget = TargetSceneManager.Instance.SpawnTarget(demoLatLon, TargetType.STATIONARY, skipMarker: true);
                SetActiveTarget(demoTarget);

                Debug.Log("[SONIC] No targets found — demo target spawned.");
            }
        }

        // Begin loop if target now exists
        if (activeTarget != null)
        {
            StartHuntLoop();
        }
    }

    public void EndSonicHunt()
    {
        Debug.Log("[SONIC] Hunt ended");
        huntActive = false;
        isLoopRunning = false;

        RenderSettings.skybox = defaultSkybox;
        DynamicGI.UpdateEnvironment();

        if (mapHUD != null) mapHUD.SetActive(true);
        if (mapContainer != null) mapContainer.SetActive(true);
        if (sonicHUD != null) sonicHUD.SetActive(false);
        if (SonicCamera != null) SonicCamera.SetActive(false);

        if (huntLoop != null) StopCoroutine(huntLoop);
    }

    public bool IsHuntActive() => huntActive;

    public void ToggleSettings() => SettingsPanel.SetActive(!SettingsPanel.activeInHierarchy);

    public void UpdateTargetCount(int count)
    {
        hudController?.SetTargetCount(count);
    }

    public void SetUpdateFrequency(int sliderValue)
    {
        frequencySeconds = sliderValue switch
        {
            1 => 30f,
            2 => 60f,
            3 => 90f,
            _ => 30f
        };
        Debug.Log($"[SONIC] Frequency set to {frequencySeconds}s");
    }

    public void SetActiveTarget(TargetActor target)
    {
        activeTarget = target;
        if (huntActive && !isLoopRunning) StartHuntLoop();
    }

    private void StartHuntLoop()
    {
        if (huntLoop != null) StopCoroutine(huntLoop);
        huntLoop = StartCoroutine(HuntClueLoop());
        isLoopRunning = true;
    }

    public void HearNow()
    {
        PlayClue();
        RestartLoop();
    }

    private void RestartLoop()
    {
        if (huntLoop != null) StopCoroutine(huntLoop);
        huntLoop = StartCoroutine(HuntClueLoop());
    }

    private IEnumerator HuntClueLoop()
    {
        while (huntActive && activeTarget != null)
        {
            yield return new WaitForSeconds(frequencySeconds);
            PlayClue();
        }
    }

    private void PlayClue()
    {
        if (activeTarget == null || clueAudioSource == null || audioSourceParent == null) return;

        Vector3 playerPos = playerTransform.position;
        Vector2 geoPos = new Vector2((float)activeTarget._Lat, (float)activeTarget._Lon);
        Vector3 targetWorldPos = GeoUtils.GeoToWorld(geoPos);

        Vector3 directionToTarget = targetWorldPos - playerPos;
        Vector3 flatDirection = new Vector3(directionToTarget.x, 0, directionToTarget.z).normalized;

        if (flatDirection.sqrMagnitude < 0.01f) return;

        // Point parent in that direction
        audioSourceParent.rotation = Quaternion.LookRotation(flatDirection, Vector3.up);

        float angleToTarget = Vector3.SignedAngle(playerTransform.forward, flatDirection, Vector3.up);
        float absAngle = Mathf.Abs(angleToTarget);

        // Main directional cue
        clueAudioSource.PlayOneShot(directionalClip);

        // Add context clip
        if (absAngle <= headingTolerance)
        {
            clueAudioSource.PlayOneShot(straightAheadClip);
            RestartLoop(); // Reset timer since player is heading right way
        }
        else if (absAngle >= 170f)
        {
            clueAudioSource.PlayOneShot(directlyBehindClip);
        }
        else if (absAngle > 90f)
        {
            clueAudioSource.PlayOneShot(behindYouClip);
        }

        Debug.Log($"[SONIC] Angle to target: {angleToTarget:F1}°");
    }

    private void UpdateDebugText()
    {
        if (debugText == null) return;

        // Fallback messages for inactive states
        if (!huntActive)
        {
            debugText.text = "[DEBUG] Hunt not active.";
            return;
        }

        if (activeTarget == null)
        {
            debugText.text = "[DEBUG] No active target.";
            return;
        }

        // Real-time heading info
        Vector3 playerPos = PlayerLocator.instance.SceneCam.transform.position;
        Vector3 targetWorldPos = GeoUtils.GeoToWorld(new Vector2((float)activeTarget._Lat, (float)activeTarget._Lon));
        Vector3 directionToTarget = (targetWorldPos - playerPos).normalized;

        float playerYaw = PlayerLocator.instance.SceneCam.transform.eulerAngles.y;
        float headingToTarget = Quaternion.LookRotation(directionToTarget).eulerAngles.y;
        float headingDiff = Mathf.DeltaAngle(playerYaw, headingToTarget);

        string guidance = "None";

        if (Mathf.Abs(headingDiff) <= headingTolerance)
        {
            guidance = "Straight Ahead";
        }
        else if (Mathf.Abs(headingDiff) > 160f)
        {
            guidance = "Directly Behind";
        }
        else if (Mathf.Abs(headingDiff) > 90f)
        {
            guidance = "Behind You";
        }

        debugText.text = $"[DEBUG]\n" +
                         $"Player Heading: {playerYaw:F1}°\n" +
                         $"Target Heading: {headingToTarget:F1}°\n" +
                         $"Diff: {headingDiff:F1}°\n" +
                         $"Guidance: {guidance}";
    }



}
