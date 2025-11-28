using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleRouteMover : MonoBehaviour
{
    List<Transform> _points; float _speed; float _dwell; bool _loop; Coroutine _co;

    public void Configure(List<Transform> pts, float speed, float dwell, bool loop)
    { _points = pts; _speed = Mathf.Max(0.01f, speed); _dwell = Mathf.Max(0f, dwell); _loop = loop; }

    public void Begin() { if (_co != null) StopCoroutine(_co); if (_points == null || _points.Count < 2) return; _co = StartCoroutine(Run()); }
    public void StopMoving() { if (_co != null) StopCoroutine(_co); _co = null; }

    IEnumerator Run()
    {
        int i = 0;
        while (true)
        {
            var a = _points[i].position;
            var b = _points[(i + 1) % _points.Count].position;

            while ((transform.position - b).sqrMagnitude > 0.05f)
            {
                transform.position = Vector3.MoveTowards(transform.position, b, _speed * Time.deltaTime);
                yield return null;
            }

            if (_dwell > 0f) yield return new WaitForSeconds(_dwell);
            i++;
            if (i >= _points.Count - 1) { if (_loop) i = 0; else yield break; }
        }
    }
}
