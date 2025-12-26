using UnityEngine;

public class TargetProxy : MonoBehaviour
{
    [Header("Runtime actor model (not a Component)")]
    public TargetActor actor;

    [Header("Defaults used if actor is missing")]
    public TargetType defaultType = TargetType.STATIONARY; // pick whatever maps best
    public string defaultId = "";
    public string defaultName = "";

    private void Awake()
    {
        // Ensure we always have an actor model so MissionLoader can proceed
        if (actor == null)
        {
            // We don't know lat/lon yet here; let something else set it (or keep 0,0 for now)
            actor = new TargetActor(defaultType, 0, 0);

            if (!string.IsNullOrEmpty(defaultId)) actor._ID = defaultId;
            if (!string.IsNullOrEmpty(defaultName)) actor._Name = defaultName;
        }
    }
}
