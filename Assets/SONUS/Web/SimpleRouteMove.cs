using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleRouteMove : MonoBehaviour
{
    List<Transform> _points;
    float _speed;
    float _dwell;
    bool _loop;
    Coroutine _co;

    // Optional actor sync
    private TargetProxy _proxy;
    private OLMGeoMapper _geo;

    public void Configure(List<Transform> pts, float speed, float dwell, bool loop)
    {
        _points = pts;
        _speed = Mathf.Max(0.01f, speed);
        _dwell = Mathf.Max(0f, dwell);
        _loop = loop;

        // Cache optional deps
        _proxy = GetComponent<TargetProxy>();
        _geo = FindFirstObjectByType<OLMGeoMapper>();
    }

    public void Begin()
    {
        if (_co != null) StopCoroutine(_co);
        if (_points == null || _points.Count < 2) return;
        _co = StartCoroutine(Run());
    }

    public void StopMoving()
    {
        if (_co != null) StopCoroutine(_co);
        _co = null;
    }

    IEnumerator Run()
    {
        int i = 0;
        while (true)
        {
            var b = _points[(i + 1) % _points.Count].position;

            while ((transform.position - b).sqrMagnitude > 0.05f)
            {
                transform.position = Vector3.MoveTowards(transform.position, b, _speed * Time.deltaTime);

                // ✅ Keep the TargetActor in sync with the moved world position (best-effort).
                if (_proxy != null && _proxy.actor != null && _geo != null)
                {
                    if (_geo.TryWorldToLatLon(transform.position, out double lat, out double lon))
                    {
                        _proxy.actor._Lat = lat;
                        _proxy.actor._Lon = lon;
                        // _proxy.actor._Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                    }
                }

                yield return null;
            }

            // Snap exactly to the point to avoid tiny drift.
            transform.position = b;

            // One more sync at the segment end (helps when loop exits quickly)
            if (_proxy != null && _proxy.actor != null && _geo != null)
            {
                if (_geo.TryWorldToLatLon(transform.position, out double lat, out double lon))
                {
                    _proxy.actor._Lat = lat;
                    _proxy.actor._Lon = lon;
                }
            }

            if (_dwell > 0f) yield return new WaitForSeconds(_dwell);
            i++;
            if (i >= _points.Count - 1) { if (_loop) i = 0; else yield break; }
        }
    }
}
