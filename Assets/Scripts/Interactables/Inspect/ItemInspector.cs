using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Resident-Evil-style inspection: the object flies up in front of the camera, the mouse
/// turns it around, the player cannot walk or look away until it is put down.
///
/// This is a singleton that builds itself the first time it is needed, so there is nothing
/// to add to the scene. It works on any object that has an InspectableItem on its collider.
///
/// The held object is NOT parented to the camera: the player's transform has a non-uniform
/// scale (0.56, 0.95, 1.16), which would shear the model. Instead a world-space pivot is
/// placed at the camera each frame and the object is parented to that.
/// </summary>
[DisallowMultipleComponent]
public class ItemInspector : MonoBehaviour
{
    // ------------------------------------------------------------------ settings

    [Header("Turning")]
    [Tooltip("Degrees of rotation per pixel of mouse movement. Bigger = the object spins faster.")]
    [SerializeField] private float rotationSensitivity = 0.5f;

    [Tooltip("How fast the object catches up with the mouse. Higher = tighter, lower = floatier.")]
    [SerializeField] private float rotationSmoothing = 22f;

    [Tooltip("How far up and down the object may be tilted.")]
    [Range(10f, 90f)]
    [SerializeField] private float maxPitch = 80f;

    [Header("Placement")]
    [Tooltip("How much of the view the object should fill. 1 = as large as it can be without clipping.")]
    [Range(0.15f, 0.95f)]
    [SerializeField] private float screenFill = 0.55f;

    [Tooltip("The object never comes closer to the camera than this, however small it is.")]
    [SerializeField] private float minDistance = 0.35f;

    [Tooltip("The object is never held further away than this, however big it is.")]
    [SerializeField] private float maxDistance = 5f;

    [Tooltip("Seconds the object takes to travel from where it sits in the world to the camera.")]
    [SerializeField] private float liftDuration = 0.18f;

    [Header("Input")]
    [Tooltip("Also put the object down with Escape, not just with the interact key.")]
    [SerializeField] private bool closeWithEscape = true;

    [Tooltip("Optional. Left empty, the Interact action is looked up on the player's PlayerInput.")]
    [SerializeField] private InputActionReference interactActionReference;

    // ------------------------------------------------------------------ singleton

    private static ItemInspector instance;

    /// <summary>The inspector, created on demand if the scene does not contain one.</summary>
    public static ItemInspector Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<ItemInspector>();

            if (instance == null)
            {
                GameObject host = new GameObject("ItemInspector");
                instance = host.AddComponent<ItemInspector>();
            }

            return instance;
        }
    }

    /// <summary>True while an object is being held up.</summary>
    public static bool IsInspecting => instance != null && instance.currentItem != null;

    /// <summary>
    /// Frame on which the last inspection started or ended. Anything that reacts to the interact
    /// key checks this so one E press cannot both pick up and put down (or both release and sit).
    ///
    /// This is compared with EQUALITY, never with &lt;=: this project runs with Enter Play Mode
    /// Options (domain reload disabled), so a static like this survives between Play sessions,
    /// while Time.frameCount restarts at 0. A stale "4000" would otherwise lock out every
    /// interaction until frame 4000.
    /// </summary>
    public static int ReleaseLockFrame { get; private set; } = -1;

    // Static state survives between Play sessions in this project, so wipe it on every start.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        ReleaseLockFrame = -1;
    }

    // ------------------------------------------------------------------ state

    private static readonly string HoldObjectName = "~InspectHold";

    private InspectableItem currentItem;
    private Transform heldTransform;
    private Transform holdPivot;

    private Transform originalParent;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private Vector3 originalLocalScale;

    private Collider[] heldColliders;
    private bool[] heldColliderStates;
    private Rigidbody heldBody;
    private bool bodyWasKinematic;
    private bool bodyUsedGravity;

    private FirstPersonController player;
    private bool playerWasEnabled;
    private Transform viewCamera;
    private Camera viewCameraComponent;

    private InputAction interactAction;

    private Vector3 startCenter;
    private float holdDistance;
    private float liftElapsed;
    private float targetYaw;
    private float targetPitch;

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
    }

    void Start()
    {
        ResolvePlayer();
        ConnectInteractInput();
    }

    void OnDisable()
    {
        // Being switched off mid-inspection would otherwise leave the object floating
        // in the air with the player frozen.
        if (currentItem != null) StopInspect();
    }

    void OnDestroy()
    {
        if (interactAction != null) interactAction.performed -= OnInteractPressed;
        if (instance == this) instance = null;
    }

    // ------------------------------------------------------------------ public API

    /// <summary>Lifts the item in front of the camera. Called by InspectableItem.Interact().</summary>
    public void BeginInspect(InspectableItem item)
    {
        if (item == null || currentItem != null) return;
        if (Time.frameCount == ReleaseLockFrame) return;

        // While seated the interact key has to stay "stand up". Inspecting from a chair would
        // make one E press do two things (put the object down and stand up at the same time).
        if (ChairSeat.IsSeated) return;

        // The same E press that picked the object up also reaches our own Interact handler
        // (and any other listener). Latch the frame so this press cannot also put it down.
        ReleaseLockFrame = Time.frameCount;

        Transform target = item.transform;
        if (target == null) return;

        if (!ResolvePlayer()) return;

        // ---- remember everything we are about to change ----
        currentItem = item;
        heldTransform = target;
        originalParent = target.parent;
        originalLocalPosition = target.localPosition;
        originalLocalRotation = target.localRotation;
        originalLocalScale = target.localScale;

        heldColliders = target.GetComponentsInChildren<Collider>(true);
        heldColliderStates = new bool[heldColliders.Length];
        for (int i = 0; i < heldColliders.Length; i++)
        {
            heldColliderStates[i] = heldColliders[i].enabled;
            heldColliders[i].enabled = false;
        }

        heldBody = target.GetComponent<Rigidbody>();
        if (heldBody != null)
        {
            bodyWasKinematic = heldBody.isKinematic;
            bodyUsedGravity = heldBody.useGravity;
            heldBody.isKinematic = true;
            heldBody.useGravity = false;
        }

        // ---- freeze the player (look and movement both live in this component) ----
        playerWasEnabled = player.enabled;
        player.enabled = false;

        // ---- bounds decide where the object is centred and how close it is held ----
        if (!TryGetWorldBounds(target.gameObject, out Bounds bounds))
        {
            bounds = new Bounds(target.position, Vector3.one * 0.2f);
        }

        startCenter = bounds.center;
        float radius = Mathf.Max(0.02f, bounds.extents.magnitude);

        float halfFov = viewCameraComponent.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float fitDistance = radius / Mathf.Sin(Mathf.Clamp(screenFill, 0.05f, 0.95f) * halfFov);
        float nearSafe = viewCameraComponent.nearClipPlane + radius * 0.5f + 0.02f;
        holdDistance = Mathf.Clamp(Mathf.Max(fitDistance, nearSafe), minDistance, maxDistance);

        // ---- world-space pivot sitting exactly on the object's bounds centre ----
        holdPivot = new GameObject(HoldObjectName).transform;
        holdPivot.position = startCenter;
        holdPivot.rotation = viewCamera.rotation;

        // worldPositionStays: true, so nothing jumps when it is picked up.
        heldTransform.SetParent(holdPivot, true);

        targetYaw = 0f;
        targetPitch = 0f;
        liftElapsed = 0f;
    }

    /// <summary>Puts the held object back exactly where it came from.</summary>
    public void StopInspect()
    {
        Transform objectTransform = heldTransform;
        Transform pivot = holdPivot;

        // Release the player first: even if the object has been destroyed meanwhile,
        // the player must never be left frozen.
        if (player != null) player.enabled = playerWasEnabled;

        if (objectTransform != null)
        {
            objectTransform.SetParent(originalParent, true);
            objectTransform.localPosition = originalLocalPosition;
            objectTransform.localRotation = originalLocalRotation;
            objectTransform.localScale = originalLocalScale;

            if (heldColliders != null)
            {
                for (int i = 0; i < heldColliders.Length; i++)
                {
                    if (heldColliders[i] != null) heldColliders[i].enabled = heldColliderStates[i];
                }
            }

            if (heldBody != null)
            {
                heldBody.isKinematic = bodyWasKinematic;
                heldBody.useGravity = bodyUsedGravity;
            }
        }

        if (pivot != null) Destroy(pivot.gameObject);

        currentItem = null;
        heldTransform = null;
        holdPivot = null;
        heldColliders = null;
        heldColliderStates = null;
        heldBody = null;

        ReleaseLockFrame = Time.frameCount;

        Physics.SyncTransforms();
    }

    // ------------------------------------------------------------------ update

    void Update()
    {
        if (currentItem == null || heldTransform == null)
        {
            if (currentItem != null) StopInspect(); // object was destroyed while held
            return;
        }

        if (closeWithEscape && Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame &&
            Time.frameCount != ReleaseLockFrame)
        {
            StopInspect();
            return;
        }

        // Raw key fallback. It is always checked, not only when the Interact action could not
        // be found: PlayerInput clones its action asset, so the "Interact" action we can reach
        // through it may not be the same instance PlayerInteractor listens to. Pressing E twice
        // is harmless anyway — the frame latch means only the first handler acts.
        if (Keyboard.current != null &&
            Keyboard.current.eKey.wasPressedThisFrame &&
            Time.frameCount != ReleaseLockFrame)
        {
            StopInspect();
            return;
        }

        ReadRotationInput();
    }

    void LateUpdate()
    {
        if (currentItem == null || heldTransform == null || holdPivot == null || viewCamera == null) return;

        // ---- fly the object in front of the camera ----
        liftElapsed += Time.deltaTime;
        float t = liftDuration <= 0.0001f ? 1f : Mathf.Clamp01(liftElapsed / liftDuration);
        float eased = t * t * (3f - 2f * t);

        Vector3 holdPoint = viewCamera.position + viewCamera.forward * holdDistance;
        holdPivot.position = Vector3.Lerp(startCenter, holdPoint, eased);

        // ---- turn it with the mouse ----
        Quaternion targetRotation =
            viewCamera.rotation * Quaternion.Euler(targetPitch, targetYaw, 0f);

        float blend = 1f - Mathf.Exp(-Mathf.Max(0.01f, rotationSmoothing) * Time.deltaTime);
        holdPivot.rotation = Quaternion.Slerp(holdPivot.rotation, targetRotation, blend);
    }

    // ------------------------------------------------------------------ input

    void ReadRotationInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 delta = mouse.delta.ReadValue();

        targetYaw += delta.x * rotationSensitivity;
        targetPitch -= delta.y * rotationSensitivity;
        targetPitch = Mathf.Clamp(targetPitch, -Mathf.Abs(maxPitch), Mathf.Abs(maxPitch));
    }

    void ConnectInteractInput()
    {
        if (interactAction != null) return;

        InputAction action = null;

        if (interactActionReference != null) action = interactActionReference.action;

        if (action == null)
        {
            PlayerInput playerInput = player != null
                ? player.GetComponent<PlayerInput>()
                : FindAnyObjectByType<PlayerInput>();

            if (playerInput != null && playerInput.actions != null)
            {
                action = playerInput.actions.FindAction("Interact", false);
            }
        }

        if (action == null) return;

        interactAction = action;
        interactAction.Enable();
        interactAction.performed += OnInteractPressed;
    }

    void OnInteractPressed(InputAction.CallbackContext context)
    {
        // This same action also starts an inspection (PlayerInteractor calls Interact()).
        // Only react to it here when something is already being held.
        if (currentItem == null) return;
        if (Time.frameCount == ReleaseLockFrame) return;

        StopInspect();
    }

    // ------------------------------------------------------------------ helpers

    bool ResolvePlayer()
    {
        if (player == null) player = FindAnyObjectByType<FirstPersonController>();
        if (player == null)
        {
            Debug.LogWarning("ItemInspector: no FirstPersonController found in the scene.");
            return false;
        }

        if (viewCamera == null) viewCamera = player.cam != null ? player.cam : null;
        if (viewCamera == null)
        {
            Camera main = Camera.main;
            if (main != null) viewCamera = main.transform;
        }

        if (viewCamera == null || viewCamera.GetComponent<Camera>() == null)
        {
            Debug.LogWarning("ItemInspector: the player has no camera assigned.");
            return false;
        }

        viewCameraComponent = viewCamera.GetComponent<Camera>();

        if (interactAction == null) ConnectInteractInput();
        return true;
    }

    static bool TryGetWorldBounds(GameObject root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

        bool found = false;
        bounds = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (renderer is LineRenderer || renderer is TrailRenderer || renderer is ParticleSystemRenderer) continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!found)
        {
            Collider collider = root.GetComponentInChildren<Collider>(true);
            if (collider != null)
            {
                bounds = collider.bounds;
                found = true;
            }
        }

        return found;
    }
}
