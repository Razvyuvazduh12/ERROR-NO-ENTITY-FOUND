using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A swing the player can sit on, in the same way as ChairSeat: aim at it, press E, the player is
/// welded to the seat point and its CharacterController is switched off.
///
/// While seated: C pushes forward, V pushes backward. The swing is a pendulum, so it speeds up on
/// its own as gravity pulls it back down and slows down as it climbs, until the damping eats it.
///
/// GETTING OFF. E puts him back exactly where he was standing. WASD instead jumps: how hard he is
/// thrown comes from how fast the seat was moving at that moment, so a still swing just lets him
/// down gently and a hard swing throws him across the field (see Launch). His collider stays on
/// the whole way, so the arc can still end against a wall; he just cannot steer it. The swing
/// carries on swinging after he leaves.
///
/// There are no hard stops on the hinge. Push hard enough and the swing goes over the top and
/// right round in a circle.
///
/// SETUP: this component belongs on the object that carries the collider the player's raycast hits
/// (the MeshCollider on "hinge"), and that object must be on the Interactable layer. Only the hinge's
/// local X rotation is touched — its Y and Z stay whatever they were.
/// </summary>
[DisallowMultipleComponent]
public class Swing : MonoBehaviour, IInteractable
{
    [Header("Parts")]
    [Tooltip("The object that rotates — the hinge. Leave empty to use this object.")]
    public Transform hinge;

    [Tooltip("Optional empty marking where the player's origin goes. Parent it under the hinge so "
             + "it rides the swing. When empty, Seat Offset is used instead.")]
    public Transform seatMarker;

    [Tooltip("Used when Seat Marker is empty: metres from the HINGE PIVOT to the player's origin, in "
             + "the swing's own directions. Object scale is ignored, so this is plain metres. Y is up, "
             + "so a seat hanging below the pivot is negative. The gizmo draws exactly where the player "
             + "lands.")]
    public Vector3 seatOffset = new Vector3(0f, -2.79f, 0f);

    [Header("Rest position (hinge local X)")]
    [Tooltip("Where the swing hangs when nobody is on it. -90 is the model upright. There are no "
             + "swing limits: the hinge turns freely all the way round.")]
    public float restAngle = -90f;

    [Header("Motion")]
    [Tooltip("How hard gravity pulls the swing back toward Rest Angle, in degrees per second squared "
             + "at a 90 degree difference. Bigger = heavier and faster.")]
    public float gravity = 260f;

    [Tooltip("Extra speed gained per second while C or V is held.")]
    public float pushAcceleration = 300f;

    [Tooltip("Slowest the swing moves once it has been pushed.")]
    public float minVelocity = 25f;

    [Tooltip("Speed cap. A full circle needs about 211 deg/s with this gravity, so the cap has to "
             + "sit above that or looping is impossible.")]
    public float maxVelocity = 320f;

    [Tooltip("Energy the swing loses per second. 0 = it swings forever.")]
    [Range(0f, 4f)]
    public float damping = 0.25f;

    [Header("Sitting")]
    [Tooltip("The player still has to be this close to the SEAT, on top of the raycast hit.")]
    public float interactRange = 3f;

    [Tooltip("Turn the player to face the direction the swing travels when it goes forward. The "
             + "hinge's own axes are a mess, so this is measured from the seat's actual motion "
             + "instead of guessed.")]
    public bool faceSwingForward = true;

    [Tooltip("Added to that direction. Set to 180 if the player ends up facing the wrong way.")]
    public float yawOffset = 0f;

    [Tooltip("Look up/down angle applied when sitting. 0 = straight ahead.")]
    [Range(-80f, 80f)]
    public float seatPitch = 0f;

    [Header("Jump-off")]
    [Tooltip("How fast the seat has to be moving, in metres per second, before WASD throws him "
             + "instead of just letting him off. Below this the swing is parked enough that a jump "
             + "would go nowhere, so he simply steps off.")]
    public float minLaunchSpeed = 1f;

    [Tooltip("How much faster than the seat he is thrown. 1.6 turns the swing's top speed (about "
             + "7.8 m/s at the seat) into a launch of about 12.5 m/s, which lands 12-15 m away.")]
    public float launchBoost = 1.6f;

    [Tooltip("Ceiling on the launch speed, whatever the boost would give.")]
    public float maxLaunchSpeed = 14f;

    [Tooltip("Angle above horizontal he is thrown at, in degrees. 0 is flat, 90 is straight up.")]
    [Range(0f, 89f)]
    public float launchAngle = 40f;

    // ---- static registry: only one seat in the house may own the player ----
    private static Swing OccupiedSwing;
    private static FirstPersonController cachedPlayer;

    public static bool IsSeated => OccupiedSwing != null;

    // Static state survives between Play sessions here (domain reload is off), so wipe it on start.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        OccupiedSwing = null;
        cachedPlayer = null;
    }

    // ---- per-swing state ----
    private float angle;          // hinge local X, degrees
    private float velocity;       // degrees per second
    private Vector3 seatLocal;    // seat point in the hinge's local space, so it swings with it
    private float baseYaw;        // hinge euler Y at rest, never touched
    private float baseRoll;       // hinge euler Z at rest, never touched
    private float forwardYaw;     // yaw the player faces when sitting

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

    void Awake()
    {
        if (hinge == null) hinge = transform;

        Vector3 rest = hinge.localEulerAngles;
        baseYaw = rest.y;
        baseRoll = rest.z;

        // Seat Offset is authored upright, in metres from the pivot; freeze it into hinge space once
        // so every later frame just rides the hinge's own rotation, exactly like the model does.
        Transform frame = hinge.parent != null ? hinge.parent : hinge;
        seatLocal = hinge.InverseTransformPoint(hinge.position + frame.rotation * seatOffset);

        angle = restAngle;
        Apply();

        forwardYaw = MeasureForwardYaw();
    }

    // Which way does the player face while swinging? Rather than trusting the hinge's crooked axes,
    // nudge the hinge both ways and see which way the seat actually travels when the angle drops
    // toward Forward Angle — that is the direction the sitter is heading.
    float MeasureForwardYaw()
    {
        Vector3 a = SampleSeat(restAngle + 2f);
        Vector3 b = SampleSeat(restAngle - 2f);
        Vector3 travel = b - a;
        travel.y = 0f;

        if (travel.sqrMagnitude < 0.0001f)
        {
            Transform frame = hinge.parent != null ? hinge.parent : hinge;
            return frame.eulerAngles.y;
        }

        return Quaternion.LookRotation(travel, Vector3.up).eulerAngles.y;
    }

    // Seat position for an arbitrary angle, without disturbing the one the swing is using.
    Vector3 SampleSeat(float a)
    {
        float keep = angle;
        angle = a;
        Apply();
        Vector3 p = SeatPoint();
        angle = keep;
        Apply();
        return p;
    }

    void OnDisable()
    {
        // Being switched off while occupied would leave the player frozen, so let them out.
        if (OccupiedSwing == this) StandUp();
    }

    // ------------------------------------------------------------------ interactable

    public void Interact()
    {
        if (OccupiedSwing == this)
        {
            if (Time.frameCount != sitFrame) StandUp();
            return;
        }

        if (OccupiedSwing != null) return;
        if (ChairSeat.IsSeated) return;

        // An object is held up in front of the camera: E belongs to the inspector.
        if (ItemInspector.IsInspecting) return;
        if (Time.frameCount == ItemInspector.ReleaseLockFrame) return;

        FirstPersonController fpc = FindPlayer();
        if (fpc == null) return;

        if (Vector3.Distance(fpc.transform.position, SeatPoint()) > interactRange) return;

        Sit(fpc);
    }

    // ------------------------------------------------------------------ update

    void Update()
    {
        if (OccupiedSwing != this)
        {
            // Nobody on it. A swing abandoned mid-arc keeps swinging and slows down on its own,
            // instead of freezing where the player left it. Gravity keeps working the whole time,
            // so one that stopped dead against a hard stop swings back down instead of hanging up
            // there.
            if (Mathf.Abs(velocity) > 0.05f || Mathf.Abs(Mathf.DeltaAngle(angle, restAngle)) > 0.05f)
            {
                StepSwing(Time.deltaTime);

                // Damped all the way back: leave it hanging exactly where it hangs when idle.
                if (Mathf.Abs(velocity) < 0.5f && Mathf.Abs(Mathf.DeltaAngle(angle, restAngle)) < 0.05f)
                {
                    velocity = 0f;
                    angle = restAngle;
                    Apply();
                }
            }
            return;
        }

        if (Time.frameCount != sitFrame && Keyboard.current != null
            && Keyboard.current.eKey.wasPressedThisFrame)
        {
            StandUp();
            return;
        }

        if (ReadMoveInput().sqrMagnitude > 0.01f)
        {
            // Ignore keys the player was already holding the moment they sat down.
            if (waitingForMoveRelease) return;

            // Moving off is a jump: the faster the seat is travelling right now, the further he
            // goes. A parked swing just lets him off.
            if (SeatVelocity().magnitude >= minLaunchSpeed) Launch();
            else StandUp();
            return;
        }
        waitingForMoveRelease = false;

        StepSwing(Time.deltaTime);
    }

    // Keeps the player welded to the seat, even though the seat moves with the hinge.
    void LateUpdate()
    {
        if (OccupiedSwing == this && playerTransform != null)
        {
            playerTransform.position = SeatPoint();
        }
    }

    void OnDrawGizmosSelected()
    {
        Transform h = hinge != null ? hinge : transform;
        Transform frame = h.parent != null ? h.parent : h;

        Vector3 seat = Vector3.zero;
        if (Application.isPlaying && seatMarker == null)
        {
            seat = h.TransformPoint(seatLocal);
        }
        else if (seatMarker != null)
        {
            seat = seatMarker.position;
        }
        else
        {
            seat = h.position + frame.rotation * seatOffset;
        }

        Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.9f);
        Gizmos.DrawLine(h.position, seat);
        Gizmos.DrawWireSphere(seat, 0.08f);

        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.35f);
        Gizmos.DrawWireSphere(h.position, interactRange);
    }

    // ------------------------------------------------------------------ motion

    void StepSwing(float dt)
    {
        Keyboard k = Keyboard.current;
        bool seated = OccupiedSwing == this;
        bool forward = seated && k != null && k.cKey.isPressed;
        bool backward = seated && k != null && k.vKey.isPressed;

        if (forward) velocity -= pushAcceleration * dt;
        if (backward) velocity += pushAcceleration * dt;

        // Pendulum: the further from rest, the harder gravity pulls back. This is what makes the
        // swing crawl at the top of the arc and rush through the bottom.
        velocity -= gravity * Mathf.Sin((angle - restAngle) * Mathf.Deg2Rad) * dt;

        velocity *= Mathf.Clamp01(1f - damping * dt);

        // A push always gets it moving at least the slow speed, and never past the fast one.
        if (forward) velocity = Mathf.Min(velocity, -minVelocity);
        if (backward) velocity = Mathf.Max(velocity, minVelocity);
        velocity = Mathf.Clamp(velocity, -maxVelocity, maxVelocity);

        angle += velocity * dt;

        // No stops: past the top it just keeps going round. Keep the number bounded so it cannot
        // creep away over a long session; the hinge does not care either way.
        if (angle > 180f || angle < -180f)
        {
            angle = Mathf.Repeat(angle + 180f, 360f) - 180f;
        }

        Apply();
    }

    void Apply()
    {
        hinge.localRotation = Quaternion.Euler(angle, baseYaw, baseRoll);
    }

    // ------------------------------------------------------------------ sitting

    void Sit(FirstPersonController fpc)
    {
        playerController = fpc;
        playerTransform = fpc.transform;
        playerCapsule = fpc.GetComponent<CharacterController>();

        PlayerInput playerInput = fpc.GetComponent<PlayerInput>();
        moveAction = (playerInput != null && playerInput.actions != null)
            ? playerInput.actions.FindAction("Move", false)
            : null;

        savedPosition = playerTransform.position;
        savedYaw = playerTransform.eulerAngles.y;
        savedPitch = fpc.CurrentPitch;

        if (playerCapsule != null) playerCapsule.enabled = false;

        playerTransform.position = SeatPoint();

        if (faceSwingForward)
        {
            playerController.SetLookRotation(forwardYaw + yawOffset, seatPitch);
        }

        waitingForMoveRelease = true;
        sitFrame = Time.frameCount;
        OccupiedSwing = this;
    }

    void StandUp()
    {
        if (playerTransform != null)
        {
            playerTransform.position = savedPosition;
            if (playerController != null) playerController.SetLookRotation(savedYaw, savedPitch);
        }

        if (playerCapsule != null) playerCapsule.enabled = true;
        Physics.SyncTransforms();

        // "Stop completely": no leftover velocity, hanging exactly where it hangs when idle.
        velocity = 0f;
        angle = restAngle;
        Apply();

        waitingForMoveRelease = false;
        ClearOccupant();
    }

    void ClearOccupant()
    {
        playerTransform = null;
        playerController = null;
        playerCapsule = null;
        moveAction = null;
        OccupiedSwing = null;
    }

    // ------------------------------------------------------------------ jumping off

    // How fast the seat itself is moving, in metres per second, found by letting the hinge run a
    // hair forward in time and watching where the seat goes. The hinge's local axes are crooked
    // (the model is tilted), so measuring the motion beats trying to reason about the axes.
    Vector3 SeatVelocity()
    {
        const float probe = 0.01f;   // seconds
        Vector3 here = SeatPoint();
        Vector3 soon = SampleSeat(angle + velocity * probe);
        return (soon - here) / probe;
    }

    // WASD while the swing is moving: he leaves the seat as a projectile, thrown the way the seat
    // was carrying him and tipped up by Launch Angle. Pushing at the bottom of the arc therefore
    // throws him furthest, exactly like a real jump off a swing. His collider is never switched
    // off; only the swing itself is ignored until he lands, because he starts the jump standing
    // inside it.
    void Launch()
    {
        FirstPersonController fpc = playerController;
        CharacterController capsule = playerCapsule;

        Vector3 seat = SeatVelocity();

        // Where the seat is actually travelling, flattened. A pendulum's seat moves horizontally
        // through the bottom of the arc, and that is the direction a jump off a swing goes in.
        Vector3 flat = new Vector3(seat.x, 0f, seat.z);
        float sideways = flat.magnitude;

        float radians = launchAngle * Mathf.Deg2Rad;
        Vector3 direction;

        if (sideways < 0.2f)
        {
            // Straight over the top: every bit of the seat's speed is vertical, so there is no
            // sideways heading to aim along. Going straight up is the honest answer. Making up a
            // heading here used to fling him sideways at full force.
            direction = Vector3.up;
        }
        else
        {
            flat /= sideways;
            direction = flat * Mathf.Cos(radians) + Vector3.up * Mathf.Sin(radians);
        }

        float launchSpeed = Mathf.Min(seat.magnitude * launchBoost, maxLaunchSpeed);
        Collider[] rig = RigColliders();

        // Let go of him without moving him: he takes off from the seat, not from where he stood.
        ClearOccupant();

        if (fpc != null && fpc.StartLaunch(direction * launchSpeed, rig)) return;

        // No controller to throw him with. Never leave him without a collider.
        if (capsule != null) capsule.enabled = true;
        Physics.SyncTransforms();
    }

    // Every collider the swing is made of. Only the single chain of wrapper objects is walked, so
    // a swing parked under a "World" folder cannot drag the rest of the scene in with it.
    Collider[] RigColliders()
    {
        Transform top = transform;
        while (top.parent != null && top.parent.childCount == 1) top = top.parent;
        return top.GetComponentsInChildren<Collider>(true);
    }

    // ------------------------------------------------------------------ helpers

    Vector3 SeatPoint()
    {
        return seatMarker != null ? seatMarker.position : hinge.TransformPoint(seatLocal);
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
