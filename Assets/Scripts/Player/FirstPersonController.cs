// ============================================================
//  FirstPersonController.cs  —  New Input System (Direct Read)
//
//  SETUP (simpler than before):
//    1. Player needs: CharacterController + PlayerInput + this script
//    2. On PlayerInput:
//         - "Create Actions..." → save the asset
//         - Behavior → set to "Do Nothing"  ← important change
//    3. Camera child setup stays the same, drag it into "Cam" slot
// ============================================================
using UnityEngine;
using UnityEngine.InputSystem;

public class FirstPersonController : MonoBehaviour
{


    [Header("Movement")]
    public float moveSpeed = 5f;

    [Header("Mouse Look")]
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 80f;

    [Header("References")]
    public Transform cam;

    [Header("Jump-off")]
    [Tooltip("How fast leftover jump momentum bleeds off once he is back on the ground, in metres "
             + "per second squared. Stops a launch from ending in him stopping dead sideways.")]
    public float carryDeceleration = 14f;

    // -------------------------------------------------------
    // PRIVATE
    // -------------------------------------------------------
    private CharacterController controller;
    private float verticalVelocity = 0f;
    private float gravity = -9.81f;
    private float verticalRotation = 0f;

    // Jump-off: while launched the player is a plain projectile. Collisions stay on the whole
    // time, so he still hits walls; he just cannot steer and gravity does what it likes.
    private bool launched;
    private Vector3 airVelocity;

    // Horizontal momentum a jump leaves behind, so he lands moving instead of stopping dead.
    private Vector3 momentum;

    // Colliders that are ignored for the duration of a launch. A jump-off starts with the player
    // standing inside whatever he was sitting on, and a CharacterController cannot be switched on
    // inside a collider without PhysX shoving it out sideways, which fights the arc.
    private Collider[] ignoredWhileLaunching;

    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction cancelAction;

    // Read-only view of the current pitch, for external scripts (like a camera-focus feature)
    // that need to know where the camera is currently looking.
    public float CurrentPitch => verticalRotation;

    /// <summary>True while he is in the air on a jump-off arc (no steering, real gravity).</summary>
    public bool IsLaunched => launched;

    // Sets yaw (on the player body) and pitch (stored the same way HandleMouseLook() stores it)
    // together, so HandleMouseLook() naturally continues from here next frame instead of
    // snapping back to whatever verticalRotation held before this was called.
    public void SetLookRotation(float yaw, float pitch)
    {
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        verticalRotation = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);
        if (cam != null)
        {
            cam.localEulerAngles = new Vector3(verticalRotation, 0f, 0f);
        }
    }
    void Start()
    {
        controller = GetComponent<CharacterController>();
        PlayerInput playerInput = GetComponent<PlayerInput>();

        if (playerInput == null)
        {
            Debug.LogError("FirstPersonController: No PlayerInput component found on " + gameObject.name + ". Add one via Add Component → Player Input.");
            return;
        }

        if (playerInput.actions == null)
        {
            Debug.LogError("FirstPersonController: PlayerInput has no Actions asset assigned. Open the PlayerInput component and click 'Create Actions...' or drag in your asset.");
            return;
        }

        moveAction   = playerInput.actions["Move"];
        lookAction   = playerInput.actions["Look"];
        cancelAction = playerInput.actions["Cancel"];

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (launched) StepLaunch(Time.deltaTime);
        else HandleMovement();

        HandleMouseLook();

        if (cancelAction.WasPerformedThisFrame())
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (moveAction.WasPerformedThisFrame())
        {
           
        }

        if (Cursor.lockState == CursorLockMode.None && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void HandleMovement()
    {
        // Sitting on a chair or a swing: the seat owns the player's position and the controller is
        // switched off, so there is nothing to walk with.
        if (controller == null || !controller.enabled) return;

        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 moveDirection = (transform.right   * input.x)
                              + (transform.forward  * input.y);

        verticalVelocity += gravity * Time.deltaTime;
        moveDirection.y = verticalVelocity;

        if (controller.isGrounded)
        {
            verticalVelocity = -2f;
        }

        Vector3 motion = moveDirection * moveSpeed * Time.deltaTime;

        // Momentum left over from a jump-off, so he arrives moving and slides to a stop instead of
        // stopping dead the instant he touches down.
        if (momentum.sqrMagnitude > 0.0001f)
        {
            motion += momentum * Time.deltaTime;

            if (controller.isGrounded)
            {
                momentum = Vector3.MoveTowards(momentum, Vector3.zero,
                                               carryDeceleration * Time.deltaTime);
                if (momentum.sqrMagnitude < 0.04f) momentum = Vector3.zero;
            }
        }

        controller.Move(motion);

    Vector3 horizontalVelocity = controller.velocity;
    horizontalVelocity.y = 0f;

    bool isMoving = horizontalVelocity.magnitude > 0.1f;

    }

    // ------------------------------------------------------------------ jump-off

    /// <summary>
    /// Throws the player along an arc, keeping his collider exactly as it is: he can still hit the
    /// house, he just cannot steer until he lands. Returns false if he was already in the air.
    /// Call this while the CharacterController is still switched off (sitting on a swing); it puts
    /// him back on his feet and switches it on again.
    /// <para><paramref name="ignoreWhileLaunching"/> is for the thing he is getting off. He always
    /// starts the jump standing inside the seat he was sitting on, so that seat is ignored until he
    /// lands; everything else still collides normally.</para>
    /// </summary>
    public bool StartLaunch(Vector3 velocity, Collider[] ignoreWhileLaunching = null)
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (controller == null || launched) return false;

        ignoredWhileLaunching = ignoreWhileLaunching;
        if (ignoreWhileLaunching != null)
        {
            for (int i = 0; i < ignoreWhileLaunching.Length; i++)
            {
                if (ignoreWhileLaunching[i] != null)
                {
                    Physics.IgnoreCollision(controller, ignoreWhileLaunching[i], true);
                }
            }
        }

        if (!controller.enabled)
        {
            StandUpOutOfOverlap();
            controller.enabled = true;
            Physics.SyncTransforms();
        }

        airVelocity = velocity;
        launched = true;
        verticalVelocity = 0f;
        momentum = Vector3.zero;
        return true;
    }

    // The seat puts the player's origin at seat height, which leaves the bottom of his 2 m capsule
    // a metre further down, usually inside the seat or the floor. Push him up until his feet are
    // clear of whatever is under him, so switching collisions back on cannot eject him sideways.
    void StandUpOutOfOverlap()
    {
        float foot = controller.height * 0.5f - controller.center.y;
        Vector3 from = transform.position + Vector3.up * 0.05f;
        float reach = foot + 0.5f;

        // The seat is usually straight below him; step past it so the lift is measured against the
        // real ground instead of the thing he is leaving.
        for (int pass = 0; pass < 6 && reach > 0f; pass++)
        {
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, reach,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return;   // nothing solid under him: he is up in the air, leave him there
            }

            if (IsIgnoredWhileLaunching(hit.collider))
            {
                reach -= (hit.point.y - from.y) + 0.05f;
                from = new Vector3(from.x, hit.point.y - 0.05f, from.z);
                continue;
            }

            float targetY = hit.point.y + foot + 0.05f;
            if (transform.position.y >= targetY) return;

            Vector3 p = transform.position;
            p.y = targetY;
            transform.position = p;
            return;
        }
    }

    bool IsIgnoredWhileLaunching(Collider other)
    {
        if (ignoredWhileLaunching == null || other == null) return false;

        for (int i = 0; i < ignoredWhileLaunching.Length; i++)
        {
            if (ignoredWhileLaunching[i] == other) return true;
        }
        return false;
    }

    // Hands collisions with the thing he jumped off back to PhysX, once he is standing on the ground.
    void RestoreIgnoredCollisions()
    {
        if (ignoredWhileLaunching == null) return;

        if (controller != null)
        {
            for (int i = 0; i < ignoredWhileLaunching.Length; i++)
            {
                if (ignoredWhileLaunching[i] != null)
                {
                    Physics.IgnoreCollision(controller, ignoredWhileLaunching[i], false);
                }
            }
        }

        ignoredWhileLaunching = null;
    }

    void StepLaunch(float dt)
    {
        airVelocity.y += gravity * dt;
        controller.Move(airVelocity * dt);

        if (!controller.isGrounded) return;

        // Down. Hand the horizontal part over to the normal walking code as momentum, which fades
        // out over the next moment so a fast landing does not stop him dead.
        launched = false;
        verticalVelocity = -2f;
        momentum = new Vector3(airVelocity.x, 0f, airVelocity.z);
        airVelocity = Vector3.zero;
        RestoreIgnoredCollisions();
    }

    void HandleMouseLook()
    {
        Vector2 input = lookAction.ReadValue<Vector2>();

        float mouseX = input.x * mouseSensitivity;
        float mouseY = input.y * mouseSensitivity;

        transform.Rotate(0f, mouseX, 0f, Space.Self);

        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -maxLookAngle, maxLookAngle);
        cam.localEulerAngles = new Vector3(verticalRotation, 0f, 0f);
    }
}