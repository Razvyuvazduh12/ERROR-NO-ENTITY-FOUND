using UnityEngine;

/// <summary>Where one letter ended up.</summary>
public struct SurfacePose
{
    /// <summary>World position, already pushed off the surface by SurfaceSnapSettings.surfaceOffset.</summary>
    public Vector3 position;

    /// <summary>
    /// World rotation. The sprite's visible face is local -Z, so this makes -Z point along the
    /// surface normal — out of the wall, towards whoever is reading it.
    /// </summary>
    public Quaternion rotation;

    /// <summary>False when no surface was found and the letter stayed on the current plane.</summary>
    public bool snapped;
}

/// <summary>
/// The frame a letter is being drawn in.
/// normal points OUT of the surface (towards the reader); right/up are the letter's own axes on
/// that surface, so the text keeps reading left-to-right / top-to-bottom after a corner.
/// </summary>
public struct SurfaceFrame
{
    public Vector3 normal;
    public Vector3 right;
    public Vector3 up;

    public SurfaceFrame(Vector3 normal, Vector3 right, Vector3 up)
    {
        this.normal = normal;
        this.right = right;
        this.up = up;
    }
}

/// <summary>
/// Turns the flat grid the dialogue scripts already compute into poses on real geometry.
///
/// One instance is created per rebuild and fed each letter's flat LOCAL point, in order. It keeps
/// the surface frame (normal / right / up) as it walks, so when the text runs off the end of a wall
/// it can rotate its probe onto the neighbouring face and carry on drawing there — a cube gives it
/// six faces to work with, a wall gives it the floor and the next wall.
///
/// Deliberately stateful (not static), so SentenceBuilder and ResponseButtonGroup each run their
/// own walk without stepping on each other.
/// </summary>
public class TextLayoutEngine
{
    // A hit counts as "the same face" above this dot product (about 25 degrees of deviation).
    private const float SameFaceDot = 0.9f;
    // A ray hit counts as a FRONT face when the ray direction faces the surface normal.
    private const float FrontFaceDot = 0.05f;

    // Shared scratch buffer for RaycastNonAlloc — single-threaded, and this avoids allocating a
    // RaycastHit[] per probe (the engine runs on every rebuild, including live in the editor).
    private static readonly RaycastHit[] HitBuffer = new RaycastHit[32];

    private readonly Transform owner;
    private readonly SurfaceSnapSettings settings;
    private readonly Transform ignoreRoot;

    private bool started;
    private SurfaceFrame frame;

    // The flat layout's own axes, captured once. Advance distances are measured against these so
    // that after a corner the text keeps walking along the NEW face instead of flying off in the
    // direction the old wall happened to face.
    private Vector3 baseRight;
    private Vector3 baseUp;

    private Vector3 lastFlatWorld;
    private Vector3 lastSurface;
    private Vector3 lastSurfacePoint;

    public TextLayoutEngine(Transform owner, SurfaceSnapSettings settings, Transform ignoreRoot = null)
    {
        this.owner = owner;
        this.settings = settings != null ? settings : new SurfaceSnapSettings();
        this.ignoreRoot = ignoreRoot != null ? ignoreRoot : owner;
    }

    public void Reset()
    {
        started = false;
    }

    /// <summary>
    /// Returns where the next letter should be drawn. Pass this object's own local-space grid
    /// position (the same numbers the flat layout would have used for localPosition).
    /// </summary>
    public SurfacePose PlaceNext(Vector3 flatLocalPoint)
    {
        Vector3 flatWorld = owner.TransformPoint(flatLocalPoint);

        if (!started)
        {
            started = true;
            baseRight = SafeNormalize(owner.right, Vector3.right);
            baseUp = SafeNormalize(owner.up, Vector3.up);
            frame = OrthonormalFrame(-owner.forward, owner.right);
            lastFlatWorld = flatWorld;

            SurfacePose first = Resolve(flatWorld, 0f);
            lastSurfacePoint = lastSurface;
            return first;
        }

        Vector3 delta = flatWorld - lastFlatWorld;
        lastFlatWorld = flatWorld;

        float du = Vector3.Dot(delta, baseRight);
        float dv = Vector3.Dot(delta, baseUp);

        Vector3 ideal = lastSurfacePoint + frame.right * du + frame.up * dv;

        SurfacePose pose = Resolve(ideal, new Vector2(du, dv).magnitude);
        lastSurfacePoint = lastSurface;
        return pose;
    }

    /// <summary>One-shot probe, used to drop a whole object (a response button) onto a surface.</summary>
    public SurfacePose ProbeSingle(Vector3 idealWorld, SurfaceFrame startFrame)
    {
        started = true;
        frame = startFrame;
        baseRight = startFrame.right;
        baseUp = startFrame.up;
        lastFlatWorld = idealWorld;

        SurfacePose pose = Resolve(idealWorld, 0f);
        lastSurfacePoint = lastSurface;
        return pose;
    }

    // ------------------------------------------------------------------ core

    private SurfacePose Resolve(Vector3 ideal, float advance)
    {
        SyncForRaycasts();

        RaycastHit hit;

        // 1) Straight down the current normal. Handles flat walls and gentle curvature:
        //    every letter re-finds its own point on a cylinder and re-orients to that point.
        if (TryStraightProbe(ideal, frame.normal, out hit))
        {
            SurfaceFrame f = Carry(frame, hit.normal);
            frame = f;
            lastSurface = hit.point;
            return MakePose(hit.point, f);
        }

        // 2) Nothing there — rotate the probe until it finds a different face, then continue the
        //    text ON that face, carrying the leftover distance across the corner.
        if (settings.turnCorners && TryTurnProbe(ideal, advance, out hit))
        {
            Vector3 leftover = ideal - hit.point;
            SurfaceFrame f = Carry(frame, hit.normal);

            // Measure against the OLD axes, place along the NEW ones: that is the actual "wrap".
            Vector3 p = hit.point
                        + f.right * Vector3.Dot(leftover, frame.right)
                        + f.up * Vector3.Dot(leftover, frame.up);

            frame = f;
            lastSurface = p;
            return MakePose(p, f);
        }

        // 3) No surface anywhere even after turning. Keep the walk going on the current plane so
        //    the sentence still reads in order instead of collapsing onto one point.
        lastSurface = ideal;
        return new SurfacePose
        {
            position = ideal + frame.normal * settings.surfaceOffset,
            rotation = RotationFor(frame),
            snapped = false
        };
    }

    private bool TryStraightProbe(Vector3 ideal, Vector3 normal, out RaycastHit hit)
    {
        // Start a probeDepth in front of the letter and look back through the plane it sits on,
        // so the letter may be slightly in front of OR slightly inside the surface and still snap.
        Vector3 origin = ideal + normal * settings.probeDepth;

        // Front faces only: the far side of a wall is not a surface.
        return TryRaycast(origin, -normal, settings.probeDepth * 2f, true, false, out hit);
    }

    /// <summary>
    /// Nearest usable hit along a ray. Skipped colliders (the player, our own letters/buttons) do
    /// not block the ray — we just keep walking the hit list instead of giving up on the first one.
    /// </summary>
    private bool TryRaycast(Vector3 origin, Vector3 dir, float distance, bool frontOnly,
                            bool requireDifferentFace, out RaycastHit hit)
    {
        hit = default;

        int count = Physics.RaycastNonAlloc(origin, dir, HitBuffer, distance, settings.surfaceMask,
                                            QueryTriggerInteraction.Ignore);
        if (count <= 0) return false;

        bool found = false;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit h = HitBuffer[i];
            if (!IsUsable(h)) continue;

            Vector3 n = h.normal.normalized;

            if (frontOnly && Vector3.Dot(dir, n) > FrontFaceDot) continue;

            // For a corner turn the hit has to be a genuinely DIFFERENT face; the same wall
            // slightly out of reach is not a turn.
            if (requireDifferentFace && Mathf.Abs(Vector3.Dot(n, frame.normal)) > SameFaceDot) continue;

            if (h.distance >= bestDistance) continue;

            bestDistance = h.distance;
            hit = h;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// Fans the probe out around the current frame looking for a different face. Front faces win;
    /// a second pass also accepts back faces, which is what an inside corner gives you (the ray
    /// starts inside the neighbouring wall and exits through the face you can see).
    /// </summary>
    private bool TryTurnProbe(Vector3 ideal, float advance, out RaycastHit best)
    {
        best = default;

        float distance = settings.probeDepth * 2f + advance + 0.01f;
        float step = Mathf.Max(5f, settings.turnStep);

        // Closest angle first, so the smallest fold wins instead of some faraway wall.
        for (int pass = 0; pass < 2; pass++)
        {
            bool frontOnly = pass == 0;

            for (float angle = step; angle <= settings.maxTurnAngle + 0.001f; angle += step)
            {
                for (int a = 0; a < 2; a++)
                {
                    Vector3 axis = a == 0 ? frame.up : frame.right;

                    for (int s = 0; s < 2; s++)
                    {
                        float sign = s == 0 ? 1f : -1f;
                        Vector3 dir = Quaternion.AngleAxis(sign * angle, axis) * (-frame.normal);

                        RaycastHit hit;
                        if (!TryRaycast(ideal, dir, distance, frontOnly, true, out hit)) continue;

                        best = hit;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool IsUsable(RaycastHit hit)
    {
        Collider c = hit.collider;
        if (c == null) return false;
        if (c.isTrigger) return false;
        if (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot)) return false;
        if (c.CompareTag("Player")) return false;
        return true;
    }

    // ------------------------------------------------------------------ helpers

    private SurfacePose MakePose(Vector3 surfacePoint, SurfaceFrame f)
    {
        return new SurfacePose
        {
            position = surfacePoint + f.normal * settings.surfaceOffset,
            rotation = RotationFor(f),
            snapped = true
        };
    }

    /// <summary>
    /// Moves the frame onto a new face. Rotating the old frame is what keeps the letters upright
    /// and readable across a 90 degree fold instead of spinning them randomly.
    /// </summary>
    private static SurfaceFrame Carry(SurfaceFrame from, Vector3 newNormalRaw)
    {
        Vector3 n = SafeNormalize(newNormalRaw, from.normal);
        if (Vector3.Dot(n, from.normal) > 0.9999f) return from;

        Vector3 r = Quaternion.FromToRotation(from.normal, n) * from.right;
        return OrthonormalFrame(n, r);
    }

    /// <summary>Builds a clean, right-handed frame (normal, right, up) with up = right x normal.</summary>
    private static SurfaceFrame OrthonormalFrame(Vector3 normalHint, Vector3 rightHint)
    {
        Vector3 n = SafeNormalize(normalHint, Vector3.forward);

        Vector3 r = Vector3.ProjectOnPlane(rightHint, n);
        if (r.sqrMagnitude < 1e-6f)
        {
            Vector3 fallback = Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up;
            r = Vector3.Cross(n, fallback);
        }
        r.Normalize();

        Vector3 u = Vector3.Cross(r, n).normalized;
        return new SurfaceFrame(n, r, u);
    }

    private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        if (v.sqrMagnitude < 1e-10f) return fallback;
        return v.normalized;
    }

    private static void SyncForRaycasts()
    {
        // In the editor the scene transform graph is not necessarily pushed into PhysX yet, so
        // an [ExecuteAlways] preview would otherwise raycast against stale collider positions.
        if (!Application.isPlaying) Physics.SyncTransforms();
    }

    // ------------------------------------------------------------------ public statics

    /// <summary>Frame implied by a transform: its visible face (local -Z) is the outward normal.</summary>
    public static SurfaceFrame FrameFromTransform(Transform t)
    {
        return OrthonormalFrame(-t.forward, t.right);
    }

    /// <summary>Letter rotation for a frame: local -Z along the normal, local +Y along up.</summary>
    public static Quaternion RotationFor(SurfaceFrame f)
    {
        Vector3 forward = -f.normal;
        if (forward.sqrMagnitude < 1e-8f) return Quaternion.identity;
        return Quaternion.LookRotation(forward, f.up);
    }

    /// <summary>
    /// Drops a whole object onto a surface in one go (used for response buttons, whose collider has
    /// to land on the wall together with the letters). Returns false and leaves it alone if there
    /// is nothing in front of it.
    /// </summary>
    public static bool SnapTransform(Transform target, SurfaceSnapSettings settings, Transform ignoreRoot)
    {
        if (target == null || settings == null || !settings.stickToSurfaces) return false;

        TextLayoutEngine engine = new TextLayoutEngine(target, settings, ignoreRoot);
        SurfacePose pose = engine.ProbeSingle(target.position, FrameFromTransform(target));

        if (!pose.snapped) return false;

        target.SetPositionAndRotation(pose.position, pose.rotation);
        return true;
    }
}
