using System;
using System.Collections.Generic;
using UnityEngine;

public class ActiveTargetManager : MonoBehaviour
{
    public static ActiveTargetManager Instance { get; private set; }

    public event Action<TargetActor> OnActiveTargetChanged;

    private TargetActor _activeTarget;
    public TargetActor ActiveTarget => _activeTarget;
    public bool HasActiveTarget => _activeTarget != null;

    private readonly Dictionary<string, TargetActor> _byId = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Register(TargetActor t)
    {
        if (t == null || string.IsNullOrEmpty(t._ID)) return;
        _byId[t._ID] = t;
    }

    public void Unregister(TargetActor t)
    {
        if (t == null || string.IsNullOrEmpty(t._ID)) return;
        if (_byId.ContainsKey(t._ID)) _byId.Remove(t._ID);
        if (_activeTarget == t) SetActiveTarget(null);
    }

    public void SetActiveTargetById(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (_byId.TryGetValue(id, out var t)) SetActiveTarget(t);
    }

    public void SetActiveTarget(TargetActor t)
    {
        _activeTarget = t;
        OnActiveTargetChanged?.Invoke(_activeTarget);
    }
}
