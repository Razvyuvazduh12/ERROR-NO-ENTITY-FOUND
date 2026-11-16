using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A chair the player can sit on by aiming at it and pressing E — the exact same raycast
/// path as the doorknob (PlayerInteractor finds this component on the hit collider and
/// calls Interact). Standing close to a chair does nothing anymore.
///
/// SETUP: this component goes on the SAME GameObject as the collider the player's raycast
/// hits, and that object must be on the Interactable layer.
///
/// The player is teleported to the seat point, its CharacterController is switched off so it
/// cannot walk (mouse look keeps working, so you can still look around), and it is pinned there
/// every frame. Pressing E again — or trying to walk — puts the player back exactly where they
/// were standing before they sat down. Standing up is deliberately not raycast-based: the chair
/// is usually behind or below the camera, so E works from anywhere while seated.
/// </summary>
[DisallowMultipleComponent]
public class ChairSeat : MonoBehaviour, IInteractable
{
    [Header("Seat point")]
    [Tooltip("Optional. Drag an empty GameObject here to mark exactly where the player's origin " +
             "goes. When set, it is used instead of Seat Offset, and nothing (scale, rotation, " +
             "parenting) can throw it off. This is the safest way to place a seat.")]
    public Transform seatMarker;

    [Tooltip("Offset from this object's pivot, in the chair's own directions, measured in METRES.\n\n" +
             "Y is the height. The player's eye sits about 0.5 above its own origin, so if the " +
             "pivot is on the floor, 0.7 puts the eye at roughly 1.2 m — sitting height. If the " +
             "pivot is at the seat surface, use about 0.25.\n\n" +
             "The object's scale is deliberately ignored. A scaled model or collider proxy would " +
             "otherwise turn 0.7 into 7 or 70.")]
    public Vector3 seatOffset = new Vector3(0f, 0.7f, 0f);

    [Tooltip("Multiply Seat Offset by this object's size. Leave this OFF unless you want the " +
             "seat to move when you resize the object. This is what used to fling the player away.")]
    public bool scaleOffsetWithObject = false;

    [Tooltip("Turn the player to face the same way the chair faces when they sit down.")]
    public bool faceChairForward = true;

    [Tooltip("Added to the chair's yaw. Set to 180 if the chair model faces the other way round.")]
    public float yawOffset = 0f;

    [Tooltip("Looking up/down angle applied when sitting. 0 = straight ahead.")]
    [Range(-80f, 80f)]
    public float seatPitch = 0f;

    [Header("Interaction")]
    [Tooltip("Safety net on top of the raycast: the player still has to be within this many " +
             "metres of the chair, otherwise E does nothing.")]
    public float interactRange = 2.5f;

    [Tooltip("Stand up the moment the player tries to walk. Untick to leave E as the only way out.")]
    public bool standUpOnMove = true;

    [Tooltip("Put the player back at the yaw they had before sitting. Untick to keep the chair's direction.")]
    public bool restoreLookDirection = true;

    // ---- static registry: several chairs can exist, only one may own the player ----
    private static ChairSeat OccupiedChair;
    private static FirstPersonController cachedPlayer;

    /// <summary>True while the player is sitting in any chair.</summary>
    public static bool IsSeated => OccupiedChair != null;

    // Static state survives between Play sessions in this project (Enter Play Mode Options with
    // domain reload disabled), so wipe it on every start. Otherwise a chair that was occupied in
    // the previous session would keep every other chair — and the item inspector — locked out.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        OccupiedChair = null;
        cachedPlayer = null;
    }

    // ---- per-sit state ----
    private Transform playerTransform;
    private FirstPersonController playerController;
    private CharacterController playerCapsule;
    private InputAction moveAction;

    private Vector3 savedPosition;
    private float savedYaw;
    private float savedPitch;
    private bool waitingForMoveRelease;
    private int sitFrame;

    void OnDisable()
    {
        // Being disabled while occupied would leave the player frozen in mid-air, so let them out.
        if (OccupiedChair == this) StandUp();
    }

    void Start()
    {
        // The old seat point used TransformPoint, which multiplies the offset by the object's
        // world scale. A chair imported from an FBX and then resized can carry a scale of 10 or
        // 100 on its root, so a 0.7 m offset became 7 or 70 m and the player landed half a room
        // away. That is fixed, but the scale is still worth pointing out once.
        Vector3 scale = transform.lossyScale;

        bool unusualScale =
            Mathf.Abs(scale.x) > 2.01f || Mathf.Abs(scale.y) > 2.01f || Mathf.Abs(scale.z) > 2.01f ||
            Mathf.Abs(scale.x) < 0.49f || Mathf.Abs(scale.y) < 0.49f || Mathf.Abs(scale.z) < 0.49f;

        if (unusualScale)
        {
            Debug.LogWarning(
                $"ChairSeat on '{name}': world scale is {scale}. Seat Offset is in metres and is " +
                "NOT multiplied by that scale, so 0.7 stays 0.7 m. If the player used to be " +
                "teleported far away from this chair, this scale is why.", this);
        }
    }

    // ------------------------------------------------------------------ interactable

    /// <summary>Called by PlayerInteractor when the player presses E while aiming at this chair.</summary>
    public void Interact()
    {
        // Already sitting in this chair? Then it is the "stand up" target.
        if (OccupiedChair == this)
        {
            if (Time.frameCount != sitFrame) StandUp();
            return;
        }

        if (OccupiedChair != null) return;

        // An object is being held up in front of the camera: E belongs to the inspector.
        if (ItemInspector.IsInspecting) return;
        if (Time.frameCount == ItemInspector.ReleaseLockFrame) return;

        FirstPersonController fpc = FindPlayer();
        if (fpc == null) return;

        if (Vector3.Distance(fpc.transform.position, transform.position) > interactRange) return;

        Sit(fpc);
    }

    // ------------------------------------------------------------------ update

    void Update()
    {
        if (OccupiedChair != this) return;

        UpdateSeated();
    }

    // Keeps the player welded to the seat even if something else nudges the transform.
    void LateUpdate()
    {
        if (OccupiedChair == this && playerTransform != null)
        {
            playerTransform.position = SeatPoint();
        }
    }

    void OnDrawGizmosSelected()
    {
        Vector3 seat = SeatPoint();

        Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.9f);
        Gizmos.DrawLine(transform.position, seat);
        Gizmos.DrawWireSphere(seat, 0.08f);

        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }

    // ------------------------------------------------------------------ sitting

    void Sit(FirstPersonController fpc)
    {
        playerController = fpc;
        playerTransform = fpc.transform;
        playerCapsule = fpc.GetComponent<CharacterController>();

        // Read the player's own "Move" action so walking away from the chair is detected with
        // whatever keys they actually bound, without touching the controller script.
        PlayerInput playerInput = fpc.GetComponent<PlayerInput>();
        moveAction = (playerInput != null && playerInput.actions != null)
            ? playerInput.actions.FindAction("Move", false)
            : null;

        savedPosition = playerTransform.position;
        savedYaw = playerTransform.eulerAngles.y;
        savedPitch = fpc.CurrentPitch;

        // Switching the capsule off is what actually stops the walking: FirstPersonController
        // keeps calling Move(), which does nothing while the controller is disabled, and mouse
        // look still works because that only rotates the transform.
        if (playerCapsule != null) playerCapsule.enabled = false;

        playerTransform.position = SeatPoint();

        if (faceChairForward)
        {
            playerController.SetLookRotation(transform.eulerAngles.y + yawOffset, seatPitch);
        }

        // If the player was already holding a movement key when they sat, wait for them to let go
        // before "move to stand up" is allowed — otherwise they would pop straight back out.
        waitingForMoveRelease = true;

        // The very same E press that sat them down must not also stand them up.
        sitFrame = Time.frameCount;
        OccupiedChair = this;
    }

    void UpdateSeated()
    {
        // E from anywhere stands the player up: the chair is usually below or behind the camera,
        // so asking them to aim at it while seated would be a trap.
        if (WasEPressedThisFrame() && Time.frameCount != sitFrame)
        {
            StandUp();
            return;
        }

        if (!standUpOnMove) return;

        bool tryingToMove = ReadMoveInput().sqrMagnitude > 0.01f;

        if (waitingForMoveRelease)
        {
            if (!tryingToMove) waitingForMoveRelease = false;
            return;
        }

        if (tryingToMove) StandUp();
    }

    void StandUp()
    {
        if (playerTransform != null)
        {
            playerTransform.position = savedPosition;

            if (restoreLookDirection)
            {
                if (playerController != null)
                {
                    playerController.SetLookRotation(savedYaw, savedPitch);
                }
                else
                {
                    playerTransform.rotation = Quaternion.Euler(0f, savedYaw, 0f);
                }
            }
        }

        // Re-enable only after the transform is back in place, then push the new pose into PhysX
        // so the capsule does not start from a stale position.
        if (playerCapsule != null) playerCapsule.enabled = true;
        Physics.SyncTransforms();

        playerTransform = null;
        playerController = null;
        playerCapsule = null;
        moveAction = null;
        OccupiedChair = null;
    }

    // ------------------------------------------------------------------ helpers

    Vector3 SeatPoint()
    {
        if (seatMarker != null) return seatMarker.position;

        Vector3 offset = seatOffset;

        if (scaleOffsetWithObject)
        {
            Vector3 scale = transform.lossyScale;
            offset = new Vector3(offset.x * scale.x, offset.y * scale.y, offset.z * scale.z);
        }

        // Rotation yes, scale no: "0.7 up" means 0.7 metres up, whatever the model is scaled to.
        return transform.position + transform.rotation * offset;
    }

    static bool WasEPressedThisFrame()
    {
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    }

    // The player's bound Move action when we have it, raw WASD/arrows as a fallback.
    Vector2 ReadMoveInput()
    {
        if (moveAction != null && moveAction.enabled)
        {
            return moveAction.ReadValue<Vector2>();
        }

        Keyboard k = Keyboard.current;
        if (k == null) return Vector2.zero;

        float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f)
                - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
        float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f)
                - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);

        return new Vector2(x, y);
    }

    static FirstPersonController FindPlayer()
    {
        if (cachedPlayer == null)
        {
            cachedPlayer = Object.FindAnyObjectByType<FirstPersonController>();
        }
        return cachedPlayer;
    }
}
