using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[ExecuteAlways]
public class Text_Iteration_And_Animation_Sprites : MonoBehaviour
{
    public string spriteSheetPath_CAPS = "Letters/fontcaps";
    public int frameNumber = 24; // used for the static editor preview only
    public string spriteName = "A";
    public Color letterTint = Color.white;
    public int totalFrames = 24;
    public float fps = 10f;
    public bool loop = false;
    public bool clickToDisappear = true; // false when used as a button letter, or a question's letters — something else controls disappearance instead

    private SpriteRenderer sr;
    private Sprite[] frames;
    private Coroutine currentAnimation;
    private bool hasDisappeared = false;
    public bool disappearFinished = false; // true only once PlayDisappearAnimation() actually finishes — for callers that need to wait on the real animation, not an estimate

    // Every letter used to call Resources.LoadAll once per frame (24 sheet loads per letter, and
    // there are hundreds of letters in the scene). The sheet only has to be read once per path.
    private static readonly Dictionary<string, Sprite[]> sheetCache = new Dictionary<string, Sprite[]>();
    private static readonly HashSet<string> reportedMissing = new HashSet<string>();

    // Domain reload is disabled in this project, so statics survive between Play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCaches()
    {
        sheetCache.Clear();
        reportedMissing.Clear();
    }

    void Start()
    {
        EnsureSpawned();

        if (Application.isPlaying)
        {
            LoadAllFrames();
            currentAnimation = StartCoroutine(PlayAnimation());
        }
    }

    void Update()
    {
        if (Application.isPlaying && clickToDisappear && !hasDisappeared && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            TriggerDisappear();
        }
    }

    void OnValidate()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            if (!gameObject.activeInHierarchy) return;
            SpawnSprite(frameNumber, spriteName);
        };
        #endif
    }

    // Public wrapper so callers (like ResponseButtonGroup) can force the sprite to spawn
    // immediately and get the real SpriteRenderer back, instead of waiting on Start() timing.
    public SpriteRenderer EnsureSpawned()
    {
        SpawnSprite(frameNumber, spriteName);
        return sr;
    }

    // Starts the disappear animation from an external trigger (e.g. SentenceBuilder reacting
    // to an acknowledged click) rather than only from this object's own click detection.
    public void TriggerDisappear()
    {
        if (hasDisappeared) return;
        hasDisappeared = true;
        disappearFinished = false;
        if (currentAnimation != null) StopCoroutine(currentAnimation);

        // A letter that is switched off cannot run a coroutine. Reporting it as finished keeps
        // whoever is waiting on it from waiting forever.
        if (!isActiveAndEnabled)
        {
            disappearFinished = true;
            return;
        }

        currentAnimation = StartCoroutine(PlayDisappearAnimation());
    }

    void SpawnSprite(int frame, string letter)
    {
        if (string.IsNullOrEmpty(letter)) return;

        ClearOldLetters(letter);

        Transform existing = transform.Find("Letter_" + letter);
        GameObject child;
        SpriteRenderer renderer;

        if (existing == null)
        {
            child = new GameObject("Letter_" + letter);
            child.transform.SetParent(transform, false);
            renderer = child.AddComponent<SpriteRenderer>();
        }
        else
        {
            child = existing.gameObject;
            renderer = child.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = child.AddComponent<SpriteRenderer>();
        }

        // The SpriteRenderer is created even when the sheet has no sprite for this character.
        // Returning early here used to leave 'sr' null, and the disappear animation then threw a
        // NullReferenceException — which meant disappearFinished was never set, so the dialogue
        // waited on it forever and the next text never appeared.
        renderer.sprite = LoadFrameSprite(frame, letter);
        renderer.color = letterTint;

        sr = renderer;
    }

    Sprite LoadFrameSprite(int frame, string letter)
    {
        string frameStr = frame.ToString("00");
        string path = spriteSheetPath_CAPS + "/" + frameStr;

        foreach (Sprite s in LoadSheet(path))
        {
            if (s != null && s.name == letter) return s;
        }

        // One message per missing character, not one per frame per letter: a single tab in the
        // text used to fill the Console with dozens of identical errors.
        string key = spriteSheetPath_CAPS + "|" + letter;
        if (reportedMissing.Add(key))
        {
            Debug.LogError($"No sprite named {Describe(letter)} found in {spriteSheetPath_CAPS}. " +
                           "Check that character in the text — a tab or another invisible whitespace is the usual cause. " +
                           "This letter will be invisible, but the dialogue will keep running.", this);
        }

        return null;
    }

    static string Describe(string letter)
    {
        if (letter == null) return "(null)";
        if (letter.Length == 1 && (letter[0] < ' ' || letter[0] == '\u00A0'))
            return $"'\\u{(int)letter[0]:X4}'";
        return $"'{letter}'";
    }

    static Sprite[] LoadSheet(string path)
    {
        Sprite[] cached;
        if (Application.isPlaying && sheetCache.TryGetValue(path, out cached)) return cached;

        Sprite[] all = Resources.LoadAll<Sprite>(path);

        // Only cache while playing: in the editor the sheet can be reimported at any moment and a
        // stale cache would show the old artwork.
        if (Application.isPlaying) sheetCache[path] = all;
        return all;
    }

    void LoadAllFrames()
    {
        int count = Mathf.Max(totalFrames, 1);
        frames = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = LoadFrameSprite(i + 1, spriteName);
        }
    }

    IEnumerator PlayAnimation()
    {
        float delay = 1f / Mathf.Max(fps, 0.01f);

        if (frames == null) LoadAllFrames();

        do
        {
            for (int i = 0; i < frames.Length; i++)
            {
                if (sr != null && frames[i] != null) sr.sprite = frames[i];
                yield return new WaitForSeconds(delay);
            }
        } while (loop && !hasDisappeared);
    }

    IEnumerator PlayDisappearAnimation()
    {
        float delay = 1f / Mathf.Max(fps, 0.01f);

        // Whatever happens in here, disappearFinished has to end up true: the SentenceBuilder
        // waits on this flag before it shows the next text, so a throw would freeze the dialogue.
        try
        {
            if (frames == null) LoadAllFrames();
            if (sr == null) EnsureSpawned();

            // Walk the SAME frame array backwards: index (totalFrames-1) is frame 24, index 0 is frame 1
            for (int i = frames.Length - 1; i >= 0; i--)
            {
                if (sr != null && frames[i] != null) sr.sprite = frames[i];
                yield return new WaitForSeconds(delay);
            }

            // One step "past" frame 1 — nothing left to draw
            if (sr != null) sr.sprite = null;
        }
        finally
        {
            disappearFinished = true;
        }
    }

    void ClearOldLetters(string keep)
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform childT = transform.GetChild(i);
            if (childT.name.StartsWith("Letter_") && childT.name != "Letter_" + keep)
            {
                if (Application.isPlaying)
                    Destroy(childT.gameObject);
                else
                    DestroyImmediate(childT.gameObject);
            }
        }
    }
}
