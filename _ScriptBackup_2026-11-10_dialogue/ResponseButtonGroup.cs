using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[System.Serializable]
public class ResponseOption
{
    [TextArea(1, 4)]
    public string text;
    public Color tint = Color.white;
    public Color hoverTint = Color.yellow;
    public GameObject nextGroup; // the FONT-style object to reveal if THIS response is picked
}
[ExecuteAlways]
public class ResponseButtonGroup : MonoBehaviour
{   
    public GameObject ChosenNextGroup = null;
    [SerializeField] private List<ResponseOption> responses =
    new List<ResponseOption> { new ResponseOption { text = "Yes" }, new ResponseOption { text = "No" } };
    public int ClickedIndex = -1;

    [SerializeField] private string spriteSheetPath_CAPS = "Letters/fontcaps";
    [SerializeField] private string spriteSheetPath_lower = "Letters/fontLower";
    [SerializeField] private string descenderChars = "gjpqy";
    [SerializeField] private float cellSizeX = 0.3f;
    [SerializeField] private float cellSizeY = 1f;
    [SerializeField] private float descenderOffset = 0.3f;
    [SerializeField] private float buttonSpacingY = 0.8f; // vertical gap between stacked buttons
    [SerializeField] private int frameNumber = 24;
    [SerializeField] private bool loop = false;
    [SerializeField] private int totalFrames = 24;
    public float fps = 24f; 

    [Header("Surface sticking")]
    [Tooltip("Off = the original flat stack of buttons. On = each reply is raycast onto the geometry in front of it, and its click collider comes along.")]
    [SerializeField] private SurfaceSnapSettings surface = new SurfaceSnapSettings();
    

    public Dictionary<string, bool> clickedResponses = new Dictionary<string, bool>();

    private List<ResponseButton> buttons = new List<ResponseButton>();
    private int interactableMask;
    private bool responseChosen = false;
    private bool hasBuiltSinceEnable = false;

    // Reset whenever this object transitions to active — including as a cascade side-effect
    // of a parent FONT reactivating. This runs synchronously, before Start() (which Unity defers),
    // so it's always fresh by the time either Start() or Activate() checks it below.
    void OnEnable()
    {
        hasBuiltSinceEnable = false;
    }

    // Called by the SentenceBuilder this group is linked to, the moment its question is
    // actually revealed — keeps buttons hidden until the matching FONT text appears.
    public void Activate()
    {
        gameObject.SetActive(true);
        TryBuildButtonsOnce();
    }

    void OnValidate()
    {
        if (surface == null) surface = new SurfaceSnapSettings();

        #if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            if (!gameObject.activeInHierarchy) return;
            BuildButtons();
        };
        #endif
    }
    void Awake()
    {
        if (surface == null) surface = new SurfaceSnapSettings();
        interactableMask = LayerMask.GetMask("Interactable");
    }

    void Start()
    {
        TryBuildButtonsOnce();
    }

    // Whichever of Start() or Activate() actually runs first wins and builds — the other
    // sees hasBuiltSinceEnable already true and safely skips. This is immune to the ordering
    // race that activeSelf-checking couldn't reliably detect when this object is a child of
    // the FONT that reveals it (parent SetActive(true) cascades activation before Start() fires).
    void TryBuildButtonsOnce()
    {
        if (hasBuiltSinceEnable) return;
        hasBuiltSinceEnable = true;
        BuildButtons();
    }

    void Update()
    {
        if (!Application.isPlaying || responseChosen || Mouse.current == null || Camera.main == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit hit;
        ResponseButton hovered = null;

        if (Physics.Raycast(ray, out hit, Mathf.Infinity, interactableMask))
        {
            hovered = hit.collider.GetComponent<ResponseButton>();
        }

        foreach (ResponseButton btn in buttons)
        {
            btn.SetHovered(btn == hovered);
        }

        if (hovered != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            responseChosen = true;
            clickedResponses[hovered.OptionText] = true;
            int idx = buttons.IndexOf(hovered);
            ClickedIndex = idx;
            ChosenNextGroup = responses[idx].nextGroup;
            Debug.Log($"Response selected: {hovered.OptionText}");
            StartCoroutine(PlayDisappearThenDeactivate());
        }
    }

    // Plays each button letter's disappear animation (frames counting back down), then
    // waits for it to finish before deactivating the whole response group.
    IEnumerator PlayDisappearThenDeactivate()
    {
        foreach (ResponseButton btn in buttons)
        {
            btn.SetHovered(false); // reset to normal tint so hoverTint doesn't bleed through the fade
        }

        foreach (Text_Iteration_And_Animation_Sprites anim in GetComponentsInChildren<Text_Iteration_And_Animation_Sprites>())
        {
            anim.TriggerDisappear();
        }

        // Wait for the real animation to finish rather than estimating with totalFrames/fps —
        // that estimate drifts short at low framerates (same fix as SentenceBuilder).
        yield return new WaitUntil(() =>
        {
            foreach (Text_Iteration_And_Animation_Sprites anim in GetComponentsInChildren<Text_Iteration_And_Animation_Sprites>())
            {
                if (!anim.disappearFinished) return false;
            }
            return true;
        });

        gameObject.SetActive(false);
    }

    void BuildButtons()
    {
        ClearButtons();
        clickedResponses.Clear();
        buttons.Clear();
        ClickedIndex = -1;
        ChosenNextGroup = null;
        responseChosen = false;

        // Reply FONT objects shouldn't be visible until their response is actually picked —
        // enforce this here instead of relying on each one being set up correctly by hand.
        foreach (ResponseOption option in responses)
        {
            if (option.nextGroup != null)
            {
                option.nextGroup.SetActive(false);
            }
        }

        int interactableLayer = LayerMask.NameToLayer("Interactable");

        for (int i = 0; i < responses.Count; i++)
        {
            ResponseOption option = responses[i];
            clickedResponses[option.text] = false;

            GameObject buttonRoot = new GameObject("Button_" + option.text);
            buttonRoot.transform.SetParent(transform, false);
            buttonRoot.transform.localPosition = new Vector3(0f, -i * buttonSpacingY, 0f);
            buttonRoot.layer = interactableLayer;

            // Snap the whole button (letters AND its click collider) onto the surface before the
            // letters are laid out inside it, so they inherit the snapped frame for free.
            if (surface != null && surface.stickToSurfaces)
            {
                TextLayoutEngine.SnapTransform(buttonRoot.transform, surface, transform);
            }

            ResponseButton rb = buttonRoot.AddComponent<ResponseButton>();
            rb.Setup(option);

            // Letters live under their own "Letter Pngs" child, same as SentenceBuilder,
            // just to keep the Hierarchy readable when scanning through button objects.
            GameObject lettersContainer = new GameObject("Letter Pngs");
            lettersContainer.transform.SetParent(buttonRoot.transform, false);

            List<SpriteRenderer> renderers = new List<SpriteRenderer>();
            int column = 0;

            foreach (char ch in option.text)
            {
                if (ch == ' ') { column++; continue; }

                string sheetPath = char.IsUpper(ch) ? spriteSheetPath_CAPS : spriteSheetPath_lower;
                bool isDescender = descenderChars.IndexOf(ch) >= 0;

                GameObject letterObj = new GameObject("Letter_" + column + "_" + ch);
                letterObj.transform.SetParent(lettersContainer.transform, false);
                float dropAmount = isDescender ? descenderOffset : 0f;
                letterObj.transform.localPosition = new Vector3(column * cellSizeX, -dropAmount, 0f);

                Text_Iteration_And_Animation_Sprites anim = letterObj.AddComponent<Text_Iteration_And_Animation_Sprites>();
                anim.spriteSheetPath_CAPS = sheetPath;
                anim.spriteName = ch.ToString();
                anim.frameNumber = frameNumber;
                anim.totalFrames = totalFrames;
                anim.fps = fps;
                anim.letterTint = option.tint;
                anim.loop = loop;
                anim.clickToDisappear = false; // buttons control their own disappearance via ResponseButtonGroup, not a global click

                SpriteRenderer sr = anim.EnsureSpawned(); // spawn immediately and grab the real renderer, no Start() timing dependency
                renderers.Add(sr);

                column++;
            }

            BoxCollider col = buttonRoot.AddComponent<BoxCollider>();
            float width = Mathf.Max(column * cellSizeX, cellSizeX);
            col.size = new Vector3(width, cellSizeY, 0.1f);   // small Z depth instead of 2D's implicit flatness
            col.center = new Vector3((width - cellSizeX) / 2f, 0f, 0f);
            rb.SetRenderers(renderers);
            buttons.Add(rb);
        }
    }

    Sprite LoadFrameSprite(string basePath, int frame, string letter)
    {
        string path = basePath + "/" + frame.ToString("00");
        Sprite[] all = Resources.LoadAll<Sprite>(path);
        foreach (Sprite s in all)
        {
            if (s.name == letter) return s;
        }
        Debug.LogError($"No sprite named '{letter}' found in {path}");
        return null;
    }

    void ClearButtons()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform childT = transform.GetChild(i);
            if (childT.name.StartsWith("Button_"))
            {
                if (Application.isPlaying) Destroy(childT.gameObject);
                else DestroyImmediate(childT.gameObject);
            }
        }
    }
}