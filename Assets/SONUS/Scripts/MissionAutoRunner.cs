using System.Collections;
using UnityEngine;

public class MissionAutoRunner : MonoBehaviour
{
    [Tooltip("Mission index in MissionLoader.missions to run on scene start.")]
    [SerializeField] private int missionIndex = 0;

    [Tooltip("Automatically select + activate the mission on Start().")]
    [SerializeField] private bool runOnStart = true;

    [SerializeField] private bool logDebug = true;

    private IEnumerator Start()
    {
        if (!runOnStart) yield break;

        // Wait a frame so MissionLoader.Awake() has time to register Instance
        yield return null;

        var loader = MissionLoader.Instance ?? FindFirstObjectByType<MissionLoader>();
        if (loader == null)
        {
            Debug.LogError("[MissionAutoRunner] No MissionLoader found in scene.");
            yield break;
        }

        if (loader.missions == null || loader.missions.Count == 0)
        {
            Debug.LogError("[MissionAutoRunner] MissionLoader has no missions configured.");
            yield break;
        }

        if (missionIndex < 0 || missionIndex >= loader.missions.Count)
        {
            Debug.LogWarning($"[MissionAutoRunner] missionIndex {missionIndex} out of range. Clamping to 0.");
            missionIndex = 0;
        }

        // Stage mission
        loader.SelectMissionByIndex(missionIndex);

        if (logDebug)
        {
            Debug.Log($"[MissionAutoRunner] Selected mission: {loader.ActiveMissionName}");
        }

        // Activate mission (starts coroutine internally)
        loader.ActivateMission();

        if (logDebug)
        {
            Debug.Log("[MissionAutoRunner] ActivateMission() called.");
        }
    }
}
