// CoordinateFinderGeo.cs
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class CoordinateFinderGeo : MonoBehaviour
{
    [Tooltip("Reference your existing GeoMapper in the scene.")]
    public GeoMapper mapper;

    [SerializeField, TextArea(1, 2)] string _note = "Move this object. Lat/Lon updates live.";
    [SerializeField, HideInInspector] double _lat, _lon;

    public (double lat, double lon) Current => (_lat, _lon);

    void Update()
    {
        if (mapper == null) return;
        var (lat, lon) = mapper.WorldToLatLon(transform.position);
        _lat = lat; _lon = lon;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (mapper == null) return;
        Handles.Label(transform.position + Vector3.up * 1.25f, $"Lat {_lat:F6}\nLon {_lon:F6}");
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.35f);
    }

    [CustomEditor(typeof(CoordinateFinderGeo))]
    public class E : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var cf = (CoordinateFinderGeo)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Geo (read-only)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Latitude", cf.Current.lat.ToString("F6"));
            EditorGUILayout.LabelField("Longitude", cf.Current.lon.ToString("F6"));
            EditorGUILayout.Space();
            if (GUILayout.Button("Copy as 'lat, lon'"))
                EditorGUIUtility.systemCopyBuffer = $"{cf.Current.lat:F6}, {cf.Current.lon:F6}";
            if (GUILayout.Button("Copy as JSON"))
                EditorGUIUtility.systemCopyBuffer = $"{{\"lat\":{cf.Current.lat:F6},\"lon\":{cf.Current.lon:F6}}}";
        }
    }
#endif
}
