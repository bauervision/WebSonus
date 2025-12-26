using System;
using System.Collections.Generic;

[Serializable]
public class MissionDef
{
    public string name = "Mission";
    public bool randomizeFirst = true;
    public bool selectRandomOnTryAnother = true;
    public List<TargetDef> targets = new();
}

[Serializable]
public class TargetDef
{
    public string id = "T1";
    public MissionTargetType targetType = MissionTargetType.Stationary;
    public double lat;
    public double lon;
}
