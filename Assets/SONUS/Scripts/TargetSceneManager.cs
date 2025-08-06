using System.Collections.Generic;
using UnityEngine;

public class TargetSceneManager : MonoBehaviour
{
    public static TargetSceneManager Instance { get; private set; }

    public List<TargetActor> ActiveTargets = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        Instance = this;
    }

    void Update()
    {
        if (TargetHUDManager.instance == null) return;

        TargetHUDManager.instance.ClearGroupingCache();
        foreach (TargetActor target in ActiveTargets)
            TargetHUDManager.instance.UpdateTargetUI(target);
    }

    public void RegisterTarget(TargetActor target)
    {
        if (!ActiveTargets.Exists(t => t._ID == target._ID))
            ActiveTargets.Add(target);
    }

    public TargetActor GetTargetById(string id)
    {
        return ActiveTargets.Find(t => t._ID == id);
    }
}
