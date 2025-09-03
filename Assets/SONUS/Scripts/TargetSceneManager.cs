using System;
using System.Collections.Generic;
using UnityEngine;

public class TargetSceneManager : MonoBehaviour
{
    public static TargetSceneManager Instance { get; private set; }

    public List<TargetActor> ActiveTargets = new();
    public GameObject StaticTarget, DynamicTarget;
    [SerializeField] private Transform targetsParent;


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

    public void ClearAllTargets()
    {
        ActiveTargets.Clear();

    }

    public TargetActor SpawnTarget(Vector2 latLon /* (lat,lon) */, TargetType type, string name = null)
    {
        // ----- 1) Create data model -----
        var actor = new TargetActor(type, latLon.x, latLon.y);
        actor._ID = Guid.NewGuid().ToString("N");
        actor._Name = string.IsNullOrEmpty(name) ? (type == TargetType.STATIONARY ? "Stationary" : "Dynamic") : name;



        // ----- 3) Instantiate the correct prefab -----
        GameObject prefab = type == TargetType.STATIONARY ? StaticTarget : DynamicTarget;
        if (prefab == null)
        {
            Debug.LogError($"[{nameof(TargetSceneManager)}] Missing prefab for {type}. Assign it in the inspector.");
            return actor; // data still exists for map-only usage
        }

        var go = Instantiate(prefab, targetsParent ? targetsParent : null);

        // ----- 4) Bind data -> 3D placement -----
        var proxy = go.GetComponent<TargetProxy>();
        var binder = go.GetComponent<TargetGeoBinder>();

        if (proxy == null || binder == null)
        {
            Debug.LogError($"[{nameof(TargetSceneManager)}] Prefab '{prefab.name}' must have TargetProxy and TargetGeoBinder.");
        }
        else
        {
            proxy.actor = actor;       // this is the key link
            binder.proxy = proxy;      // (safe even if already set on prefab)
            binder.Apply(true);        // snap onto RWT & sync marker once
        }

        // (Optional) keep your own list, events, etc.
        ActiveTargets.Add(actor);

        return actor;
    }
}




