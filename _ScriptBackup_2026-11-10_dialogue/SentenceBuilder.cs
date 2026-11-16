using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[System.Serializable]
public class SentenceEntry
{
    [TextArea(2, 10)]
    public string text;
    public Color tint = Color.black;   // THIS sentence's own color
    public float fps = 24f;            // THIS sentence's own animation speed
}
    
    
[ExecuteAlways]
public class SentenceBuilder : MonoBehaviour
{   
    [SerializeField] private ResponseButtonGroup responseGroup; // assign only on the sentence group that ends in a question
    public bool isRevealed = true; // set to FALSE in the Inspector for groups that should stay dormant until activated
    public bool activateOnTrigger = false; // flip this to true from any other script (e.g. a trigger volume) to reveal this FONT chain — requires the GameObject to stay active with isRevealed = false
    public bool finishedRendering = false;
    [SerializeField] private GameObject nextGroup; // the next (initially hidden) font-group GameObject to reveal once this one is done
    [SerializeField] private int gapFrames = 6; // extra pause between one sentence fully vanishing and the next appearing
    [SerializeField] private List<SentenceEntry> sentences = new List<SentenceEntry> { new SentenceEntry { text = "HI" } };
    [SerializeField] private bool loop = false;

    [Header("Persistence")]
    [Tooltip("Off = a click dismisses the text (original behaviour). On = nothing can dismiss it: no click ever clears it. Use this for easter eggs and signage that must stay readable forever.")]
    [SerializeField] private bool persistent = false;

    [Header("Sequential reveal")]
    [Tooltip("Off = each sentence replaces the previous one (original behaviour). On = each new sentence is added BELOW the ones already on screen and they all stay visible; the whole stack clears only after the last sentence has been clicked.")]
    [SerializeField] private bool sequentialReveal = false;

    [SerializeField] private string descenderChars = "gjpqy";
    [SerializeField] private string spriteSheetPath_CAPS = "Letters/fontcaps";
    [SerializeField] private string spriteSheetPath_lower = "Letters/fontLower";
    [SerializeField] private int maxColumns = 10;
    [SerializeField] private int maxRows = 3;
    [SerializeField] private float cellSizeX = 1f;
    [SerializeField] private float cellSizeY = 1f;
    [SerializeField] private float gridScale = 1f;

    [Header("Surface sticking")]
    [Tooltip("Off = the original flat grid, untouched. On = every letter is raycast onto the geometry in front of it and turns corners with the surface.")]
    [SerializeField] private SurfaceSnapSettings surface = new SurfaceSnapSettings();

    [SerializeField] private int frameNumber = 24;
    [SerializeField] private int totalFrames = 24;
    [SerializeField] private float descenderOffset = 0.3f;

    private int currentIndex = 0;
    private int stackedRowOffset = 0; // sequential mode: the first free grid row below everything already on screen
    private TextLayoutEngine stackedEngine; // sequential mode: keeps the surface walk going from one sentence to the next
    private bool waitingForNext = false;
    private bool hasBuiltSinceEnable = false;
    private Transform lettersContainer;

    // Reset whenever this object transitions to active — including as a cascade side-effect of
    // a parent reactivating. Runs synchronously, before Start() (which Unity defers), so it's
    // always fresh by the time either Start() or Reveal() checks it below.
    void OnEnable()
    {
        hasBuiltSinceEnable = false;
    }

    // All Slot_ letters live under this child object instead of directly under this transform,
    // purely to keep the Hierarchy panel readable when you're hunting through many word groups.
    Transform GetLettersContainer()
    {
        if (lettersContainer == null)
        {
            Transform existing = transform.Find("Letter Pngs");
            if (existing != null)
            {
                lettersContainer = existing;
            }
            else
            {
                GameObject go = new GameObject("Letter Pngs");
                go.transform.SetParent(transform, false);
                lettersContainer = go.transform;
            }
        }
        return lettersContainer;
    }

    void Awake()
    {
        if (surface == null) surface = new SurfaceSnapSettings();

        // Force our OWN nextGroup target to start fully inactive — mirrors how ResponseButtonGroup
        // already hides its reply targets. Awake() always runs before ANY object's Start() in the
        // scene, so this guarantees nextGroup can't sneak in an early BuildSentence() before it's
        // properly Revealed, regardless of what isRevealed happened to be left at in the Inspector.
        if (nextGroup != null)
        {
            nextGroup.SetActive(false);
        }
    }

    void Start()
    {
        if (isRevealed)
        {
            TryBuildSentenceOnce();
        }
        else
        {
            ClearAllLetters(); // wipes any letters left over from a time isRevealed was true (e.g. editor setup) so nothing lingers visible before the trigger fires
            if (responseGroup != null)
            {
                responseGroup.gameObject.SetActive(false);
            }
        }
    }

    // Whichever of Start() or Reveal() actually runs first wins and builds — the other sees
    // hasBuiltSinceEnable already true and safely skips. Immune to the ordering race that
    // activeSelf-checking couldn't reliably catch when this object is a child of something
    // else that reactivates it (parent SetActive(true) cascades activation before Start() fires).
    void TryBuildSentenceOnce()
    {
        if (hasBuiltSinceEnable) return;
        hasBuiltSinceEnable = true;
        BuildSentence();
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        if (!isRevealed)
        {
            if (activateOnTrigger)
            {
                activateOnTrigger = false; // consume the flag so it only fires once per trigger
                Reveal();
            }
            return;
        }

        if (responseGroup != null)
        {
            if (!waitingForNext && responseGroup.ClickedIndex >= 0)
            {
                waitingForNext = true;
                StartCoroutine(AdvanceToResponseChoice(responseGroup.ChosenNextGroup));
            }
            return; // this builder is tied to a response group — left click never advances it
        }

        if (waitingForNext || finishedRendering) return;

        // A persistent sentence has to survive every click, so on its own it is completely inert.
        // Paired with sequentialReveal it can still be clicked through — the stack just never
        // clears itself once the last sentence is up.
        if (persistent && !sequentialReveal) return;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            waitingForNext = true;
            StartCoroutine(sequentialReveal ? AdvanceStacked() : AdvanceAfterDisappear());
        }
    }

    void OnValidate()
    {
        if (surface == null) surface = new SurfaceSnapSettings();

        #if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            if (isRevealed)
            {
                if (sequentialReveal && !Application.isPlaying)
                {
                    BuildFullPreview(); // editor preview: show the whole stack at once
                }
                else
                {
                    BuildSentence();
                }
            }
            else
            {
                ClearAllLetters();
                if (responseGroup != null)
                {
                    responseGroup.gameObject.SetActive(false);
                }
            }
        };
        #endif
    }

    // Forces this FONT into its dormant state — call this from an external script (like a
    // trigger) at Start() if isRevealed isn't reliably holding its Inspector value on Play.
    public void Hide()
    {
        isRevealed = false;
        hasBuiltSinceEnable = false; // allow the next Reveal() to build again, since we're not deactivating the GameObject here
        ClearAllLetters();
        if (responseGroup != null)
        {
            responseGroup.gameObject.SetActive(false);
        }
    }

    // The most recently Revealed FONT object, scene-wide. Updated every time Reveal() runs,
    // so it always unambiguously points at "the current one" — no Hierarchy scanning needed,
    // and it correctly keeps pointing at the last text even briefly after it finishes/disappears.
    public static SentenceBuilder CurrentActive { get; private set; }

    public void Reveal()
    {
        gameObject.SetActive(true);
        isRevealed = true;
        CurrentActive = this;
        TryBuildSentenceOnce();

        if (responseGroup != null)
        {
            responseGroup.Activate();
        }
    }

    // Waits for every currently-spawned letter's disappear animation to actually finish,
    // instead of guessing the duration with WaitForSeconds(totalFrames/fps) — that estimate
    // drifts short at low framerates since the per-letter loop pays WaitForSeconds' one-frame
    // rounding cost on every iteration, while a single estimated wait only pays it once.
    IEnumerator WaitForLettersToFinishDisappearing()
    {
        yield return new WaitUntil(() =>
        {
            foreach (Text_Iteration_And_Animation_Sprites anim in GetComponentsInChildren<Text_Iteration_And_Animation_Sprites>())
            {
                if (!anim.disappearFinished) return false;
            }
            return true;
        });
    }

    IEnumerator AdvanceAfterDisappear()
    {
        yield return WaitForLettersToFinishDisappearing();

        float currentFps = sentences[currentIndex].fps;
        float gapDuration = gapFrames / currentFps;
        yield return new WaitForSeconds(gapDuration);

        currentIndex++;
        if (currentIndex >= sentences.Count)
        {
            currentIndex = sentences.Count - 1;
            ClearAllLetters();
            finishedRendering = true;

            if (nextGroup != null)
            {
                SentenceBuilder nextBuilder = nextGroup.GetComponent<SentenceBuilder>();
                if (nextBuilder != null) nextBuilder.Reveal();
            }
        }
        else
        {
            BuildSentence();
        }

        waitingForNext = false;
    }

    // Runs when this builder is tied to a responseGroup and the player has picked an answer.
    // Since the letters' own clickToDisappear is switched off for question builders, we have to
    // trigger their disappear animation manually here instead of relying on a click.
    IEnumerator AdvanceToResponseChoice(GameObject chosenGroup)
    {
        foreach (Text_Iteration_And_Animation_Sprites anim in GetComponentsInChildren<Text_Iteration_And_Animation_Sprites>())
        {
            anim.TriggerDisappear();
        }

        yield return WaitForLettersToFinishDisappearing();

        ClearAllLetters();
        finishedRendering = true;

        // responseGroup handles playing its own button-letter disappear animation and
        // deactivating itself on its own timer — don't touch its active state here,
        // or we risk cutting that fade off mid-animation.
        if (chosenGroup != null)
        {
            SentenceBuilder nextBuilder = chosenGroup.GetComponent<SentenceBuilder>();
            if (nextBuilder != null) nextBuilder.Reveal();
        }
        // chosenGroup being null just means this was a terminal response — nothing further
        // to reveal, but the question text above still correctly played its disappear animation.
        responseGroup = null;

        waitingForNext = false;
    }

    // Sequential mode: the sentences already on screen stay put, the next one is added underneath.
    // Only the click that lands after the LAST sentence clears the whole stack.
    IEnumerator AdvanceStacked()
    {
        if (sentences.Count == 0)
        {
            waitingForNext = false;
            yield break;
        }

        if (currentIndex + 1 < sentences.Count)
        {
            currentIndex++;
            BuildSentence(); // appends below, keeps everything already visible
            waitingForNext = false;
            yield break;
        }

        currentIndex = sentences.Count - 1;

        // Easter-egg mode: the finished text is allowed to stay forever.
        if (persistent)
        {
            waitingForNext = false;
            yield break;
        }

        // End of the sequence — NOW everything goes away at once. The letters don't fade on their
        // own in this mode, so the builder has to trigger every one of them itself.
        foreach (Text_Iteration_And_Animation_Sprites anim in GetComponentsInChildren<Text_Iteration_And_Animation_Sprites>())
        {
            anim.TriggerDisappear();
        }

        yield return WaitForLettersToFinishDisappearing();

        SentenceEntry last = sentences[currentIndex];
        float lastFps = last != null ? last.fps : 24f;
        yield return new WaitForSeconds(gapFrames / Mathf.Max(lastFps, 0.01f));

        ClearAllLetters();
        finishedRendering = true;

        if (nextGroup != null)
        {
            SentenceBuilder nextBuilder = nextGroup.GetComponent<SentenceBuilder>();
            if (nextBuilder != null) nextBuilder.Reveal();
        }

        waitingForNext = false;
    }

    void BuildSentence()
    {
        transform.localScale = Vector3.one * gridScale;

        // Sequential mode stacks onto whatever is already on screen. In the editor a rebuild is
        // always a fresh preview (BuildFullPreview), so an Inspector tweak can never append a
        // second copy of the same sentence.
        bool append = Application.isPlaying && sequentialReveal && stackedRowOffset > 0;

        if (!append)
        {
            ClearAllLetters(); // also resets stackedRowOffset
        }

        int startRow = append ? stackedRowOffset : 0;
        stackedRowOffset = startRow + BuildSentenceAt(currentIndex, startRow);
    }

    // Lays out ONE sentence starting at startRow of the grid and returns how many rows it used,
    // so the caller can stack the next sentence directly underneath it.
    int BuildSentenceAt(int index, int startRow)
    {
        if (index < 0 || index >= sentences.Count) return 0;

        SentenceEntry entry = sentences[index];
        string currentSentence = entry?.text ?? "";
        Color tint = entry != null ? entry.tint : Color.white;
        float fps = entry != null ? entry.fps : 10f;

        bool stick = surface != null && surface.stickToSurfaces;
        TextLayoutEngine engine = null;

        if (stick)
        {
            if (sequentialReveal)
            {
                // One engine for the whole stack: the walk carries on from the last letter already on
                // screen, so a sentence that wrapped around a corner doesn't drag the next one back
                // to this object's original facing plane.
                if (stackedEngine == null) stackedEngine = new TextLayoutEngine(transform, surface, transform);
                engine = stackedEngine;
            }
            else
            {
                engine = new TextLayoutEngine(transform, surface, transform);
            }
        }

        int column = 0;
        int row = startRow;

        for (int i = 0; i < currentSentence.Length; i++)
        {
            char c = currentSentence[i];

            if (c == '\n') { column = 0; row++; continue; }

            if (column >= maxColumns) { column = 0; row++; }

            // maxRows is how many rows ONE sentence may occupy, so it is measured from this
            // sentence's own first row — not from the top of the stacked text.
            if (row - startRow >= maxRows)
            {
                Debug.LogWarning("Sentence is longer than maxRows can hold — remaining letters were not spawned.");
                break;
            }

            if (c == ' ') { column++; continue; }

            string sheetPath = char.IsUpper(c) ? spriteSheetPath_CAPS : spriteSheetPath_lower;
            bool isDescender = descenderChars.IndexOf(c) >= 0;

            if (stick)
            {
                // Same grid position the flat layout would have used, but handed to the engine so
                // it can drop the letter onto whatever surface is in front of that point.
                float dropAmount = isDescender ? descenderOffset : 0f;
                Vector3 flatLocal = new Vector3(column * cellSizeX, -row * cellSizeY - dropAmount, 0f);

                SurfacePose pose = engine.PlaceNext(flatLocal);
                SpawnLetterAtPose(c.ToString(), pose, sheetPath, tint, fps);
            }
            else
            {
                SpawnLetterAt(c.ToString(), column, row, sheetPath, isDescender, tint, fps);
            }

            column++;
        }

        return row - startRow + 1;
    }

#if UNITY_EDITOR
    // Editor-only preview for sequential builders: lays out every sentence at once so you can see
    // the finished stack while authoring it. Play mode still reveals them one click at a time.
    void BuildFullPreview()
    {
        transform.localScale = Vector3.one * gridScale;
        ClearAllLetters();

        int row = 0;
        for (int i = 0; i < sentences.Count; i++)
        {
            row += BuildSentenceAt(i, row);
        }

        stackedRowOffset = row;
    }
#endif

    void SpawnLetterAt(string letter, int column, int row, string sheetPath, bool isDescender, Color tint, float fps)
    {
        GameObject letterObj = new GameObject("Slot_" + row + "_" + column + "_" + letter);
        letterObj.SetActive(false);

        letterObj.transform.SetParent(GetLettersContainer(), false);

        float dropAmount = isDescender ? descenderOffset : 0f;
        letterObj.transform.localPosition = new Vector3(column * cellSizeX, -row * cellSizeY - dropAmount, 0f);

        Text_Iteration_And_Animation_Sprites anim = letterObj.AddComponent<Text_Iteration_And_Animation_Sprites>();
        anim.spriteSheetPath_CAPS = sheetPath;
        anim.spriteName = letter;
        anim.frameNumber = frameNumber;
        anim.totalFrames = totalFrames;
        anim.fps = fps;
        anim.letterTint = tint;
        anim.loop = loop;
        anim.clickToDisappear = responseGroup == null && !persistent && !sequentialReveal; // question-linked text ignores clicks entirely, and so does a persistent easter egg or a sequential stack (the builder clears those itself)

        letterObj.SetActive(true);
    }

    // Surface variant: the pose already carries a world position and rotation, so we ignore the
    // local grid entirely and let the parent transform supply only the uniform gridScale.
    void SpawnLetterAtPose(string letter, SurfacePose pose, string sheetPath, Color tint, float fps)
    {
        GameObject letterObj = new GameObject("Slot_" + letter);
        letterObj.SetActive(false);

        letterObj.transform.SetParent(GetLettersContainer(), false);
        letterObj.transform.SetPositionAndRotation(pose.position, pose.rotation);

        Text_Iteration_And_Animation_Sprites anim = letterObj.AddComponent<Text_Iteration_And_Animation_Sprites>();
        anim.spriteSheetPath_CAPS = sheetPath;
        anim.spriteName = letter;
        anim.frameNumber = frameNumber;
        anim.totalFrames = totalFrames;
        anim.fps = fps;
        anim.letterTint = tint;
        anim.loop = loop;
        anim.clickToDisappear = responseGroup == null && !persistent && !sequentialReveal;

        letterObj.SetActive(true);
    }

    void OnDrawGizmosSelected()
    {
        if (surface == null || !surface.stickToSurfaces || !surface.drawGizmos) return;

        Transform container = transform.Find("Letter Pngs");
        if (container == null) return;

        Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.8f);
        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);
            if (!child.name.StartsWith("Slot_")) continue;

            Vector3 p = child.position;
            Gizmos.DrawWireSphere(p, 0.03f);
            Gizmos.DrawLine(p, p + transform.forward * surface.probeDepth);
        }
    }

    void ClearAllLetters()
    {
        stackedRowOffset = 0; // a cleared builder always starts a fresh stack
        stackedEngine = null;

        Transform container = GetLettersContainer();
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Transform childT = container.GetChild(i);
            if (childT.name.StartsWith("Slot_"))
            {
                if (Application.isPlaying)
                    Destroy(childT.gameObject);
                else
                    DestroyImmediate(childT.gameObject);
            }
        }
    }
}   