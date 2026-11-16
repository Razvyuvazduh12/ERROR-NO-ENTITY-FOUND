using UnityEngine;

/// <summary>
/// Brings the grandfather clock to life: the three hands sweep the dial and the pendulum swings
/// inside the case. It is deliberately not a real clock — it never reads the system time, it just
/// counts up from the moment you press Play.
///
/// SETUP
///   1. Drop this on the clock's root GameObject (the "grand_clock" object the FBX imports as).
///   2. Press Play. Every part is found by name, so nothing has to be dragged into the inspector.
///      If you rename something, just assign the reference yourself.
///
/// WHAT IT DOES NOT TOUCH
///   Nothing is reparented, no mesh is edited and no material is created, so the model can still
///   be changed in Blender and re-imported. Each frame it only writes transform.position and
///   transform.rotation. The "rest pose" is captured once when Play starts, and every angle is
///   added on top of it — so the hands keep the pose the model was saved in (about 10:10) and
///   simply advance from there.
///
/// WHY IT IS WRITTEN THE WAY IT IS
///   Hands    — they are separate objects whose pivots already sit at the dial centre, so each one
///              is simply rotated about the direction the dial faces. That direction is worked out
///              from the hand mesh itself (a hand is a flat blade, so its thinnest local axis is
///              the dial normal) and then converted into the hand's PARENT space, because that is
///              the space localRotation lives in. Doing it in parent space means it survives
///              whatever rotation the FBX importer happened to put on the nodes.
///   Pendulum — the rod's pivot is in the MIDDLE of the rod, not at the top, so rotating it in
///              place would swing it around its centre like a propeller. Instead the rod and the
///              bob are both positioned every frame by rotating them around the suspension point
///              at the top of the rod, which is measured from the rod's own mesh bounds.
/// </summary>
[DisallowMultipleComponent]
public class ClockSwing : MonoBehaviour
{
    [Header("Parts — leave empty and they are found by name")]
    [Tooltip("The case. Used to work out which side the dial faces and how wide the inside is.")]
    public Transform clockBody;

    [Tooltip("The rod the pendulum bob hangs from. Its pivot does not have to be at the top.")]
    public Transform pendulumRod;

    [Tooltip("The round weight at the bottom of the pendulum.")]
    public Transform pendulumBob;

    public Transform hourHand;
    public Transform minuteHand;
    public Transform secondHand;

    [Header("Hands")]
    [Tooltip("How fast the fake clock runs. 1 = real seconds. 60 makes the second hand do a full " +
             "turn every second, which is handy for checking the motion.")]
    [Min(0f)]
    public float timeScale = 1f;

    [Tooltip("Second hand jumps a whole second at a time instead of creeping round.")]
    public bool tickSecondHand = true;

    [Tooltip("Untick if the hands sweep backwards. The dial normal was then guessed pointing the " +
             "wrong way — see Flip Dial Normal.")]
    public bool clockwise = true;

    [Tooltip("Reverses the guessed dial normal. Only touch this if the hands spin the wrong way " +
             "or the pendulum swings towards/away from the clock instead of side to side.")]
    public bool flipDialNormal = false;

    [Header("Pendulum")]
    [Tooltip("How far the pendulum swings to each side, in degrees. 4-8 looks like a real clock; " +
             "much more and the bob starts hitting the case.")]
    [Range(0f, 30f)]
    public float swingDegrees = 7f;

    [Tooltip("Seconds for one complete left-right-left swing. ~2 s is the real thing.")]
    [Min(0.1f)]
    public float swingPeriod = 2f;

    [Tooltip("Stops the bob from ending up outside the case. It measures the case's overall " +
             "bounding box, which is wider than the trunk, so treat it as a safety net rather " +
             "than a guarantee — select the clock and the gizmo shows the real swing arc.")]
    public bool clampSwingToCase = true;

    [Tooltip("Gap kept between the bob and the case wall, in world units.")]
    [Min(0f)]
    public float caseMargin = 0.02f;

    // ------------------------------------------------------------------ resolved state

    private Vector3 dialNormal;                 // world space, points out of the dial
    private Vector3 pivot;                      // world space, the pendulum suspension point
    private Vector3 rodRestPos, bobRestPos;
    private Quaternion rodRestRot, bobRestRot;
    private Quaternion hourRestRot, minuteRestRot, secondRestRot;
    private Vector3 hourAxis, minuteAxis, secondAxis;   // dial normal, in each hand's parent space
    private float swingLimit;                   // degrees, after the case clamp
    private bool ready;

    // ------------------------------------------------------------------ runtime

    private float clockSeconds;                 // drives the hands, follows timeScale
    private float swingSeconds;                 // drives the pendulum, always real seconds

    void Start()
    {
        ready = Resolve(true);

        if (!ready)
        {
            Debug.LogError(
                $"ClockSwing on '{name}': found no hands and no pendulum under this object. Put " +
                "the script on the clock's root GameObject, or assign the parts in the inspector.",
                this);
            enabled = false;
            return;
        }

        Vector3 scale = transform.lossyScale;
        if (Mathf.Abs(scale.x - scale.y) > 0.001f || Mathf.Abs(scale.y - scale.z) > 0.001f)
        {
            Debug.LogWarning(
                $"ClockSwing on '{name}': the clock is scaled to {scale}. A non-uniform scale on " +
                "a parent shears anything rotated under it, so the hands and the pendulum may look " +
                "squashed. Scale the clock evenly.", this);
        }
    }

    void Update()
    {
        if (!ready) return;

        float dt = Time.deltaTime;
        clockSeconds += dt * Mathf.Max(0f, timeScale);
        swingSeconds += dt;      // the pendulum keeps real time even if you speed the hands up

        AnimateHands();
        AnimatePendulum();
    }

    // ------------------------------------------------------------------ hands

    void AnimateHands()
    {
        if (hourHand == null && minuteHand == null && secondHand == null) return;

        // Positive rotation about the dial normal is anticlockwise for someone standing in front
        // of the clock, so going clockwise means negating the angle.
        float sign = clockwise ? -1f : 1f;
        float t = clockSeconds;

        if (secondHand != null)
        {
            // 360 degrees per 60 seconds
            float shown = tickSecondHand ? Mathf.Floor(t) : t;
            secondHand.localRotation = Quaternion.AngleAxis(sign * 6f * shown, secondAxis) * secondRestRot;
        }

        if (minuteHand != null)
        {
            // 360 degrees per 3600 seconds
            minuteHand.localRotation = Quaternion.AngleAxis(sign * 0.1f * t, minuteAxis) * minuteRestRot;
        }

        if (hourHand != null)
        {
            // 360 degrees per 12 hours = 43200 seconds
            hourHand.localRotation = Quaternion.AngleAxis(sign * (360f / 43200f) * t, hourAxis) * hourRestRot;
        }
    }

    // ------------------------------------------------------------------ pendulum

    void AnimatePendulum()
    {
        if (pendulumRod == null && pendulumBob == null) return;

        float period = Mathf.Max(0.1f, swingPeriod);
        float angle = swingLimit * Mathf.Sin(swingSeconds / period * Mathf.PI * 2f);

        // One rotation about the suspension point, applied to both parts. This is what keeps the
        // top of the rod nailed in place despite the rod's own pivot being in the middle of it.
        Quaternion swing = Quaternion.AngleAxis(angle, dialNormal);

        if (pendulumRod != null)
        {
            pendulumRod.position = pivot + swing * (rodRestPos - pivot);
            pendulumRod.rotation = swing * rodRestRot;
        }

        if (pendulumBob != null)
        {
            pendulumBob.position = pivot + swing * (bobRestPos - pivot);
            pendulumBob.rotation = swing * bobRestRot;
        }
    }

    // ------------------------------------------------------------------ resolving the rig

    /// <summary>
    /// Finds the parts, works out the dial direction and the pendulum pivot, and applies the case
    /// clamp. Safe to call again at any time. <paramref name="captureRestPose"/> must only be true
    /// when the clock is standing at rest, otherwise the current animated pose becomes the baseline.
    /// </summary>
    bool Resolve(bool captureRestPose)
    {
        if (clockBody == null) clockBody = FindPart("Clock_Body", "Body", "Case");
        if (pendulumRod == null) pendulumRod = FindPart("Pendulum_Rod", "PendulumRod", "Rod");
        if (pendulumBob == null) pendulumBob = FindPart("Pendulum_Bob", "PendulumBob", "Bob");
        if (hourHand == null) hourHand = FindPart("Hand_Hour", "HourHand");
        if (minuteHand == null) minuteHand = FindPart("Hand_Minute", "MinuteHand");
        if (secondHand == null) secondHand = FindPart("Hand_Second", "SecondHand");

        bool havePendulum = pendulumRod != null && pendulumBob != null;
        bool haveHands = hourHand != null || minuteHand != null || secondHand != null;

        if (!havePendulum && !haveHands) return false;

        dialNormal = FindDialNormal();

        if (hourHand != null) hourAxis = AxisInParentSpace(hourHand);
        if (minuteHand != null) minuteAxis = AxisInParentSpace(minuteHand);
        if (secondHand != null) secondAxis = AxisInParentSpace(secondHand);

        if (havePendulum) pivot = FindPendulumPivot();

        if (captureRestPose)
        {
            if (pendulumRod != null) { rodRestPos = pendulumRod.position; rodRestRot = pendulumRod.rotation; }
            if (pendulumBob != null) { bobRestPos = pendulumBob.position; bobRestRot = pendulumBob.rotation; }
            if (hourHand != null) hourRestRot = hourHand.localRotation;
            if (minuteHand != null) minuteRestRot = minuteHand.localRotation;
            if (secondHand != null) secondRestRot = secondHand.localRotation;
        }

        swingLimit = Mathf.Max(0f, swingDegrees);

        if (havePendulum && clampSwingToCase && clockBody != null)
        {
            LimitSwingToCase(captureRestPose);
        }

        return true;
    }

    /// <summary>
    /// The direction the dial faces. A clock hand is a flat blade, so its thinnest local axis is
    /// the dial normal; the mesh is rotated by the importer, so that axis has to be pushed into
    /// world space. The result is then flipped to point away from the case towards the hands,
    /// because a normal has no sign of its own.
    /// </summary>
    Vector3 FindDialNormal()
    {
        Vector3 normal = Vector3.zero;
        bool found = false;

        Transform blade = hourHand != null ? hourHand : (minuteHand != null ? minuteHand : secondHand);
        Renderer bladeRenderer = RendererOf(blade);

        if (bladeRenderer != null)
        {
            Vector3 thin = ThinnestLocalAxis(bladeRenderer.localBounds.size);
            normal = blade.TransformDirection(thin);
            found = normal.sqrMagnitude > 1e-8f;
        }

        if (!found && clockBody != null)
        {
            normal = clockBody.forward;
            found = true;
        }

        if (!found) normal = transform.forward;

        normal.Normalize();

        // Point it at the side the hands are on. The case pivot is in the middle of the trunk,
        // so "from the case towards a hand" is the way the dial is looking.
        if (blade != null && clockBody != null)
        {
            if (Vector3.Dot(normal, blade.position - clockBody.position) < 0f) normal = -normal;
        }

        if (flipDialNormal) normal = -normal;

        return normal;
    }

    /// <summary>
    /// The suspension point at the top of the rod. The rod's mesh is a thin bar, so its longest
    /// local axis is the way it hangs; of the two ends, the one higher up in the world is the
    /// point the pendulum is meant to swing from.
    /// </summary>
    Vector3 FindPendulumPivot()
    {
        Renderer rodRenderer = RendererOf(pendulumRod);
        if (rodRenderer == null) return pendulumRod.position;

        Bounds lb = rodRenderer.localBounds;
        Vector3 longAxis = LongestLocalAxis(lb.size);
        Vector3 half = Vector3.Scale(lb.extents, longAxis);   // only the long component survives

        Vector3 endA = pendulumRod.TransformPoint(lb.center + half);
        Vector3 endB = pendulumRod.TransformPoint(lb.center - half);

        return endA.y >= endB.y ? endA : endB;
    }

    /// <summary>
    /// Shrinks the swing until the bob's outer edge clears the inside of the case. The distance
    /// from the pivot to the bob is the arc radius, so the largest angle whose sideways travel
    /// still fits is asin(room / radius).
    /// </summary>
    void LimitSwingToCase(bool verbose)
    {
        Vector3 toBob = pendulumBob.position - pivot;
        float radius = toBob.magnitude;

        if (radius < 1e-4f) return;

        // Sideways direction inside the dial plane: across the rod, perpendicular to the dial.
        Vector3 swingDir = Vector3.Cross(dialNormal, toBob).normalized;
        if (swingDir.sqrMagnitude < 1e-8f) return;

        float caseHalf = HalfExtentAlong(clockBody, swingDir);
        float bobHalf = HalfExtentAlong(pendulumBob, swingDir);
        float room = caseHalf - bobHalf - Mathf.Max(0f, caseMargin);

        if (room <= 0f)
        {
            if (verbose)
            {
                Debug.LogWarning(
                    $"ClockSwing on '{name}': the bob does not fit inside the case, so the " +
                    "pendulum will not swing at all. Turn Clamp Swing To Case off to ignore this.",
                    this);
            }

            swingLimit = 0f;
            return;
        }

        float maxAngle = Mathf.Asin(Mathf.Clamp01(room / radius)) * Mathf.Rad2Deg;

        if (swingLimit > maxAngle)
        {
            if (verbose)
            {
                Debug.Log(
                    $"ClockSwing on '{name}': a {swingDegrees:0.#}° swing would take the bob out " +
                    $"of the case, so it is being limited to {maxAngle:0.#}°.", this);
            }

            swingLimit = maxAngle;
        }
    }

    // ------------------------------------------------------------------ gizmos

    void OnDrawGizmosSelected()
    {
        if (!Resolve(false)) return;

        if (pendulumRod != null && pendulumBob != null)
        {
            Vector3 toBob = pendulumBob.position - pivot;
            float knob = Mathf.Max(toBob.magnitude * 0.015f, 0.00001f);

            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.95f);
            Gizmos.DrawWireSphere(pivot, knob);

            if (swingLimit > 0.01f)
            {
                Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.85f);
                Gizmos.DrawLine(pivot, pivot + Quaternion.AngleAxis(swingLimit, dialNormal) * toBob);
                Gizmos.DrawLine(pivot, pivot + Quaternion.AngleAxis(-swingLimit, dialNormal) * toBob);

                // The arc the bob's centre travels, in 16 slices.
                Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.45f);
                Vector3 previous = pivot + Quaternion.AngleAxis(-swingLimit, dialNormal) * toBob;
                for (int i = 1; i <= 16; i++)
                {
                    float a = Mathf.Lerp(-swingLimit, swingLimit, i / 16f);
                    Vector3 next = pivot + Quaternion.AngleAxis(a, dialNormal) * toBob;
                    Gizmos.DrawLine(previous, next);
                    previous = next;
                }
            }

            Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.85f);
            Gizmos.DrawLine(pivot, dialNormal * knob * 6f + pivot);
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The dial normal expressed in a transform's PARENT space — the space localRotation is written
    /// in. For the hands that is the clock root, so the animation does not care how the importer
    /// rotated the individual nodes.
    /// </summary>
    Vector3 AxisInParentSpace(Transform t)
    {
        Transform parent = t.parent;
        if (parent == null) return dialNormal;

        Vector3 axis = parent.InverseTransformDirection(dialNormal);
        return axis.sqrMagnitude < 1e-8f ? Vector3.forward : axis.normalized;
    }

    /// <summary>How far a renderer's bounding box reaches along a world-space direction.</summary>
    static float HalfExtentAlong(Transform t, Vector3 dir)
    {
        Renderer r = RendererOf(t);
        if (r == null) return 0f;

        Vector3 e = r.bounds.extents;   // half size of the world-space AABB
        return Mathf.Abs(e.x * dir.x) + Mathf.Abs(e.y * dir.y) + Mathf.Abs(e.z * dir.z);
    }

    static Renderer RendererOf(Transform t)
    {
        if (t == null) return null;

        Renderer r = t.GetComponent<Renderer>();
        return r != null ? r : t.GetComponentInChildren<Renderer>();
    }

    static Vector3 ThinnestLocalAxis(Vector3 size)
    {
        if (size.x <= size.y && size.x <= size.z) return Vector3.right;
        return size.y <= size.z ? Vector3.up : Vector3.forward;
    }

    static Vector3 LongestLocalAxis(Vector3 size)
    {
        if (size.x >= size.y && size.x >= size.z) return Vector3.right;
        return size.y >= size.z ? Vector3.up : Vector3.forward;
    }

    Transform FindPart(params string[] wanted)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform found = SearchDeep(transform.GetChild(i), wanted);
            if (found != null) return found;
        }

        return null;
    }

    static Transform SearchDeep(Transform t, string[] wanted)
    {
        string name = Normalise(t.name);

        for (int i = 0; i < wanted.Length; i++)
        {
            if (name.StartsWith(Normalise(wanted[i]))) return t;
        }

        for (int i = 0; i < t.childCount; i++)
        {
            Transform found = SearchDeep(t.GetChild(i), wanted);
            if (found != null) return found;
        }

        return null;
    }

    /// <summary>"Hand_Hour", "hand hour" and "HandHour (1)" all become "handhour".</summary>
    static string Normalise(string s)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);

        foreach (char c in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Call this after moving or rebuilding the clock while the game is running. It forgets the
    /// pose captured at Start and takes the current one as the new rest pose.
    /// </summary>
    [ContextMenu("Re-capture rest pose")]
    public void RecaptureRestPose()
    {
        ready = Resolve(true);
    }
}
