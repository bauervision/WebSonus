using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Octo.Surge.Sonus
{
    [System.Serializable]
    public class TargetRange
    {
        public AudioClip _10m;
        public AudioClip _20m;
        public AudioClip _30m;
        public AudioClip _40m;
        public AudioClip _50m;
        public AudioClip _60m;
        public AudioClip _70m;
        public AudioClip _80m;
        public AudioClip _90m;
        public AudioClip _100m;
        public AudioClip _125m;
        public AudioClip _150m;
        public AudioClip _175m;
        public AudioClip _200m;
        public AudioClip _250m;
        public AudioClip _300m;
        public AudioClip _350m;
        public AudioClip _400m;
        public AudioClip _400Greater;
        public AudioClip _500Greater;
        public AudioClip _1000Greater;
    }

    [System.Serializable]
    public class TargetType
    {
        public AudioClip _stationary;
        public AudioClip _dynamic;


    }

    [System.Serializable]
    public class TargetMoving
    {
        public AudioClip __north, _northEast, _east, _southEast, _south, _southWest, _west, _northWest;

    }


    [System.Serializable]
    public class SONUS
    {
        public TargetRange targetRange;
        public TargetType targetType;
        public TargetMoving targetMoving;
        public AudioClip _behindYou, _straightAhead, _directlyBehind;
        public AudioClip _newStationary, _newDynamic;
        public AudioClip _noTargets;
        public AudioClip _north1;

    }


}