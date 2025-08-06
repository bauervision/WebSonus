using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager instance;

    public TargetType SelectedTargetType { get; private set; } = TargetType.STATIONARY;

    [Header("Target Selection Buttons")]
    public Button stationaryButton;
    public Button dynamicButton;

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        stationaryButton.onClick.AddListener(() => SetTargetType(TargetType.STATIONARY));
        dynamicButton.onClick.AddListener(() => SetTargetType(TargetType.DYNAMIC));
    }

    public void SetTargetType(TargetType type)
    {
        SelectedTargetType = type;
        // Optional: highlight selected button
        HighlightSelectedButton();
    }

    private void HighlightSelectedButton()
    {
        Color selectedColor = Color.cyan;
        Color normalColor = Color.white;

        var colors = stationaryButton.colors;
        colors.normalColor = SelectedTargetType == TargetType.STATIONARY ? selectedColor : normalColor;
        stationaryButton.colors = colors;

        colors = dynamicButton.colors;
        colors.normalColor = SelectedTargetType == TargetType.DYNAMIC ? selectedColor : normalColor;
        dynamicButton.colors = colors;
    }
}
