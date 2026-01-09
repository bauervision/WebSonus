// Assets/Sonus/Core/OLMPlayerGeoPublisher.cs
using UnityEngine;
using Sonus.Core;

public class OLMPlayerGeoPublisher : MonoBehaviour
{
    public OLMGeoMapper mapper;
    public float writeHz = 20f;

    float _next;

    void Awake()
    {
        if (mapper == null) mapper = FindFirstObjectByType<OLMGeoMapper>();
    }

    void Update()
    {
        if (Time.time < _next) return;
        _next = Time.time + (1f / Mathf.Max(1f, writeHz));

        if (mapper != null && mapper.TryFeetScreenToLatLon(out double lat, out double lon))
            SonusPlayerGeoState.Set(lat, lon);
    }
}
