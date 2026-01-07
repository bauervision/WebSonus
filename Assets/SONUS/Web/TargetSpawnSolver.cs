// Assets/SONUS/Web/TargetSpawnSolver.cs
using System.Collections;
using UnityEngine;
using OnlineMaps;

public class TargetSpawnSolver
{
    public struct Params
    {
        // Distance in METERS (geo meters)
        public float desiredMeters;
        public float jitterMeters;
        public float minMeters;
        public float maxMeters;

        // Bounds constraint (map view)
        public bool constrainToMapView;

        // Direction selection
        public int bearingSamples;              // e.g., 16 or 24
        public bool avoidRepeatingBearingBucket; // don't pick same "direction" twice
    }

    private readonly System.Action<string> _warn;
    private readonly System.Action<string> _log;
    private readonly System.Func<bool> _debugOn;

    // Remember last chosen bearing bucket so we don't keep drifting out
    private int _lastBearingBucket = -1;

    public TargetSpawnSolver(
        System.Action<string> warn,
        System.Action<string> log,
        System.Func<bool> debugOn
    )
    {
        _warn = warn;
        _log = log;
        _debugOn = debugOn;
    }

    /// <summary>
    /// Picks a new target near baseLat/baseLon using geo meters, optionally constrained to the current 2D map view bounds.
    /// </summary>
    public IEnumerator SpawnTargetGeoSolved(
        Map map2D,
        double baseLat,
        double baseLon,
        Params p,
        System.Action<double, double> commitTargetLatLon
    )
    {
        if (commitTargetLatLon == null) yield break;

        float desired = Mathf.Clamp(
            Mathf.Max(p.minMeters, p.desiredMeters),
            p.minMeters,
            Mathf.Max(p.maxMeters, p.minMeters + 0.01f)
        );

        float jitter = Mathf.Abs(p.jitterMeters);

        // Determine bounds in lon/lat
        bool hasBounds = false;
        double leftLon = 0, rightLon = 0, topLat = 0, botLat = 0;

        if (p.constrainToMapView && map2D != null)
        {
            // OnlineMaps uses GeoPoint: x=lng, y=lat
            GeoPoint tl = map2D.view.topLeft;
            GeoPoint br = map2D.view.bottomRight;

            // Normalize (some systems might flip)
            leftLon = System.Math.Min(tl.x, br.x);
            rightLon = System.Math.Max(tl.x, br.x);
            topLat = System.Math.Max(tl.y, br.y);
            botLat = System.Math.Min(tl.y, br.y);

            hasBounds = true;
        }

        int samples = Mathf.Clamp(p.bearingSamples, 8, 64);

        // Build candidate bearings (uniform around circle, with random phase)
        double phase = Random.value * System.Math.PI * 2.0;

        // Try multiple passes: first try respecting "avoid same direction",
        // then relax if nothing fits.
        for (int pass = 0; pass < 2; pass++)
        {
            bool enforceAvoid = (pass == 0) && p.avoidRepeatingBearingBucket;

            for (int i = 0; i < samples; i++)
            {
                double t = phase + (i * (System.Math.PI * 2.0 / samples));
                int bucket = BearingBucket(t, samples);

                if (enforceAvoid && _lastBearingBucket >= 0 && bucket == _lastBearingBucket)
                    continue;

                float trialMeters = Mathf.Clamp(desired + Random.Range(-jitter, jitter), p.minMeters, p.maxMeters);

                (double tLat, double tLon) = TargetGeoUtil.GeoOffsetByBearing(baseLat, baseLon, trialMeters, t);

                if (hasBounds)
                {
                    if (!IsInsideBounds(tLat, tLon, leftLon, rightLon, topLat, botLat))
                        continue;
                }

                if (_debugOn())
                {
                    _log?.Invoke($"[TargetHunt] GeoSolved pass={pass} i={i} meters={trialMeters:F1} bearingDeg={(t * 180.0 / System.Math.PI):F0} bucket={bucket} -> ({tLat:F6},{tLon:F6})");
                }

                _lastBearingBucket = bucket;
                commitTargetLatLon(tLat, tLon);
                yield break;
            }
        }

        // If we got here, we couldn't find an in-bounds point (or no bounds).
        // Fallback: just pick any bearing.
        {
            double t = Random.value * System.Math.PI * 2.0;
            float m = Mathf.Clamp(desired, p.minMeters, p.maxMeters);
            (double tLat, double tLon) = TargetGeoUtil.GeoOffsetByBearing(baseLat, baseLon, m, t);

            if (_debugOn())
                _warn?.Invoke("[TargetHunt] GeoSolved fallback used (no in-bounds candidate found).");

            _lastBearingBucket = BearingBucket(t, samples);
            commitTargetLatLon(tLat, tLon);
            yield break;
        }
    }

    private static bool IsInsideBounds(double lat, double lon, double leftLon, double rightLon, double topLat, double botLat)
    {
        return lon >= leftLon && lon <= rightLon && lat >= botLat && lat <= topLat;
    }

    private static int BearingBucket(double radians, int buckets)
    {
        double twoPi = System.Math.PI * 2.0;
        double r = radians % twoPi;
        if (r < 0) r += twoPi;
        int b = (int)System.Math.Floor(r / twoPi * buckets);
        if (b < 0) b = 0;
        if (b >= buckets) b = buckets - 1;
        return b;
    }
}
