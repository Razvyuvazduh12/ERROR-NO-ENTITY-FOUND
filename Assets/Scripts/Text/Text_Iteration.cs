using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[RequireComponent(typeof(TMP_Text))]
public class TypewriterText : MonoBehaviour
{
    [Header("Text")]
    [Tooltip("Sentence to type out. Assign a custom font on the TMP_Text component itself.")]
    [TextArea(2, 5)]
    [SerializeField] private string sentence;

    [Header("Speed")]
    [Tooltip("Characters revealed per second.")]
    [Range(1f, 60f)]
    [SerializeField] private float charsPerSecond = 20f;

    [Header("Behaviour")]
    [Tooltip("Ignored if Require External Trigger is enabled below.")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool skipOnInput = true;

    [Header("External Gate (mid-typing pause)")]
    [Tooltip("While false, the typewriter pauses between characters. Flip this from other scripts to hold typing until something happens (player reaches a spot, finishes an action, etc).")]
    [SerializeField] private bool canType = true;
    public bool CanType
    {
        get => canType;
        set => canType = value;
    }

    [Header("External Trigger (start condition)")]
    [Tooltip("If enabled, this won't start typing on its own — it waits until ActivationTrigger is set true by another script, ignoring Play On Start.")]
    [SerializeField] private bool requireExternalTrigger = false;

    [Tooltip("Set this true from another script to kick off typing when Require External Trigger is enabled.")]
    [SerializeField] private bool activationTrigger = false;
    public bool ActivationTrigger
    {
        get => activationTrigger;
        set => activationTrigger = value;
    }

    [Header("Hide On Finish")]
    [Tooltip("Hides the text once the sentence finishes typing (or is skipped) — keeps the GameObject and its booleans alive, so other scripts can still read IsSentenceComplete / drive the next line.")]
    [SerializeField] private bool hideOnFinish = false;

    [Tooltip("Delay in seconds after finishing before hiding. 0 = hide immediately.")]
    [SerializeField] private float hideDelay = 0f;

    [Header("State")]
    [Tooltip("Becomes true once the full sentence has been revealed. Other scripts can poll this to know when to trigger the next sentence.")]
    [SerializeField] private bool isSentenceComplete;
    public bool IsSentenceComplete => isSentenceComplete;

    [Header("Events")]
    [Tooltip("Optional push-based alternative to polling IsSentenceComplete.")]
    public UnityEvent OnTypingComplete;

    private TMP_Text label;
    private Coroutine typingRoutine;

    public bool IsTyping { get; private set; }

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
    }

    private void Start()
    {
        if (requireExternalTrigger)
        {
            StartCoroutine(WaitForTriggerThenPlay());
        }
        else if (playOnStart)
        {
            Play();
        }
    }

    private IEnumerator WaitForTriggerThenPlay()
    {
        yield return new WaitUntil(() => activationTrigger);
        Play();
    }

    private void Update()
    {
        if (skipOnInput && IsTyping && LeftClickThisFrame())
        {
            SkipToEnd();
        }
    }

    private bool LeftClickThisFrame()
    {
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    /// <summary>Starts (or restarts) typing the current sentence.</summary>
    public void Play()
    {
        if (typingRoutine != null) StopCoroutine(typingRoutine);
        typingRoutine = StartCoroutine(TypeRoutine());
    }

    /// <summary>Assign a new sentence at runtime, then call Play() to type it.</summary>
    public void SetSentence(string newSentence)
    {
        sentence = newSentence;
    }

    private IEnumerator TypeRoutine()
    {
        IsTyping = true;
        isSentenceComplete = false;
        label.text = "";
        float delay = 1f / Mathf.Max(charsPerSecond, 0.01f);

        for (int i = 0; i < sentence.Length; i++)
        {
            // Pauses here between characters whenever CanType is false.
            yield return new WaitUntil(() => canType);
            label.text += sentence[i];
            yield return new WaitForSeconds(delay);
        }

        FinishTyping();
    }

    /// <summary>Instantly reveals the full sentence and fires the completion state/event.</summary>
    public void SkipToEnd()
    {
        if (typingRoutine != null) StopCoroutine(typingRoutine);
        label.text = sentence;
        FinishTyping();
    }

    private void FinishTyping()
    {
        IsTyping = false;
        isSentenceComplete = true;
        OnTypingComplete?.Invoke();

        if (hideOnFinish)
        {
            StartCoroutine(HideAfterDelay(hideDelay));
        }
    }

    private IEnumerator HideAfterDelay(float delaySeconds)
    {
        if (delaySeconds > 0f) yield return new WaitForSeconds(delaySeconds);
        label.enabled = false;
    }

    /// <summary>Re-enables the text if it was hidden by Hide On Finish.</summary>
    public void Show()
    {
        label.enabled = true;
    }
}