using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Which note of the octave a key, or the recorded sample, is.</summary>
public enum PianoNote
{
    [InspectorName("C")] C = 0,
    [InspectorName("C#")] CSharp = 1,
    [InspectorName("D")] D = 2,
    [InspectorName("D#")] DSharp = 3,
    [InspectorName("E")] E = 4,
    [InspectorName("F")] F = 5,
    [InspectorName("F#")] FSharp = 6,
    [InspectorName("G")] G = 7,
    [InspectorName("G#")] GSharp = 8,
    [InspectorName("A")] A = 9,
    [InspectorName("A#")] ASharp = 10,
    [InspectorName("B")] B = 11,
}

/// <summary>
/// A playable piano. Every key plays a real musical note, in tune.
///
/// SETUP (everything below is already done in the scene — this is for when you build
/// another piano or add keys later):
///
///   1. Put this component on the piano root (the object named "piano").
///   2. Drag the folder that holds the keys into "Keys Parent" (here: piano/KEYS).
///      Every direct child of that folder with a Mesh Renderer becomes one key.
///   3. Every key needs a Collider and must be on the Interactable layer, because that is
///      what the raycast looks for. Missing colliders are added automatically at start,
///      and a warning is printed if a key is on the wrong layer.
///   4. Drag an AudioClip of ONE piano note into "Key Sound", then tell the piano which
///      note it is (Sample Note + Sample Octave). Everything else is tuned from that.
///   5. Put any other sounds you want to try in the folder named in "Samples Folder"
///      (here: Assets/Resources/Piano_sounds). Everything in that folder is loaded at
///      start, so adding a file is enough — no dragging needed.
///
/// HOW IT WORKS
///
/// The keys do not each carry a script. This one component owns all of them, so there are
/// 58 components fewer in the scene and no per-key wiring: at start it collects the keys,
/// works out which way the keyboard runs in the world, sorts the keys along that axis and
/// gives each one the next note up the scale. Looking at a key highlights it, pressing E or
/// the left mouse button plays it.
///
/// The notes are real pitches, not a slide between two pitches. A key plays its sample at
/// the exact speed ratio between its note and the sample's note:
///
///     pitch = 2 ^ ((key note - sample note) / 12)
///
/// so a C is always a C no matter which keys are next to it, and a note one semitone away
/// is always the same ratio apart. Going UP is cheap; going DOWN costs quality, because a
/// slowed-down recording loses its attack and its brightness. Unity only allows playback
/// speeds between 0.01 and 3, so an octave or two below the sample is all you really get
/// before it stops sounding like a piano.
///
/// The sound itself can be swapped while playing: the up and down arrow keys step through
/// every clip in the samples folder, and each switch plays the new sound once, untouched, so
/// it is clear what was picked. The clip in "Key Sound" is only the one it starts on.
/// </summary>
[DisallowMultipleComponent]
public class Piano : MonoBehaviour
{
    // ------------------------------------------------------------------ inspector

    [Header("Sound")]

    [Tooltip("The sound the piano starts on. Any clip works — a piano note, a bell, a honk. " +
             "One recorded note is enough for the whole keyboard, as long as you say below " +
             "which note that recording is.\n\n" +
             "The other sounds live in the Sound Library below and are switched with the " +
             "arrow keys.")]
    public AudioClip keySound;

    [Range(0f, 1f)]
    [Tooltip("Volume of every note.")]
    public float volume = 0.9f;

    [Range(0f, 1f)]
    [Tooltip("1 = the sound comes from where the key is, so low keys are heard from the left " +
             "and high keys from the right. 0 = the same volume everywhere in the room.")]
    public float spatialBlend = 1f;

    [Range(1, 32)]
    [Tooltip("How many notes can ring at the same time. Each note needs its own audio voice, " +
             "because changing the pitch of a voice also changes the note it is already " +
             "playing. 12 is plenty for normal playing.")]
    public int maxSimultaneousNotes = 12;

    [Tooltip("Longest a single note may ring, in seconds. 0 = no limit.\n\n" +
             "Low keys play the sample very slowly, so a short recording becomes a very long " +
             "note: at 0.06x a 1 second clip lasts 16 seconds. This fades the note away " +
             "instead, so a few low keys cannot drown the rest of the keyboard.")]
    public float noteLengthLimit = 4f;

    [Range(0.01f, 2f)]
    [Tooltip("How long the note above takes to fade out once Note Length Limit is reached.")]
    public float noteFadeSeconds = 0.25f;

    [Header("Sound Library")]

    [Tooltip("A folder inside a Resources folder that holds the sounds you can flip through " +
             "while playing (here: Assets/Resources/Piano_sounds).\n\n" +
             "Every audio clip in it, and in its subfolders, is loaded at start, so dropping a " +
             "new file in that folder is all it takes — it appears on the next play.")]
    public string samplesFolder = "Piano_sounds";

    [Tooltip("Press the up arrow for the next sound and the down arrow for the previous one, " +
             "while standing at the piano.")]
    public bool cycleSoundsWithArrowKeys = true;

    [Header("Tuning")]

    [Tooltip("The note your Key Sound clip was recorded at. The whole keyboard is tuned " +
             "relative to this, so a clip that is really a C must be set to C and 5 — " +
             "otherwise every key is transposed.\n\n" +
             "Reference: C5 = 523.25 Hz, A4 = 440 Hz.")]
    public PianoNote sampleNote = PianoNote.C;

    [Range(0, 9)]
    [Tooltip("The octave your Key Sound clip was recorded in. Leave this at 5 for a 523 Hz " +
             "piano C.")]
    public int sampleOctave = 5;

    [Tooltip("The note of the FIRST (leftmost) key. Every following key is one semitone " +
             "higher, so with 58 keys a C 1 start runs up to A 5.")]
    public PianoNote firstKeyNote = PianoNote.C;

    [Range(0, 9)]
    [Tooltip("The octave of the first key. Raise this to move the whole keyboard up; the top " +
             "keys then run into Unity's 3x playback limit and get cut off.")]
    public int firstKeyOctave = 1;

    [Range(1, 12)]
    [Tooltip("How many semitones each key is above the one before it. 1 = a real chromatic " +
             "keyboard. Only raise this if you deliberately want gaps between the keys.")]
    public int semitonesPerKey = 1;

    [Tooltip("Tick this if the keyboard plays upside down — the left of the piano should " +
             "sound low and the right high.")]
    public bool reversePitchOrder = false;

    [Header("Hover")]

    [Tooltip("Colour a key turns while the player is aiming at it. It only tints that one key; " +
             "the material asset itself is never modified.")]
    public Color hoverColor = new Color(1f, 0.85f, 0.4f, 1f);

    [Header("Interaction")]

    [Tooltip("How far the player can be from a key and still play it. Keep this the same as " +
             "the player's Interact Range so the piano behaves like every other interactable.")]
    public float interactRange = 3f;

    [Tooltip("The layer the keys must be on. The raycast only looks at these layers, exactly " +
             "like the player's own interaction raycast.")]
    public LayerMask interactableLayer = 1 << 6;

    [Tooltip("Play the key that is being aimed at when the interact key (E) is pressed.")]
    public bool playWithInteractKey = true;

    [Tooltip("Also play the key on a left mouse click. This only works while the mouse is " +
             "captured by the first person controller, so clicking a menu never plays a note.")]
    public bool playWithMouseClick = true;

    [Header("Keys")]

    [Tooltip("The folder that holds the keys. Only its direct children are used. Left empty, " +
             "a child named KEYS is looked for automatically.")]
    public Transform keysParent;

    [Tooltip("Give a key a Mesh Collider automatically when it has none. Handy when you add " +
             "keys to the model later; leave it on.")]
    public bool addMissingMeshColliders = true;

    // ------------------------------------------------------------------ constants

    // Unity clamps AudioSource.pitch to this range. A key that wants to go further would
    // simply sound like its neighbour, so keys are clamped and a warning points it out.
    private const float MinPitch = 0.01f;
    private const float MaxPitch = 3f;

    private static readonly string[] NoteNames =
    {
        "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B",
    };

    // ------------------------------------------------------------------ shader ids

    // Looked up by name every frame would be wasteful, so they are resolved once. Both the
    // URP name and the built-in name are set: whichever the key material uses wins, and the
    // other one is simply ignored.
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    // ------------------------------------------------------------------ state

    /// <summary>One key: its renderers, its colliders, and the pitch worked out for it.</summary>
    private class Key
    {
        public Transform transform;
        public Renderer[] renderers;
        public MaterialPropertyBlock[] originalBlocks;
        public Bounds centerInWorld;
        public float pitch;
    }

    /// <summary>One pooled AudioSource, plus when its current note should start fading.</summary>
    private class Voice
    {
        public AudioSource source;
        public float fadeStartTime;
    }

    private readonly List<Key> keys = new List<Key>();
    private readonly Dictionary<Collider, Key> keyByCollider = new Dictionary<Collider, Key>();
    private readonly List<Voice> voices = new List<Voice>();

    // Every clip found in the samples folder, in name order, plus the one the piano is on.
    private readonly List<AudioClip> sampleClips = new List<AudioClip>();
    private int sampleIndex;

    // Middle of the keyboard, used to decide whether the player is close enough to switch
    // sounds with the arrow keys, and where the preview note is heard from.
    private Vector3 keyboardCenter;

    private MaterialPropertyBlock hoverBlock;
    private Key hovered;
    private int nextVoice;
    private bool warnedAboutClip;
    private bool warnedAboutLayer;

    private Transform viewCamera;

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        hoverBlock = new MaterialPropertyBlock();

        CollectKeys();
        AssignNotes();
        LoadSamples();
        BuildVoices();
    }

    // Keeps the note settings sensible while they are being edited in the Inspector.
    void OnValidate()
    {
        sampleOctave = Mathf.Clamp(sampleOctave, 0, 9);
        firstKeyOctave = Mathf.Clamp(firstKeyOctave, 0, 9);
        semitonesPerKey = Mathf.Clamp(semitonesPerKey, 1, 12);
        maxSimultaneousNotes = Mathf.Clamp(maxSimultaneousNotes, 1, 32);
        noteLengthLimit = Mathf.Max(0f, noteLengthLimit);
        noteFadeSeconds = Mathf.Max(0.01f, noteFadeSeconds);
    }

    // ------------------------------------------------------------------ key discovery

    void CollectKeys()
    {
        keys.Clear();
        keyByCollider.Clear();

        Transform parent = keysParent != null ? keysParent : FindKeysFolder();

        if (parent == null)
        {
            Debug.LogError(
                "Piano on '" + name + "': no keys folder. Drag the object that holds the keys " +
                "into 'Keys Parent', or rename that folder to KEYS.", this);
            return;
        }

        foreach (Transform child in parent)
        {
            if (child.GetComponentInChildren<Renderer>(true) == null) continue;
            keys.Add(BuildKey(child.gameObject));
        }

        if (keys.Count == 0)
        {
            Debug.LogError("Piano on '" + name + "': '" + parent.name + "' has no key objects in it.", this);
            return;
        }

        WarnAboutLayers();
    }

    Transform FindKeysFolder()
    {
        foreach (Transform child in transform)
        {
            if (string.Equals(child.name, "KEYS", System.StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }
        return null;
    }

    Key BuildKey(GameObject keyObject)
    {
        Key key = new Key();
        key.transform = keyObject.transform;
        key.renderers = keyObject.GetComponentsInChildren<Renderer>(true);

        Collider[] colliders = keyObject.GetComponentsInChildren<Collider>(true);

        if (colliders.Length == 0 && addMissingMeshColliders)
        {
            // The collider goes on the object that owns the mesh, not blindly on the key
            // root: a Mesh Collider sitting on the wrong transform would cover the wrong
            // piece of the piano.
            MeshFilter filter = keyObject.GetComponentInChildren<MeshFilter>(true);

            if (filter != null && filter.sharedMesh != null)
            {
                MeshCollider added = filter.gameObject.AddComponent<MeshCollider>();
                added.sharedMesh = filter.sharedMesh;
                colliders = new Collider[] { added };
            }
            else
            {
                Debug.LogWarning(
                    "Piano on '" + name + "': key '" + keyObject.name + "' has no collider and " +
                    "no mesh to build one from, so it cannot be played.", this);
            }
        }

        foreach (Collider collider in colliders)
        {
            if (collider != null) keyByCollider[collider] = key;
        }

        // The world centre of the key. The transforms of this model all sit at the same
        // point (the key shapes live inside the mesh data), so the transform position would
        // be the same for every key. The renderer bounds are the real position.
        key.centerInWorld = WorldBounds(key.renderers, keyObject.transform.position);

        // Remember what each renderer looked like before the highlight touched it, so the
        // key can be restored exactly instead of hoping the material is untouched.
        key.originalBlocks = new MaterialPropertyBlock[key.renderers.Length];
        for (int i = 0; i < key.renderers.Length; i++)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            if (key.renderers[i] != null) key.renderers[i].GetPropertyBlock(block);
            key.originalBlocks[i] = block;
        }

        return key;
    }

    static Bounds WorldBounds(Renderer[] renderers, Vector3 fallbackCenter)
    {
        bool found = false;
        Bounds bounds = new Bounds(fallbackCenter, Vector3.zero);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return bounds;
    }

    void WarnAboutLayers()
    {
        if (warnedAboutLayer) return;

        int wrongLayer = 0;

        foreach (Key key in keys)
        {
            if ((interactableLayer.value & (1 << key.transform.gameObject.layer)) == 0) wrongLayer++;
        }

        if (wrongLayer > 0)
        {
            warnedAboutLayer = true;
            Debug.LogWarning(
                "Piano on '" + name + "': " + wrongLayer + " of " + keys.Count + " keys are not " +
                "on the Interactable layer, so the raycast cannot see them. Select the keys and " +
                "set their layer to Interactable.", this);
        }
    }

    // ------------------------------------------------------------------ tuning

    /// <summary>Note + octave to a MIDI number, the usual "C4 = 60" scheme.</summary>
    static int ToMidi(PianoNote note, int octave)
    {
        return (Mathf.Clamp(octave, 0, 9) + 1) * 12 + (int)note;
    }

    static string NoteName(int midi)
    {
        int index = ((midi % 12) + 12) % 12;
        int octave = Mathf.FloorToInt(midi / 12f) - 1;
        return NoteNames[index] + octave.ToString(CultureInfo.InvariantCulture);
    }

    static string NumberText(float value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Sorts the keys along the long axis of the keyboard and gives each one its own note.
    ///
    /// The long axis is measured from the keys themselves instead of being assumed to be X
    /// or Z, so the same script works if the piano is rotated or rebuilt. Keys that sit at
    /// the exact same spot (the black keys are modelled as a strip right behind their white
    /// key) are separated by the other horizontal axis, so every key still gets its own note.
    /// </summary>
    void AssignNotes()
    {
        if (keys.Count == 0) return;

        Bounds all = keys[0].centerInWorld;
        for (int i = 1; i < keys.Count; i++) all.Encapsulate(keys[i].centerInWorld);

        keyboardCenter = all.center;

        int primary = all.size.x >= all.size.z ? 0 : 2;   // 0 = X, 2 = Z
        int secondary = primary == 0 ? 2 : 0;

        keys.Sort((a, b) =>
        {
            float pa = a.centerInWorld.center[primary];
            float pb = b.centerInWorld.center[primary];
            if (Mathf.Abs(pa - pb) > 0.0005f) return pa < pb ? -1 : 1;

            float sa = a.centerInWorld.center[secondary];
            float sb = b.centerInWorld.center[secondary];
            if (Mathf.Abs(sa - sb) > 0.0005f) return sa < sb ? -1 : 1;

            return 0;
        });

        if (reversePitchOrder) keys.Reverse();

        int sampleMidi = ToMidi(sampleNote, sampleOctave);
        int firstMidi = ToMidi(firstKeyNote, firstKeyOctave);
        int step = Mathf.Max(1, semitonesPerKey);

        int clampedKeys = 0;

        for (int i = 0; i < keys.Count; i++)
        {
            int midi = firstMidi + i * step;

            // The real rule behind equal temperament: every semitone is the twelfth root of
            // two apart. This is what makes the keys land on the same frequencies as a real
            // piano (A4 440 Hz, C5 523.25 Hz...) instead of the continuous slide the older
            // version used, which was out of tune between the two end points.
            float pitch = Mathf.Pow(2f, (midi - sampleMidi) / 12f);
            float playable = Mathf.Clamp(pitch, MinPitch, MaxPitch);

            if (!Mathf.Approximately(playable, pitch)) clampedKeys++;

            keys[i].pitch = playable;
        }

        int lastMidi = firstMidi + (keys.Count - 1) * step;
        int lowMidi = Mathf.Min(firstMidi, lastMidi);
        int highMidi = Mathf.Max(firstMidi, lastMidi);

        float lowSpeed = Mathf.Pow(2f, (lowMidi - sampleMidi) / 12f);
        float highSpeed = Mathf.Pow(2f, (highMidi - sampleMidi) / 12f);

        Debug.Log(
            "Piano on '" + name + "': " + keys.Count + " keys, " + NoteName(lowMidi) + " to " +
            NoteName(highMidi) + ". Sample is " + NoteName(sampleMidi) + ", so the keys play at " +
            NumberText(lowSpeed) + "x to " + NumberText(highSpeed) + "x speed.", this);

        if (clampedKeys > 0)
        {
            Debug.LogWarning(
                "Piano on '" + name + "': " + clampedKeys + " of " + keys.Count + " keys need a " +
                "speed outside Unity's " + NumberText(MinPitch) + "x to " + NumberText(MaxPitch) +
                "x limit, so they are clamped and will sound like their neighbour. Move 'First " +
                "Key Octave' down until the warning stops.", this);
        }
    }

    // ------------------------------------------------------------------ sound library

    /// <summary>
    /// Collects every clip in the samples folder, in a stable order, so a new file only has
    /// to be dropped into that folder to show up.
    /// </summary>
    void LoadSamples()
    {
        sampleClips.Clear();

        if (!string.IsNullOrEmpty(samplesFolder))
        {
            AudioClip[] found = Resources.LoadAll<AudioClip>(samplesFolder);

            System.Array.Sort(found, (a, b) =>
                string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));

            foreach (AudioClip clip in found)
            {
                if (clip != null && !sampleClips.Contains(clip)) sampleClips.Add(clip);
            }
        }

        // "Key Sound" is the sound it starts on. If that clip does not live in the folder it
        // is put in front, so a clip dragged straight into the field still plays.
        sampleIndex = 0;

        if (keySound != null)
        {
            int existing = sampleClips.IndexOf(keySound);
            if (existing >= 0) sampleIndex = existing;
            else sampleClips.Insert(0, keySound);
        }

        if (sampleClips.Count == 0)
        {
            Debug.LogWarning(
                "Piano on '" + name + "': no sounds found. Put audio clips in Resources/'" +
                samplesFolder + "' or assign one in 'Key Sound'.", this);
            return;
        }

        Debug.Log(
            "Piano on '" + name + "': " + sampleClips.Count + " sounds loaded, starting on '" +
            sampleClips[sampleIndex].name + "'. Up/Down arrows switch sound while standing at " +
            "the piano.", this);
    }

    /// <summary>The clip every key is currently playing.</summary>
    AudioClip CurrentClip
    {
        get
        {
            if (sampleClips.Count == 0) return keySound;
            return sampleClips[Mathf.Clamp(sampleIndex, 0, sampleClips.Count - 1)];
        }
    }

    void CycleSample(int direction)
    {
        if (sampleClips.Count < 2) return;

        sampleIndex = (sampleIndex + direction + sampleClips.Count) % sampleClips.Count;
        AudioClip clip = sampleClips[sampleIndex];

        Debug.Log(
            "Piano on '" + name + "': sound " + (sampleIndex + 1) + "/" + sampleClips.Count +
            " — " + clip.name, this);

        // Play the new sound exactly as recorded, so it is obvious what was picked instead of
        // hearing it through the keyboard's transposition.
        PlayClip(clip, 1f, keyboardCenter);
    }

    // ------------------------------------------------------------------ audio

    void BuildVoices()
    {
        foreach (Voice voice in voices)
        {
            if (voice != null && voice.source != null) Destroy(voice.source.gameObject);
        }
        voices.Clear();

        int count = Mathf.Clamp(maxSimultaneousNotes, 1, 32);

        for (int i = 0; i < count; i++)
        {
            GameObject host = new GameObject("~piano voice " + i);
            host.transform.SetParent(transform, false);

            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 1f;
            source.maxDistance = 25f;

            voices.Add(new Voice { source = source, fadeStartTime = Mathf.Infinity });
        }
    }

    /// <summary>Plays one clip on a free voice, from a point in the world.</summary>
    void PlayClip(AudioClip clip, float pitch, Vector3 at)
    {
        if (clip == null) return;

        Voice voice = NextVoice();
        if (voice == null) return;

        AudioSource source = voice.source;
        source.transform.position = at;     // the note is heard from the key that was pressed
        source.clip = clip;
        source.pitch = pitch;
        source.volume = volume;
        source.spatialBlend = spatialBlend;
        source.Play();

        voice.fadeStartTime = noteLengthLimit > 0f ? Time.time + noteLengthLimit : Mathf.Infinity;
    }

    void PlayNote(Key key, Vector3 at)
    {
        AudioClip clip = CurrentClip;

        if (clip == null)
        {
            if (!warnedAboutClip)
            {
                warnedAboutClip = true;
                Debug.LogWarning(
                    "Piano on '" + name + "': no sound assigned, so the keys highlight but stay " +
                    "silent. Drag a clip into Key Sound, or drop clips into Resources/'" +
                    samplesFolder + "'.", this);
            }
            return;
        }

        PlayClip(clip, key.pitch, at);
    }

    Voice NextVoice()
    {
        if (voices.Count == 0) return null;

        // Prefer a voice that has finished, so a ringing note is never retuned underneath the
        // player. When every voice is busy, the oldest one is reused.
        for (int i = 0; i < voices.Count; i++)
        {
            int index = (nextVoice + i) % voices.Count;
            Voice voice = voices[index];
            if (voice == null || voice.source == null) continue;

            if (!voice.source.isPlaying)
            {
                nextVoice = (index + 1) % voices.Count;
                return voice;
            }
        }

        Voice oldest = voices[nextVoice % voices.Count];
        nextVoice = (nextVoice + 1) % voices.Count;
        return oldest;
    }

    /// <summary>
    /// Ends notes that have been ringing longer than Note Length Limit, fading them so the
    /// cut is inaudible. Without this the lowest keys, which play the sample many times too
    /// slowly, would each hold a voice for ten or twenty seconds.
    /// </summary>
    void UpdateVoices()
    {
        float now = Time.time;
        float fade = Mathf.Max(0.01f, noteFadeSeconds);

        foreach (Voice voice in voices)
        {
            if (voice == null || voice.source == null) continue;
            if (!voice.source.isPlaying) continue;
            if (float.IsInfinity(voice.fadeStartTime)) continue;

            float intoFade = now - voice.fadeStartTime;
            if (intoFade < 0f) continue;

            if (intoFade >= fade)
            {
                voice.source.Stop();
                voice.source.volume = volume;
                continue;
            }

            voice.source.volume = volume * (1f - intoFade / fade);
        }
    }

    // ------------------------------------------------------------------ hover + input

    void Update()
    {
        UpdateVoices();
        HandleSoundSwitching();

        // An inspected object is floating in front of the camera: the player's attention is
        // there, so keys must not highlight or play underneath it.
        if (ItemInspector.IsInspecting)
        {
            SetHovered(null);
            return;
        }

        Key key;
        Vector3 hitPoint;

        if (!FindKeyUnderCrosshair(out key, out hitPoint))
        {
            SetHovered(null);
            return;
        }

        SetHovered(key);

        if (PressedPlayThisFrame()) PlayNote(key, hitPoint);
    }

    /// <summary>Up / down arrow: step through the sounds in the samples folder.</summary>
    void HandleSoundSwitching()
    {
        if (!cycleSoundsWithArrowKeys || Keyboard.current == null) return;
        if (sampleClips.Count < 2) return;

        // While seated the arrow keys move the chair, and an inspected object owns the input,
        // so the piano stays out of the way in  both cases.
        if (ChairSeat.IsSeated || ItemInspector.IsInspecting) return;

        // Only next to the piano: an arrow pressed across the room should not change its sound.
        if (!IsPlayerAtKeyboard()) return;

        if (Keyboard.current.xKey.wasPressedThisFrame) CycleSample(1);
        else if (Keyboard.current.zKey.wasPressedThisFrame) CycleSample(-1);
    }

    bool IsPlayerAtKeyboard()
    {
        // A menu with a free cursor is not playing the piano.
        if (Cursor.lockState != CursorLockMode.Locked) return false;

        Transform cameraTransform = ResolveCamera();
        if (cameraTransform == null) return false;

        // "At the piano" means within reach of the keys — the same distance needed to press one.
        return (cameraTransform.position - keyboardCenter).sqrMagnitude <= interactRange * interactRange;
    }

    void SetHovered(Key key)
    {
        if (ReferenceEquals(key, hovered)) return;

        if (hovered != null) Highlight(hovered, false);
        hovered = key;
        if (hovered != null) Highlight(hovered, true);
    }

    /// <summary>
    /// Tints one key, or puts it back the way it was.
    ///
    /// A MaterialPropertyBlock is used instead of changing the material, because every key on
    /// this piano shares the same two materials — writing to the material would light up the
    /// whole keyboard at once and would also edit the asset on disk.
    /// </summary>
    void Highlight(Key key, bool on)
    {
        if (key == null || key.renderers == null) return;

        for (int i = 0; i < key.renderers.Length; i++)
        {
            Renderer renderer = key.renderers[i];
            if (renderer == null) continue;

            if (on)
            {
                hoverBlock.Clear();
                hoverBlock.SetColor(BaseColorId, hoverColor);   // URP Lit and Simple Lit
                hoverBlock.SetColor(ColorId, hoverColor);       // built-in / Standard
                renderer.SetPropertyBlock(hoverBlock);
            }
            else
            {
                renderer.SetPropertyBlock(key.originalBlocks[i]);
            }
        }
    }

    bool FindKeyUnderCrosshair(out Key key, out Vector3 hitPoint)
    {
        key = null;
        hitPoint = default;

        Transform cameraTransform = ResolveCamera();
        if (cameraTransform == null) return false;

        // The same idea as PlayerInteractor: one ray straight out of the camera, only the
        // interactable layers. The piano shell is on Default, so it never blocks a key.
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer,
                             QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (!keyByCollider.TryGetValue(hit.collider, out key))
        {
            key = null;
            return false;
        }

        hitPoint = hit.point;
        return true;
    }

    bool PressedPlayThisFrame()
    {
        if (playWithMouseClick && Mouse.current != null &&
            Cursor.lockState == CursorLockMode.Locked &&
            Mouse.current.leftButton.wasPressedThisFrame)
        {
            return true;
        }

        if (!playWithInteractKey) return false;

        // While seated, E belongs to the chair (stand up), so the piano leaves it alone.
        if (ChairSeat.IsSeated) return false;

        // The same E press that puts an inspected object down must not also play a note.
        if (Time.frameCount == ItemInspector.ReleaseLockFrame) return false;

        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    }

    Transform ResolveCamera()
    {
        if (viewCamera != null) return viewCamera;

        FirstPersonController player = FindAnyObjectByType<FirstPersonController>();

        if (player != null && player.cam != null)
        {
            viewCamera = player.cam;
        }
        else if (Camera.main != null)
        {
            viewCamera = Camera.main.transform;
        }

        return viewCamera;
    }
}
