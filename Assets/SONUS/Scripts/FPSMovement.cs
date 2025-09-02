using UnityEngine;

[RequireComponent(typeof(CapsuleCollider))]
public class FPSController : MonoBehaviour
{
    [Header("Scene Camera")]
    public Camera SceneCam;
    [Header("Look")]
    public Transform cameraPivot; // child with Camera
    public float mouseXSensitivity = 1.5f;
    public float mouseYSensitivity = 1.2f;
    public float pitchMin = -75f;
    public float pitchMax = 75f;

    [Header("Move")]
    public float moveSpeed = 3.0f;
    public float accel = 12f;
    public float decel = 14f;
    public float gravity = -20f;
    public float jumpForce = 5f;
    public LayerMask groundMask = ~0;

    [Header("Grounding")]
    public float eyeHeight = 1.7f;
    public float groundProbeDistance = 100f;
    public float groundSnapTolerance = 0.3f;

    [Header("Navigation")]
    public float runMultiplier = 1.8f;   // Shift to sprint
    public float slopeLimit = 47f;       // max climb angle
    public float slideGravity = 4f;      // pull down steep slopes

    // internals
    Vector3 lastGroundNormal = Vector3.up;
    bool _isGrounded;



    [Header("Footsteps")]
    public float stepIntervalMeters = 1.8f; // trigger cues while walking
    public System.Action OnStep; // wire this to your audio cue pacing

    float yaw, pitch;
    Vector3 vel;
    float stepProgress;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        yaw = transform.eulerAngles.y;
        if (cameraPivot) pitch = cameraPivot.localEulerAngles.x;
    }

    void Update()
    {
        // --- Look (FPSController owns this) ---
        yaw += Input.GetAxis("Mouse X") * mouseXSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseYSensitivity;
        pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // Use cameraPivot if set; otherwise fallback to SceneCam.transform
        var pivot = cameraPivot != null ? cameraPivot : (SceneCam != null ? SceneCam.transform : null);
        if (pivot != null) pivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // --- Move (simple accel/decel + gravity) ---
        // --- Move input (world) ---
        Vector2 in2 = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (in2.sqrMagnitude > 1f) in2.Normalize();

        // Desired direction in world space
        Vector3 wishWorld = (transform.forward * in2.y + transform.right * in2.x);

        // Follow ground plane when grounded
        if (_isGrounded) wishWorld = Vector3.ProjectOnPlane(wishWorld, lastGroundNormal);

        // Speed (with sprint)
        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? runMultiplier : 1f);
        Vector3 targetVel = wishWorld.normalized * speed;

        // Smooth accel/decel on XZ
        Vector3 xz = new Vector3(vel.x, 0f, vel.z);
        xz = Vector3.MoveTowards(xz, targetVel, (in2.sqrMagnitude > 0 ? accel : decel) * Time.deltaTime);
        vel.x = xz.x; vel.z = xz.z;


        // Gravity + simple ground snap
        vel.y += gravity * Time.deltaTime;
        bool grounded = Physics.Raycast(
            transform.position + Vector3.up * 0.1f,
            Vector3.down,
            out var _,
            0.3f,
            groundMask
        );
        if (grounded && vel.y < 0f) vel.y = -2f;
        if (grounded && Input.GetKeyDown(KeyCode.Space)) vel.y = jumpForce;

        transform.position += vel * Time.deltaTime;

        // --- Footstep pacing ---
        Vector3 xzDelta = new Vector3(vel.x, 0f, vel.z) * Time.deltaTime;
        float dist = xzDelta.magnitude;
        if (dist > 0.001f)
        {
            stepProgress += dist;
            if (stepProgress >= stepIntervalMeters)
            {
                stepProgress = 0f;
                OnStep?.Invoke();
            }
        }

        GroundSnap();
    }


    void GroundSnap()
    {
        // Probe downward to find terrain
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(origin, Vector3.down, out var hit, groundProbeDistance, groundMask))
        {
            lastGroundNormal = hit.normal;

            float desiredY = hit.point.y + eyeHeight;

            // Are we effectively on/near ground?
            _isGrounded = transform.position.y <= desiredY + groundSnapTolerance;

            if (_isGrounded)
            {
                // Clamp to ground and kill downward velocity
                var p = transform.position;
                p.y = desiredY;
                transform.position = p;
                if (vel.y < 0f) vel.y = 0f;

                // Steep slope? slide a bit down the plane
                float slopeAngle = Vector3.Angle(lastGroundNormal, Vector3.up);
                if (slopeAngle > slopeLimit)
                {
                    Vector3 slideDir = Vector3.ProjectOnPlane(Vector3.down, lastGroundNormal).normalized;
                    vel += slideDir * slideGravity * Time.deltaTime;
                }
            }
            else
            {
                // Not close enough to ground → airborne
                _isGrounded = false;
            }
        }
        else
        {
            _isGrounded = false;
            lastGroundNormal = Vector3.up;
        }
    }


    // Add to FPSController (anywhere inside the class)
    public void SnapYaw(float newYaw)
    {
        yaw = newYaw;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // keep current pitch; re-apply to pivot/cam so there’s no visual pop
        var pivot = cameraPivot != null ? cameraPivot : (SceneCam != null ? SceneCam.transform : null);
        if (pivot != null) pivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }


}
