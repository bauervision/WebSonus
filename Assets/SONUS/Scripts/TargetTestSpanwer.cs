using UnityEngine;

public class TestTargetSpawner : MonoBehaviour
{
    public TargetHUDManager hudManager;
    public Camera sceneCamera;

    private TargetActor testTarget;

    // Spawn test target ~100m northeast of PlayerLocator
    void Start()
    {
        var origin = FindFirstObjectByType<PlayerLocator>();
        if (origin == null)
        {
            Debug.LogWarning("No PlayerLocator found.");
            return;
        }


        double testLat = origin.latitude + (100.0 / 110540.0);
        double testLon = origin.longitude;


        testTarget = new TargetActor(TargetType.STATIONARY, testLat, testLon)
        {
            _ID = "TestTarget"
        };
    }

    void Update()
    {
        if (testTarget != null)
        {
            hudManager.UpdateTargetUI(testTarget);
        }
    }
}
