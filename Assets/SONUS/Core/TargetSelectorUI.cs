
using UnityEngine;

public class TargetSelectorUI : MonoBehaviour
{
    public static TargetType SelectedType = TargetType.STATIONARY;

    public void SelectStationary() => SelectedType = TargetType.STATIONARY;
    public void SelectDynamic() => SelectedType = TargetType.DYNAMIC;
}