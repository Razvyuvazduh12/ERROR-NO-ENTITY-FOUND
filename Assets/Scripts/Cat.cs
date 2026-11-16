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
    [Tooltip("Min / max seconds between meows.")]
    public Vector2 meowInterval = new Vector2(10f, 25f);
    [Range(0f, 1f)] public float meowVolume = 0.6f;

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
        faceDir = Flat(transform.forward);
        if (eyeSubMeshes.Length > 0)
        {
            var verts = mesh.vertices;
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (int i in eyeSubMeshes)
                foreach (int v in mesh.GetTriangles(i)) { sum += verts[v]; n++; }
            Vector3 d = head.TransformPoint(sum / Mathf.Max(n, 1)) - pivot;
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
        source.maxDistance = 25f;

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
            if (meow) { source.pitch = Random.Range(0.92f, 1.08f); source.PlayOneShot(meow, meowVolume); }
            nextMeow = t + Random.Range(meowInterval.x, meowInterval.y);
        }
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
    }
}
