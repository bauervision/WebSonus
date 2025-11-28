using UnityEngine;
using TMPro;

public class HUDActiveTargetLabel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;

    private void OnEnable()
    {
        if (ActiveTargetManager.Instance != null)
            ActiveTargetManager.Instance.OnActiveTargetChanged += HandleChange;

        // Initialize on show
        var t = ActiveTargetManager.Instance?.ActiveTarget;
        SetName(t != null ? t._Name : "None");
    }

    private void OnDisable()
    {
        if (ActiveTargetManager.Instance != null)
            ActiveTargetManager.Instance.OnActiveTargetChanged -= HandleChange;
    }

    public void SetName(string n)
    {
        if (label != null) label.text = $"Active Target: {n}";
    }

    private void HandleChange(TargetActor t)
    {
        SetName(t != null ? t._Name : "None");
    }
}
