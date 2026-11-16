using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[System.Serializable]
public class SentenceEntry
{
    [TextArea(2, 10)]
    public string text;
    public Color tint = Color.black;   // THIS sentence's own color
    public float fps = 24f;            // THIS sentence's own animation speed

    [Tooltip("0 = the line stays on screen until the player clicks (original behaviour). Above 0 = it advances by itself after that many seconds. 60 = the long silent beat.")]
    public float autoAdvanceAfter = 0f;
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

    [Header("Priority (who gets the click)")]
    [Tooltip("Bigger number = more important. Only the most important text on screen reacts to clicks, so an event line can appear on top of the story, be clicked away, and the story text is still there afterwards. Leave the story at 0 and give event lines 10.")]
    public int priority = 0;

    [Tooltip("Debug helper: if a click lands while ANOTHER text is in front, log which text took it. Turn this on while wiring a scene together and you will never wonder again why a text refuses to advance.")]
    [SerializeField] private bool debugClicks = false;

    [Header("Timing")]
    [Tooltip("0 = never: this text only appears through a trigger or a Reveal() call. Above 0 = it appears by itself this many seconds after the scene starts. This is the 'wait 60 seconds, then the entity speaks' timer — no extra script needed.")]
    [SerializeField] private float activateAfterSeconds = 0f;

    [Header("Trigger (this text tells the scene it is on screen)")]
    [Tooltip("On = the moment this text's first sentence becomes visible, this object fires 'On Trigger Shown' below (and the C# event Shown). Use it to decorate a room, move objects, play a sound, start another system — anything a script can do. " +
             "Note the difference with 'Activate On Trigger' at the top: that one is a flag something ELSE flips to SHOW this text. This one is this text telling the rest of the scene that it has SHOWED.")]
    public bool isTrigger = false;

    [Tooltip("On = the trigger fires only the first time this text is shown in a Play session. Off = every Reveal() fires it again (useful for event lines like 'not this door, silly!' that repeat).")]
    public bool triggerOnce = true;

    [Tooltip("Wire anything here — no code needed. Drag the GameObject that carries your script and pick a public method, or drop in an Animator / AudioSource / ParticleSystem. Runs the moment this text appears on screen.")]
    public UnityEvent onTriggerShown = new UnityEvent();

    [Header("Branches")]
    [Tooltip("Only used inside a returning branch. On = the moment this text is revealed, the question loop it came from is forgotten (use it on the first text that rejoins the main story). When a branch route simply runs out of text, it returns to the question on its own.")]
    [SerializeField] private bool leaveBranch = false;

    [Header("Letter grid")]
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
    private bool liveStackActive = false; // true only while THIS play session's own stack is on screen
    private static int playSessionId = 0;  // bumped once per Play session by the reset below
    private int builtInSession = -1;       // which session this builder last built its stack in
    private bool waitingForNext = false;
    private bool hasBuiltSinceEnable = false;
    private int triggerFiredSession = -1; // Play session in which this text last fired its trigger (-1 = never)
    private Transform lettersContainer;
    private Coroutine autoAdvanceRoutine;
    private Coroutine timedRevealRoutine;

    // ------------------------------------------------------------------
    //  THE PRIORITY SCREEN
    // ------------------------------------------------------------------
    //  Every text that is actually on screen lives in this one list, sorted by priority
    //  (highest first) and then by "revealed most recently first". onScreen[0] is therefore
    //  always the text the player is looking at, and it is the ONLY one allowed to consume a
    //  click. That single rule is what stops an event line and a story line from both
    //  vanishing on the same click.
    private static readonly List<SentenceBuilder> onScreen = new List<SentenceBuilder>();
    private static int serialCounter = 0;
    private int revealSerial = 0;

    public static SentenceBuilder FrontScreen { get; private set; } // the text on top of the screen right now (null = nothing on screen)
    public bool IsFront => FrontScreen == null || FrontScreen == this;

    // ------------------------------------------------------------------
    //  TRIGGER: "this text is on screen now"
    // ------------------------------------------------------------------
    //  isTrigger turns this FONT into a notifier for the rest of the scene. The moment its first
    //  sentence is actually laid out in play mode, it fires onTriggerShown (wire anything to it in
    //  the Inspector) and raises Shown (for scripts that prefer code). Nothing about the dialogue
    //  changes: the text still waits for its click exactly as before.
    public event System.Action<SentenceBuilder> Shown; // code listeners: font.Shown += YourMethod;

    // Polling version of the same information, for scripts that would rather check a value.
    // Automatically false again in a new Play session, even with domain reload disabled.
    public bool HasTriggered => isTrigger && triggerFiredSession == playSessionId;

    // A persistent, non-sequential text never reacts to a click at all (see the guard in Update),
    // so it must never be allowed to sit in front of a text that does — that is exactly how a
    // permanent easter-egg sign ends up silently eating every click in the room.
    bool EatsClicks => !persistent || sequentialReveal;

    // This project runs with "disable domain reload", so this list survives from one Play session
    // to the next. Wipe it before the scene starts again, or a stale entry from the previous
    // session can keep the click away from everything in the new one.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPriorityScreen()
    {
        onScreen.Clear();
        branchStack.Clear();
        FrontScreen = null;
        CurrentActive = null;
        serialCounter = 0;
        playSessionId++;
    }

    // ------------------------------------------------------------------
    //  BRANCH BOOKMARKS
    // ------------------------------------------------------------------
    //  When the player picks an answer marked "returnToBranch", one of these is pushed.
    //  The route that answer leads to plays normally (nextGroup, nextGroup, nextGroup...)
    //  and when it runs out of text the most recent bookmark is popped and the question
    //  comes back on screen with the used answers greyed out.
    private class BranchFrame
    {
        public SentenceBuilder hub;
        public ResponseButtonGroup group;
    }

    private static readonly List<BranchFrame> branchStack = new List<BranchFrame>();

    // The most recently Revealed FONT object, scene-wide. Updated every time Reveal() runs,
    // and also whenever the priority order changes, so it always points at the text on top.
    // CameraFocusOnCurrentText uses it for the F key, so it keeps working unchanged.
    public static SentenceBuilder CurrentActive { get; private set; }

    // Reset whenever this object transitions to active — including as a cascade side-effect of
    // a parent reactivating. Runs synchronously, before Start() (which Unity defers), so it's
    // always fresh by the time either Start() or Reveal() checks it below.
    void OnEnable()
    {
        hasBuiltSinceEnable = false;
        liveStackActive = false; // never continue a stack that was left behind by the editor preview or a previous Play session
    }

    // Leaving the screen (deactivated by a trigger, or by the scene) must also drop this text
    // out of the click order, otherwise a dead object could keep the click away from a live one.
    void OnDisable()
    {
        liveStackActive = false;
        UnregisterFromScreen();
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

        // The timer-based version of a trigger: "say this 60 seconds from now" without any
        // extra script. It does nothing if something already revealed this text.
        if (Application.isPlaying && activateAfterSeconds > 0f)
        {
            timedRevealRoutine = StartCoroutine(RevealAfterSeconds(activateAfterSeconds));
        }
    }

    IEnumerator RevealAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        timedRevealRoutine = null;
        if (!isRevealed) Reveal();
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
            // -1 means "still waiting", -2 means the question timed out and answered itself.
            if (!waitingForNext && responseGroup.ClickedIndex != -1)
            {
                waitingForNext = true;
                StartCoroutine(AdvanceToResponseChoice(responseGroup.ChosenNextGroup));
            }
            return; // this builder is tied to a response group — left click never advances it
        }

        if (waitingForNext || finishedRendering) return;

        // THE PRIORITY RULE. A lower-priority line just sits there: it keeps its letters, its
        // place in the sequence and its pending click. It becomes clickable again the moment
        // everything above it has finished and removed itself from the screen.
        if (!IsFront)
        {
            if (debugClicks && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Debug.Log($"[{name}] click ignored — '{FrontScreen.name}' is in front of it (priority {FrontScreen.priority}).");
            }
            return;
        }

        // A persistent sentence has to survive every click, so on its own it is completely inert.
        // Paired with sequentialReveal it can still be clicked through — the stack just never
        // clears itself once the last sentence is up.
        if (persistent && !sequentialReveal) return;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            waitingForNext = true;
            StopAutoAdvance();
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
                if (sequentialReveal)
                {
                    // Editor: preview every sentence at once so the stack is readable while
                    // authoring. Play mode: rebuild the stack that is actually on screen, instead
                    // of appending a second copy of the sentence under the old one.
                    RebuildStackUpTo(Application.isPlaying ? currentIndex : sentences.Count - 1);
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
        StopAutoAdvance();
        UnregisterFromScreen();
        ClearAllLetters();
        if (responseGroup != null)
        {
            responseGroup.gameObject.SetActive(false);
        }
    }

    public void Reveal()
    {
        // A text can sit inside a group folder that something switched off. Activating only the
        // text object would leave it "inactive in hierarchy" forever, so wake the whole chain up.
        // Without this, revealing a text inside a deactivated folder looks like nothing happened.
        for (Transform t = transform; t != null; t = t.parent)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        }

        gameObject.SetActive(true);

        // Revealing a chain that already ran to the end replays it from its first sentence.
        // That is what makes a triggered event line ("not this door, silly!") usable more
        // than once: the trigger just calls Reveal() again.
        if (finishedRendering)
        {
            currentIndex = 0;
            hasBuiltSinceEnable = false;
        }
        finishedRendering = false;
        waitingForNext = false;

        // A text marked "leaveBranch" is the point where a route rejoins the normal story,
        // so the question it walked away from is forgotten right here.
        if (leaveBranch && Application.isPlaying)
        {
            branchStack.Clear();
        }

        isRevealed = true;
        CurrentActive = this;
        TryBuildSentenceOnce();

        if (responseGroup != null)
        {
            responseGroup.Owner = this; // lets the buttons know whose turn it is to be clickable
            responseGroup.Activate();
        }
    }

    // Puts a question back on screen after the route it launched has finished. The answers
    // already used stay in the group's clickedResponses, so they come back greyed out (or
    // hidden, if the group has hideUsedResponses ticked).
    public void RestartAsBranchHub(ResponseButtonGroup group)
    {
        responseGroup = group;
        currentIndex = 0;
        finishedRendering = false;
        waitingForNext = false;
        hasBuiltSinceEnable = false;
        StopAutoAdvance();
        Reveal();
    }

    // Called once per showing, from BuildSentence(), only for the FIRST sentence of the text, so a
    // three-sentence FONT fires its trigger once instead of three times.
    void FireShowTrigger()
    {
        if (!Application.isPlaying || !isTrigger) return;

        // triggerOnce = the effect happens once per Play session, even if this text is shown again
        // (a chain that replays itself, a branch that comes back to the same question).
        if (triggerOnce && triggerFiredSession == playSessionId) return;

        triggerFiredSession = playSessionId;

        Debug.Log($"[{name}] trigger fired — this text is now on screen.", this);

        if (onTriggerShown != null) onTriggerShown.Invoke();
        if (Shown != null) Shown(this);
    }

    // ------------------------------------------------------------------
    //  Click order bookkeeping
    // ------------------------------------------------------------------
    void RegisterOnScreen()
    {
        if (!Application.isPlaying) return;

        if (!onScreen.Contains(this)) onScreen.Add(this);
        revealSerial = ++serialCounter;
        RefreshScreenOrder();
    }

    void UnregisterFromScreen()
    {
        onScreen.Remove(this);
        RefreshScreenOrder();
    }

    static void RefreshScreenOrder()
    {
        onScreen.RemoveAll(b => b == null || !b.isRevealed || !b.isActiveAndEnabled);
        onScreen.Sort((a, b) =>
        {
            // Texts that can actually take a click always rank above texts that can't, no matter
            // what priority the inert one was given.
            if (a.EatsClicks != b.EatsClicks) return a.EatsClicks ? -1 : 1;
            if (a.priority != b.priority) return b.priority.CompareTo(a.priority); // higher priority first
            return b.revealSerial.CompareTo(a.revealSerial);                       // same priority: newest first
        });

        FrontScreen = onScreen.Count > 0 ? onScreen[0] : null;
        if (FrontScreen != null) CurrentActive = FrontScreen;
    }

    // Waits for every currently-spawned letter's disappear animation to actually finish,
    // instead of guessing the duration with WaitForSeconds(totalFrames/fps) — that estimate
    // drifts short at low framerates since the per-letter loop pays WaitForSeconds' one-frame
    // rounding cost on every iteration, while a single estimated wait only pays it once.
    IEnumerator WaitForLettersToFinishDisappearing()
    {
        Text_Iteration_And_Animation_Sprites[] letters =
            GetComponentsInChildren<Text_Iteration_And_Animation_Sprites>(true);

        // Safety net. If a letter can never report itself finished (no sprite for its character, a
        // switched-off object, an error inside its own animation), waiting on it alone would freeze
        // the whole dialogue: the next text would never appear and every click would look dead.
        // The limit is deliberately generous — well past the longest possible animation.
        float slowest = 0f;
        foreach (Text_Iteration_And_Animation_Sprites anim in letters)
        {
            float frames = Mathf.Max(anim.totalFrames, 1);
            float speed = Mathf.Max(anim.fps, 0.01f);
            slowest = Mathf.Max(slowest, frames / speed);
        }

        float limit = slowest + 2f;
        float waited = 0f;

        while (true)
        {
            bool allDone = true;
            foreach (Text_Iteration_And_Animation_Sprites anim in letters)
            {
                if (anim == null) continue;
                if (!anim.gameObject.activeInHierarchy) continue; // switched off: already gone
                if (!anim.disappearFinished) { allDone = false; break; }
            }

            if (allDone) yield break;

            if (waited >= limit)
            {
                Debug.LogWarning($"[{name}] A letter never reported a finished disappear animation " +
                                 $"(waited {limit:0.0}s). Continuing anyway so the dialogue cannot get stuck. " +
                                 "Look for 'No sprite named ...' errors above — that is the usual reason.", this);
                yield break;
            }

            waited += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator AdvanceAfterDisappear()
    {
        // The letters are spawned with clickToDisappear = false (the builder owns the click), so
        // nobody starts their vanish animation here unless we do it. The sequential and question
        // paths already did; this one was missed, which meant WaitForLettersToFinishDisappearing()
        // had nothing to wait for and sat out its whole safety timeout on every single click —
        // a ~3 s freeze plus the "never reported a finished disappear animation" warning.
        foreach (Text_Iteration_And_Animation_Sprites anim in GetComponentsInChildren<Text_Iteration_And_Animation_Sprites>())
        {
            anim.TriggerDisappear();
        }

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
            UnregisterFromScreen();

            if (nextGroup != null)
            {
                RevealNextGroup(nextGroup);
            }
            else
            {
                ResolveChainEnd(); // nothing follows — maybe we owe a question an answer
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
        // Snapshot the answer BEFORE the group is released: the group fades itself out and
        // deactivates, and we still need to know whether this answer comes back here.
        ResponseButtonGroup answered = responseGroup;
        bool returnsHere = answered != null && answered.ChosenReturnToBranch;

        if (returnsHere)
        {
            branchStack.Add(new BranchFrame { hub = this, group = answered });
        }

        foreach (Text_Iteration_And_Animation_Sprites anim in GetComponentsInChildren<Text_Iteration_And_Animation_Sprites>())
        {
            anim.TriggerDisappear();
        }

        yield return WaitForLettersToFinishDisappearing();

        ClearAllLetters();
        finishedRendering = true;
        UnregisterFromScreen();

        // responseGroup handles playing its own button-letter disappear animation and
        // deactivating itself on its own timer — don't touch its active state here,
        // or we risk cutting that fade off mid-animation.
        responseGroup = null;   // stop polling this group; RestartAsBranchHub puts it back
        waitingForNext = false;

        if (chosenGroup != null)
        {
            RevealNextGroup(chosenGroup);
        }
        else
        {
            // chosenGroup being null just means this was a terminal response — the question
            // above still correctly played its disappear animation, so only the branch
            // bookkeeping is left to settle.
            ResolveChainEnd();
        }
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
        UnregisterFromScreen();

        if (nextGroup != null)
        {
            RevealNextGroup(nextGroup);
        }
        else
        {
            ResolveChainEnd(); // nothing follows — maybe we owe a question an answer
        }

        waitingForNext = false;
    }

    // One place that turns "the next GameObject" into "the next text". Pointing a Next Group at a
    // folder instead of at the text object is the easiest mistake to make in this whole system,
    // so it is handled and logged instead of silently doing nothing.
    void RevealNextGroup(GameObject target)
    {
        if (target == null) return;

        SentenceBuilder next = target.GetComponent<SentenceBuilder>();
        if (next != null)
        {
            next.Reveal();
            return;
        }

        SentenceBuilder[] inside = target.GetComponentsInChildren<SentenceBuilder>(true);
        if (inside.Length == 1)
        {
            Debug.LogWarning($"[{name}] Next Group '{target.name}' is a folder, not a text object. Using the only SentenceBuilder inside it ('{inside[0].name}'). Point the field at that object directly to be safe.", target);
            inside[0].Reveal();
            return;
        }

        Debug.LogWarning($"[{name}] Next Group '{target.name}' has no SentenceBuilder on it (found {inside.Length} inside), so nothing was revealed. Point the field at the text object itself, not at the group folder.", target);
    }

    // The route that a "returnToBranch" answer started has just run out of text. Pop the most
    // recent bookmark and put its question back on screen. If there is no bookmark, the chain
    // simply ends, which is exactly what happened before branches existed.
    void ResolveChainEnd()
    {
        if (leaveBranch)
        {
            branchStack.Clear(); // this route deliberately leaves the question loop
            return;
        }

        if (branchStack.Count == 0) return;

        BranchFrame frame = branchStack[branchStack.Count - 1];
        branchStack.RemoveAt(branchStack.Count - 1);

        if (frame.hub != null)
        {
            frame.hub.RestartAsBranchHub(frame.group);
        }
    }

    void BuildSentence()
    {
        transform.localScale = Vector3.one * gridScale;

        // Sequential mode stacks onto whatever THIS session has already put on screen. The editor
        // preview (and any letters left over from a previous play session) must never count as
        // "already on screen": that is what used to make the text appear twice in play mode —
        // the preview stayed put and the real sentence was laid out after it.
        if (Application.isPlaying && builtInSession != playSessionId)
        {
            builtInSession = playSessionId; // first build of this Play session: never append
            liveStackActive = false;
        }

        bool append = Application.isPlaying && sequentialReveal && liveStackActive;

        if (!append)
        {
            ClearAllLetters(); // also resets stackedRowOffset
        }

        int startRow = append ? stackedRowOffset : 0;
        stackedRowOffset = startRow + BuildSentenceAt(currentIndex, startRow);
        liveStackActive = Application.isPlaying && sequentialReveal;
        builtInSession = Application.isPlaying ? playSessionId : -1;

        RegisterOnScreen();
        StartAutoAdvanceForCurrentSentence();

        // This text is now visible on screen — the moment another script may want to decorate a
        // room, move objects, play a sound... Only the first sentence fires it, so a multi-sentence
        // FONT notifies once, not once per sentence.
        if (currentIndex == 0) FireShowTrigger();
    }

    // ------------------------------------------------------------------
    //  Timing: the optional per-sentence auto-advance
    // ------------------------------------------------------------------
    void StopAutoAdvance()
    {
        if (autoAdvanceRoutine != null)
        {
            StopCoroutine(autoAdvanceRoutine);
            autoAdvanceRoutine = null;
        }
    }

    void StartAutoAdvanceForCurrentSentence()
    {
        StopAutoAdvance();

        if (!Application.isPlaying || waitingForNext || finishedRendering) return;
        if (responseGroup != null) return; // a question is answered by buttons, never by a timer

        SentenceEntry entry = (currentIndex >= 0 && currentIndex < sentences.Count) ? sentences[currentIndex] : null;
        if (entry == null || entry.autoAdvanceAfter <= 0f) return;

        autoAdvanceRoutine = StartCoroutine(AutoAdvance(entry.autoAdvanceAfter));
    }

    IEnumerator AutoAdvance(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        // A timer never fires on a line that is sitting underneath a more important one:
        // the 60-second beat waits until the event line above it has been read.
        yield return new WaitUntil(() => IsFront || !isRevealed || finishedRendering);

        autoAdvanceRoutine = null;
        if (!isRevealed || finishedRendering || waitingForNext) yield break;

        waitingForNext = true;
        StartCoroutine(sequentialReveal ? AdvanceStacked() : AdvanceAfterDisappear());
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

            // Every whitespace that is not a plain space (a tab pasted from another editor, a
            // non-breaking space, a stray carriage return) has no sprite in the sheet. Left alone it
            // becomes a letter with no picture, which used to break the disappear animation. Treat
            // all of them as one empty column.
            if (char.IsWhiteSpace(c)) { column++; continue; }

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

    // Lays out sentences 0..lastIndex from scratch, then leaves the stack ready for the NEXT
    // sentence to append underneath. Used by the Inspector preview and by live edits in play mode.
    void RebuildStackUpTo(int lastIndex)
    {
        transform.localScale = Vector3.one * gridScale;
        ClearAllLetters();

        int row = 0;
        for (int i = 0; i <= lastIndex && i < sentences.Count; i++)
        {
            row += BuildSentenceAt(i, row);
        }

        stackedRowOffset = row;
        liveStackActive = Application.isPlaying && sequentialReveal;
    }

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
        // Letters no longer disappear on their own: the builder above decides whether this text
        // is the one the click belongs to. Without this, one click wiped every line on screen.
        anim.clickToDisappear = false;

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
        anim.clickToDisappear = false;

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
        liveStackActive = false; // the next build starts a brand new stack
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
