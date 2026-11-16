using UnityEngine;

public class GlitchFlipbook : MonoBehaviour
{
    [Tooltip("Path relative to a Resources folder, e.g. 'Glitch' for Assets/Resources/Glitch/")]
    public string resourceFolder = "Glitches/Glitch_purple";
    public float fps = 12f;
    public bool randomOrder = false;

    private Texture2D[] frames;
    private Renderer rend;
    private int currentFrame;
    private float timer;

    void Start()
    {
        rend = GetComponent<Renderer>();
        frames = Resources.LoadAll<Texture2D>(resourceFolder);

        if (frames == null || frames.Length == 0)
        {
            Debug.LogError($"No textures found in Resources/{resourceFolder}. " +
                            "Make sure your PNGs are inside a folder literally named 'Resources'.");
            return;
        }

        // Resources.LoadAll doesn't guarantee order, so sort by name
        // (works well if your files are named frame_00, frame_01, etc.)
        System.Array.Sort(frames, (a, b) => string.Compare(a.name, b.name));

        rend.material.mainTexture = frames[0];
    }

    void Update()
    {
        if (frames == null || frames.Length == 0) return;

        timer += Time.deltaTime;
        if (timer >= 1f / fps)
        {
            timer = 0f;
            currentFrame = randomOrder
                ? Random.Range(0, frames.Length)
                : (currentFrame + 1) % frames.Length;

            rend.material.mainTexture = frames[currentFrame];
            // if using an emission map instead:
            // rend.material.SetTexture("_EmissionMap", frames[currentFrame]);
        }
    }
}