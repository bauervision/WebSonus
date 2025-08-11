using System;
using UnityEngine;

public class MissionSession : MonoBehaviour
{
    public static MissionSession Instance { get; private set; }

    public bool IsLoaded { get; private set; }
    public string MissionName { get; private set; }

    public event Action OnLoaded;
    public event Action OnCleared;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void MarkLoaded(string missionName)
    {
        IsLoaded = true;
        MissionName = missionName;
        OnLoaded?.Invoke();
    }

    public void Clear()
    {
        IsLoaded = false;
        MissionName = null;
        OnCleared?.Invoke();
    }
}
