using UnityEngine;

public class CameraOrbitController : MonoBehaviour
{
    public float sensitivity = 0.3f;   // Mouse sensitivity
    public float damping = 5f;         // Inertia smoothing
    public float minY = -80f;          // Vertical clamp (pitch)
    public float maxY = 80f;

    private Vector2 rotation;          // Current rotation (x = yaw, y = pitch)
    private Vector2 velocity;          // Delta rotation (inertia)
    private bool isDragging;

    void LateUpdate()
    {
        HandleInput();

        // Apply velocity with damping
        rotation += velocity * Time.deltaTime;
        velocity = Vector2.Lerp(velocity, Vector2.zero, damping * Time.deltaTime);

        // Clamp vertical rotation
        rotation.y = Mathf.Clamp(rotation.y, minY, maxY);

        // Apply rotation to camera
        transform.rotation = Quaternion.Euler(rotation.y, rotation.x, 0f);
    }

    public void SetRotation(float yaw, float pitch = 0f)
    {
        rotation.x = yaw;
        rotation.y = Mathf.Clamp(pitch, minY, maxY);
        transform.rotation = Quaternion.Euler(rotation.y, rotation.x, 0f);

        // Also clear velocity so it doesn't immediately move
        velocity = Vector2.zero;
    }

    void HandleInput()
    {
        if (Input.GetMouseButtonDown(1)) isDragging = true;
        if (Input.GetMouseButtonUp(1)) isDragging = false;

        if (isDragging)
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");

            velocity += new Vector2(dx, -dy) * sensitivity * 100f;
        }
    }
}
