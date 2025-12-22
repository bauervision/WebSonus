
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Mission
{
    [Tooltip("Display name / identifier for this mission")]
    public string name;

    [Tooltip("Anchors that belong to this mission, in the order you want them encountered.")]
    public List<MissionAnchor> anchors = new();

    [Header("Flow")]
    [Tooltip("Pick a random first target when the mission starts.")]
    public bool randomizeFirst = true;

    [Tooltip("When the user hits \"Try Another\", should we randomize the next pick?")]
    public bool selectRandomOnTryAnother = true;
}