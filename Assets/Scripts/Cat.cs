using UnityEngine;

// Makes a cat stare at the player with its head, blink, and meow.
// Head + eyeballs are found by itself, so the only things to set in the
// Inspector are the timers (and the meow clip).
public class Cat : MonoBehaviour
{
    [Header("Stare")]
    [Tooltip("Head bone. Empty = the child whose name is 'Cube 002'.")]
    public Transform head;
    [Tooltip("What to stare at. Empty = the main camera.")]
    public Transform target;
    [Tooltip("Degrees per second the head swings.")]
    public float turnSpeed = 90f;
    [Tooltip("How far the head may turn away from the body, in degrees.")]
    public float maxTurnAngle = 100f;
    [Tooltip("Player further away than this is ignored and the head relaxes.")]
    public float stareRange = 20f;

    [Header("Blink")]
    [Tooltip("Material the eyeballs turn into while blinking.")]
    public Material blackEyes;
    [Tooltip("Min / max seconds between blinks.")]
    public Vector2 blinkInterval = new Vector2(2f, 7f);
    [Tooltip("Min / max seconds the eyes stay shut.")]
    public Vector2 blinkDuration = new Vector2(0.09f, 0.16f);

    [Header("Meow")]
    public AudioClip meow;
    [Tooltip("Min / max seconds between meows (while the player is close enough).")]
    public Vector2 meowInterval = new Vector2(10f, 25f);
    [Tooltip("The cat only meows while the player is nearer than this, in metres. The meow is also inaudible past it. Draws as the yellow gizmo sphere.")]
    public float meowRange = 10f;
    [Tooltip("Distance at which the meow is at full volume, in metres. Everything between this and Meow Range fades out.")]
    public float meowFullVolumeRange = 2f;
    [Range(0f, 1f)] public float meowVolume = 0.6f;
    [Tooltip("All cats share one meow queue: a cat stays quiet if ANY cat meowed less than this many seconds ago, so a room full of cats doesn't meow over itself. 0 = every cat may meow freely.")]
    public float meowGapBetweenCats = 4f;

    Renderer headRenderer;
    static Material sharedBlack;
    Mesh mesh;
    int[] eyeSubMeshes;
    Material[] originalMaterials;
    Quaternion baseRotation;
    Vector3 anchor;      // head position at start
    Vector3 pivot;       // point the head swivels around (its own centre)
    Vector3 faceDir;     // where the head looks while at rest
    float yaw;
    float nextBlink, nextMeow, eyesShutUntil = -1f;
    AudioSource source;

    // Shared by every cat: the last moment any cat in the scene meowed. Kept static so a room
    // with four cats behaves like one cat meowing, instead of four overlapping ones.
    static float lastMeowTime = -999f;

    // Domain reload is off in this project, so statics survive between Play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetMeowClock() { lastMeowTime = -999f; }

    void Awake()
    {
        if (!head) head = FindHead();
        if (!head) { enabled = false; return; }
        headRenderer = head.GetComponent<Renderer>();
        mesh = head.GetComponent<MeshFilter>().sharedMesh;
        baseRotation = head.rotation;
        anchor = head.position;
        pivot = headRenderer.bounds.center;
        originalMaterials = headRenderer.sharedMaterials;

        // Eyeballs are the tiny sub-meshes of the head (the big one is the skull).
        float big = mesh.bounds.size.magnitude * 0.5f;
        var eyes = new System.Collections.Generic.List<int>();
        for (int i = 0; i < mesh.subMeshCount; i++)
            if (mesh.GetSubMesh(i).bounds.size.magnitude < big) eyes.Add(i);
        eyeSubMeshes = eyes.ToArray();

        // The model's forward is not the head's pivot axis, so read the face
        // direction off the eyeballs: they sit in front of the skull.
        // Sub-mesh bounds are mesh metadata, not the vertex buffer, so this also works in a
        // player build. Reading mesh.vertices needs Read/Write enabled on the model; without
        // it a build silently hands back an EMPTY array and the fallback landed exactly 180
        // degrees off, so every cat stared away from the player instead of at him.
        faceDir = Flat(transform.forward);
        if (eyeSubMeshes.Length > 0)
        {
            Vector3 sum = Vector3.zero;
            foreach (int i in eyeSubMeshes) sum += mesh.GetSubMesh(i).bounds.center;
            Vector3 d = head.TransformPoint(sum / eyeSubMeshes.Length) - pivot;
            d.y = 0f;
            if (d.sqrMagnitude > 0.000001f) faceDir = d.normalized;
        }

        if (!blackEyes)
        {
            if (!sharedBlack)
            {
                sharedBlack = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                sharedBlack.name = "CatEyesBlack";
                sharedBlack.color = Color.black;
            }
            blackEyes = sharedBlack;
        }

        source = GetComponent<AudioSource>();
        if (!source) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = Mathf.Max(0f, meowFullVolumeRange);
        source.maxDistance = Mathf.Max(source.minDistance + 0.01f, meowRange);
        source.volume = 1f;

        nextBlink = Time.time + Random.Range(blinkInterval.x, blinkInterval.y);
        nextMeow = Time.time + Random.Range(meowInterval.x, meowInterval.y);
    }

    void Update()
    {
        float t = Time.time;
        Look();

        if (eyesShutUntil >= 0f)
        {
            if (t >= eyesShutUntil) { EyesShut(false); eyesShutUntil = -1f; nextBlink = t + Random.Range(blinkInterval.x, blinkInterval.y); }
        }
        else if (t >= nextBlink)
        {
            EyesShut(true);
            eyesShutUntil = t + Random.Range(blinkDuration.x, blinkDuration.y);
        }

        if (t >= nextMeow)
        {
            if (TryMeow(t)) nextMeow = t + Random.Range(meowInterval.x, meowInterval.y);
            // Not this time: retry soon. Either the player is out of earshot (so the cat should
            // greet him as soon as he comes back) or another cat just took the turn.
            else nextMeow = t + 0.5f;
        }
    }

    bool TryMeow(float t)
    {
        if (!meow) return false;
        if (!IsPlayerInEarshot()) return false;
        if (meowGapBetweenCats > 0f && t - lastMeowTime < meowGapBetweenCats) return false;

        source.pitch = Random.Range(0.92f, 1.08f);
        source.PlayOneShot(meow, meowVolume);
        lastMeowTime = t;
        return true;
    }

    // A cat that meows from another room is why the nature room never went quiet: the sound was
    // audible from 25 m away no matter what. Now nothing plays at all outside meowRange.
    bool IsPlayerInEarshot()
    {
        Transform listener = target ? target : (Camera.main ? Camera.main.transform : null);
        if (!listener) return true;
        Vector3 here = pivot.sqrMagnitude > 0f ? pivot : transform.position;
        return Vector3.Distance(listener.position, here) <= meowRange;
    }

    void OnValidate()
    {
        if (!source) return;
        source.minDistance = Mathf.Max(0f, meowFullVolumeRange);
        source.maxDistance = Mathf.Max(source.minDistance + 0.01f, meowRange);
    }

    void Look()
    {
        var lookAt = target ? target : (Camera.main ? Camera.main.transform : null);
        float wanted = 0f;
        if (lookAt)
        {
            Vector3 to = lookAt.position - pivot;
            to.y = 0f;
            if (to.sqrMagnitude < stareRange * stareRange && to.sqrMagnitude > 0.0001f)
                wanted = Mathf.Clamp(Vector3.SignedAngle(faceDir, to, Vector3.up), -maxTurnAngle, maxTurnAngle);
        }

        yaw = Mathf.MoveTowardsAngle(yaw, wanted, turnSpeed * Time.deltaTime);
        Quaternion swing = Quaternion.AngleAxis(yaw, Vector3.up);
        head.rotation = swing * baseRotation;
        // Keep the head's centre in place so it swivels instead of sliding.
        head.position = pivot - swing * (pivot - anchor);
    }

    void EyesShut(bool shut)
    {
        if (eyeSubMeshes == null || eyeSubMeshes.Length == 0) return;
        var mats = (Material[])originalMaterials.Clone();
        foreach (int i in eyeSubMeshes) mats[i] = shut ? blackEyes : originalMaterials[i];
        headRenderer.sharedMaterials = mats;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

    Transform FindHead()
    {
        foreach (var t in GetComponentsInChildren<Transform>())
        {
            string n = new string(System.Array.FindAll(t.name.ToLower().ToCharArray(), char.IsLetterOrDigit));
            if (n == "cube002") return t;
        }
        return null;
    }

    void OnDrawGizmosSelected()
    {
        if (head) Gizmos.DrawWireSphere(headRenderer ? headRenderer.bounds.center : head.position, 0.1f);

        Vector3 here = Application.isPlaying && pivot.sqrMagnitude > 0f ? pivot : transform.position;
        // Black: how far the head will follow you. Yellow: how close you have to be to hear a meow.
        Gizmos.color = Color.black;
        Gizmos.DrawWireSphere(here, stareRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(here, meowRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(here, meowFullVolumeRange);
    }
}
