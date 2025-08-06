using UnityEngine;

public class MarkerRotator : MonoBehaviour
{
    public Transform camera3D; // Assign your 3D camera here
    public RectTransform userIconUI; // or a SpriteRenderer / GameObject if it's on the map

    void Update()
    {
        float camY = camera3D.eulerAngles.y;
        userIconUI.localEulerAngles = new Vector3(0, 0, -camY); // negative to match compass style
    }
}
