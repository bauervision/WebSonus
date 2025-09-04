using UnityEngine;

public class SonicTargetTracker : MonoBehaviour
{
    [Header("Refs")]
    public Transform player;            // usually PlayerLocator.playerRoot or your FPC
    public Camera sceneCamera;          // used for yaw
    public float arrivedRadiusMeters = 5f;
    public float sampleHz = 10f;        // update rate for panning

    float _nextSample;

    void Reset()
    {
        sceneCamera = Camera.main;
        if (player == null)
        {
            var fpc = FindFirstObjectByType<FirstPersonController>();
            if (fpc) player = fpc.transform;
        }
    }

    void Update()
    {
        if (Time.unscaledTime < _nextSample) return;
        _nextSample = Time.unscaledTime + 1f / Mathf.Max(1f, sampleHz);

        var active = ActiveTargetManager.Instance?.ActiveTarget;
        if (active == null || player == null || sceneCamera == null) return;

        // Find the spawned 3D proxy for this actor (your prefabs include it)
        var proxy = FindProxy(active._ID);
        if (proxy == null) return;

        Vector3 playerPos = player.position;
        Vector3 targetPos = proxy.transform.position;

        // Flatten for “distance to arrival” & panning consistency
        Vector3 flatPlayer = playerPos; flatPlayer.y = 0f;
        Vector3 flatTarget = targetPos; flatTarget.y = 0f;

        bool arrived = Vector3.Distance(flatPlayer, flatTarget) <= arrivedRadiusMeters;

        // Feed AudioManager: this just keeps audioHeading pointing at the target
        AudioManager.Instance?.UpdateGuidanceWorld(playerPos, targetPos, arrived);
    }

    TargetProxy FindProxy(string id)
    {
        foreach (var p in FindObjectsOfType<TargetProxy>())
            if (p && p.actor != null && p.actor._ID == id) return p;
        return null;
    }
}
