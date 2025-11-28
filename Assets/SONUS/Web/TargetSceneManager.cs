using System;
using System.Collections.Generic;
using UnityEngine;

public class TargetSceneManager : MonoBehaviour
{
    public static TargetSceneManager Instance { get; private set; }

    [Header("Prefabs + Parent")]
    public GameObject StaticTarget;
    public GameObject DynamicTarget;
    [SerializeField] private Transform targetsParent;

    [Header("Active Targets (data only)")]
    public List<TargetActor> ActiveTargets = new();

    // Runtime indices
    private readonly Dictionary<string, TargetProxy> _proxies = new(); // _ID -> scene proxy
    private readonly Dictionary<string, TargetActor> _actors = new(); // _ID -> data

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[Singleton] Duplicate {nameof(TargetSceneManager)} destroyed on {name}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        var hud = TargetHUDManager.instance;
        if (hud == null) return;

        hud.ClearGroupingCache();
        for (int i = 0; i < ActiveTargets.Count; i++)
            hud.UpdateTargetUI(ActiveTargets[i]);
    }

    // -------------------- Public API --------------------

    /// <summary>Create a brand new actor at latLon and spawn its scene object (single path).</summary>
    public TargetActor SpawnTarget(Vector2 latLon /* (lat,lon) */, TargetType type, string name = null)
    {
        var actor = new TargetActor(type, latLon.x, latLon.y)
        {
            _ID = Guid.NewGuid().ToString("N"),
            _Name = string.IsNullOrEmpty(name)
                        ? (type == TargetType.STATIONARY ? "Stationary" : "Dynamic")
                        : name
        };

        return RegisterTarget(actor);
    }

    /// <summary>Register an existing actor (from map click, server, etc.). Idempotent by _ID.</summary>
    public TargetActor RegisterTarget(TargetActor actor, Texture2D iconOverride = null)
    {
        if (actor == null) return null;
        if (string.IsNullOrEmpty(actor._ID)) actor._ID = Guid.NewGuid().ToString("N");

        // Already registered? return the same one (no double spawn)
        if (_actors.ContainsKey(actor._ID))
        {
            // Make sure marker exists; let binder keep the GO in sync.
            EnsureMarker(actor, iconOverride);
            return _actors[actor._ID];
        }

        // 1) Store data & list (avoid duplicates)
        _actors[actor._ID] = actor;
        if (!ActiveTargets.Exists(t => t._ID == actor._ID))
            ActiveTargets.Add(actor);

        // 2) Ensure a single map marker
        EnsureMarker(actor, iconOverride);

        // 3) Instantiate exactly one scene object under the parent
        var prefab = ((TargetType)actor._Type == TargetType.DYNAMIC) ? DynamicTarget : StaticTarget;
        if (prefab == null)
        {
            Debug.LogError($"[{nameof(TargetSceneManager)}] Missing prefab for {(TargetType)actor._Type}. Assign it in the inspector.");
            return actor; // actor still tracked for map usage
        }

        var go = Instantiate(prefab, targetsParent ? targetsParent : null);
        if (targetsParent != null) go.transform.SetParent(targetsParent, false);

        var proxy = go.GetComponent<TargetProxy>();
        var binder = go.GetComponent<TargetGeoBinder>();

        if (proxy == null || binder == null)
        {
            Debug.LogError($"[{nameof(TargetSceneManager)}] Prefab '{prefab.name}' must have TargetProxy and TargetGeoBinder.");
        }
        else
        {
            proxy.actor = actor;   // key link
            binder.proxy = proxy;  // safe even if set on prefab
            binder.Apply(true);    // snap once to (lat,lon)
        }

        _proxies[actor._ID] = proxy;

        return actor;
    }

    public TargetProxy GetProxy(string id) => id != null && _proxies.TryGetValue(id, out var p) ? p : null;
    public TargetActor GetActor(string id) => id != null && _actors.TryGetValue(id, out var a) ? a : null;
    public TargetActor GetTargetById(string id) => ActiveTargets.Find(t => t._ID == id);

    /// <summary>Destroy all spawned targets + markers and clear registries.</summary>
    public void ClearAllTargets()
    {
        // Destroy spawned gameobjects
        foreach (var kv in _proxies)
        {
            if (kv.Value != null) Destroy(kv.Value.gameObject);
        }
        _proxies.Clear();

        // Remove ONLY target markers (keep user marker)
        var mm = OnlineMapsMarkerManager.instance;
        if (mm != null)
        {
            var items = mm.items;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i]["data"] is TargetActor) OnlineMapsMarkerManager.RemoveItem(items[i]);
            }
        }

        _actors.Clear();
        ActiveTargets.Clear();
    }

    // -------------------- Internals --------------------

    private OnlineMapsMarker EnsureMarker(TargetActor actor, Texture2D icon = null)
    {
        var existing = actor.GetMarker();
        if (existing != null) return existing;

        var tex = icon ?? AddTargetOnClick.GetIconForType((TargetType)actor._Type);
        var m = OnlineMapsMarkerManager.CreateItem(actor._Lon, actor._Lat, tex);
        m.align = OnlineMapsAlign.Center;
        m.scale = 0.4f;
        m.label = $"Target: {actor._Name}";
        m["data"] = actor;
        return m;
    }
}
