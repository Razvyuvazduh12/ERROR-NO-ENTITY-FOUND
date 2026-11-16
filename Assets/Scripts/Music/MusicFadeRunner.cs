using UnityEngine;

/// <summary>
/// Do not add this by hand — MusicPlayer spawns it automatically.
/// A switched-off GameObject cannot play audio, so when a MusicPlayer is switched off mid-song it
/// hands the last few seconds to this temporary object, which keeps the song fading and then
/// deletes itself.
/// </summary>
[AddComponentMenu("")]
public class MusicFadeRunner : MonoBehaviour
{
    private AudioSource source;
    private float startVolume;
    private float seconds;
    private float elapsed;

    /// <summary>Takes over a playing song and fades it to silence.</summary>
    internal static MusicFadeRunner Begin(AudioSource original, float startVolume, float seconds, float startTime)
    {
        if (original == null || original.clip == null) return null;

        GameObject go = new GameObject("Music fade (auto)");
        go.hideFlags = HideFlags.HideInHierarchy;

        AudioSource ghost = go.AddComponent<AudioSource>();
        ghost.clip = original.clip;
        ghost.loop = true;
        ghost.pitch = original.pitch;
        ghost.spatialBlend = 0f;
        ghost.playOnAwake = false;
        ghost.outputAudioMixerGroup = original.outputAudioMixerGroup;
        ghost.volume = startVolume;
        ghost.Play();
        ghost.time = startTime % original.clip.length;   // continue from where the original was cut

        MusicFadeRunner runner = go.AddComponent<MusicFadeRunner>();
        runner.source = ghost;
        runner.startVolume = startVolume;
        runner.seconds = seconds;

        return runner;
    }

    private void Update()
    {
        if (source == null)
        {
            Destroy(gameObject);
            return;
        }

        elapsed += Time.unscaledDeltaTime;

        float t = seconds > 0.0001f ? Mathf.Clamp01(elapsed / seconds) : 1f;
        source.volume = startVolume * (1f - t);

        if (t >= 1f) Cancel();
    }

    /// <summary>Kills the fade immediately — used when the same MusicPlayer comes back before the tail finished.</summary>
    internal void Cancel()
    {
        if (source != null) source.Stop();
        Destroy(gameObject);
    }
}
