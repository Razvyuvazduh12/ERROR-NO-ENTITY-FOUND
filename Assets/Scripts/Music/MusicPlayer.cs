using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// One looping song that lives and dies with this GameObject.
/// Switch the object ON  -> the song fades up and loops forever.
/// Switch the object OFF -> the song fades out slowly and stops (see MusicFadeRunner: a switched-off
///                          object can't play audio, so the tail finishes on a temporary object).
/// Pressing M silences every MusicPlayer in the game at once.
///
/// Typical wiring: a FONT's "Is Trigger" UnityEvent calls GameObject.SetActive(true) on an empty
/// object that carries this script. Another FONT (or the same one later) sets it back to false.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour
{
    // ------------------------------------------------------------------ the shared mute flag

    private static bool musicMuted;
    private static bool quitting;
    private static int lastMuteToggleFrame = -1;

    /// <summary>True while every MusicPlayer in the game is muted. The M key flips this.</summary>
    public static bool MusicMuted => musicMuted;

    /// <summary>Which key mutes the music. M unless you change it.</summary>
    public static Key MuteKey { get; set; } = Key.M;

    /// <summary>Mute or unmute every MusicPlayer at once — call this from a pause menu or a settings screen.</summary>
    public static void SetMusicMuted(bool muted)
    {
        musicMuted = muted;
    }

    // Domain reload is disabled in this project, so statics survive between Play sessions.
    // Wipe them here or the game would start already muted.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        musicMuted = false;
        quitting = false;
        MuteKey = Key.M;
        lastMuteToggleFrame = -1;
    }

    /// <summary>
    /// Called by whatever noticed the key this frame (MusicMuteKey, or a MusicPlayer as a fallback).
    /// The frame check means two readers can never flip the flag twice for one press.
    /// </summary>
    internal static void ToggleMuteFromKeyPress()
    {
        if (lastMuteToggleFrame == Time.frameCount) return;
        lastMuteToggleFrame = Time.frameCount;

        musicMuted = !musicMuted;
    }

    // ------------------------------------------------------------------ inspector

    [Header("Song")]
    [Tooltip("The one song this object plays. It loops forever until the object is switched off.")]
    [SerializeField] private AudioClip song;

    [Tooltip("How loud the song is once the fade-in has finished.")]
    [Range(0f, 1f)]
    [SerializeField] private float volume = 0.5f;

    [Header("Fades")]
    [Tooltip("How long the song takes to reach full volume after this object is switched on.")]
    [Min(0f)]
    [SerializeField] private float fadeInSeconds = 1f;

    [Tooltip("How long the song takes to go completely quiet after this object is switched off. This is the slow one.")]
    [Min(0f)]
    [SerializeField] private float fadeOutSeconds = 5f;

    [Tooltip("Quick fade used when the M key mutes, so the cut does not click.")]
    [Min(0f)]
    [SerializeField] private float muteFadeSeconds = 0.35f;

    [Header("Debug")]
    [Tooltip("Log every switch-on / switch-off so you can see what is happening.")]
    [SerializeField] private bool debugLogs = false;

    // ------------------------------------------------------------------ runtime state

    /// <summary>Never null after Awake.</summary>
    private AudioSource source;

    /// <summary>0 = silent, 1 = full volume. Fade-in and mute both move through this.</summary>
    private float level;

    /// <summary>The temporary object still fading out after this one was switched off.</summary>
    private MusicFadeRunner pendingFade;

    /// <summary>
    /// Playback position cached every frame. Unity kills an AudioSource *before* OnDisable runs
    /// (isPlaying turns false and time jumps to 0), so the fade-out cannot ask the source where
    /// the song was — it has to remember.
    /// </summary>
    private float lastTime;

    private bool warnedAboutSong;

    /// <summary>True while the song is actually making noise.</summary>
    public bool IsPlaying => source != null && source.isPlaying && level > 0f;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        // Music is 2D: it must not get quieter as the player walks away from this object.
        source.spatialBlend = 0f;
        source.volume = 0f;
    }

    private void OnEnable()
    {
        // Switched back on while the previous fade-out is still running: kill that tail, it would overlap.
        if (pendingFade != null)
        {
            pendingFade.Cancel();
            pendingFade = null;
        }

        level = 0f;
        lastTime = 0f;

        if (song == null)
        {
            if (!warnedAboutSong)
            {
                warnedAboutSong = true;
                Debug.LogWarning($"[MusicPlayer] '{name}' is switched on but has no song assigned.", this);
            }
            return;
        }

        source.clip = song;
        source.Stop();   // always starts from 0:00

        // Play right away at volume 0. If the game is muted we never get past the fade-in,
        // so nothing is heard anyway.
        source.Play();

        if (debugLogs)
            Debug.Log($"[MusicPlayer] '{name}' switched on", this);
    }

    private void OnDisable()
    {
        // The AudioSource dies the instant this object is switched off, so the fade has to be
        // finished by a temporary object that stays alive.
        // Note: by the time OnDisable runs, Unity has already stopped the source itself —
        // isPlaying is false and time is 0 — so only our own cached values can be trusted.
        bool needsTail = Application.isPlaying
                         && !quitting
                         && source != null
                         && level > 0.001f
                         && fadeOutSeconds > 0.001f
                         && !musicMuted;

        if (needsTail)
        {
            pendingFade = MusicFadeRunner.Begin(source, volume * level, fadeOutSeconds, lastTime);

            if (debugLogs)
                Debug.Log($"[MusicPlayer] '{name}' switched off, fading out over {fadeOutSeconds:0.##}s", this);
        }
        else if (debugLogs)
        {
            Debug.Log($"[MusicPlayer] '{name}' switched off (no fade needed)", this);
        }

        level = 0f;

        if (source != null)
        {
            source.volume = 0f;
            source.Stop();
        }
    }

    private void OnApplicationQuit()
    {
        // Creating objects while the application shuts down throws, so skip the fade there.
        quitting = true;
    }

    private void Update()
    {
        // Fallback reader: MusicMuteKey normally handles the key, but if that object ever goes
        // missing the music must still be muteable while it plays.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[MuteKey].wasPressedThisFrame)
            ToggleMuteFromKeyPress();

        float target = musicMuted ? 0f : 1f;
        float seconds = musicMuted ? muteFadeSeconds : fadeInSeconds;

        level = seconds > 0.0001f
            ? Mathf.MoveTowards(level, target, Time.unscaledDeltaTime / seconds)
            : target;

        ApplyLevel();

        if (source != null && source.isPlaying) lastTime = source.time;
    }

    private void ApplyLevel()
    {
        if (source == null || source.clip == null) return;

        source.volume = volume * level;

        if (level <= 0.0001f)
        {
            // Pause, not Stop: unmuting should resume the same moment of the song.
            if (source.isPlaying) source.Pause();
            return;
        }

        if (source.isPlaying) return;

        if (source.time > 0f && source.time < source.clip.length - 0.05f)
            source.UnPause();
        else
            source.Play();
    }

    // ------------------------------------------------------------------ public API

    /// <summary>Same as switching this GameObject on. Handy for UnityEvents that only offer 0-argument methods.</summary>
    public void Activate()
    {
        gameObject.SetActive(true);
    }

    /// <summary>Same as switching this GameObject off — the song fades out.</summary>
    public void Deactivate()
    {
        gameObject.SetActive(false);
    }
}
