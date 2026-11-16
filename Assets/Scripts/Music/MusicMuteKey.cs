using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Do not add this by hand — MusicPlayer creates it automatically when the game starts.
/// It exists so the M key still works while every music object is switched off: something has to be
/// running to read the key, and a switched-off MusicPlayer cannot.
/// </summary>
[AddComponentMenu("")]
public class MusicMuteKey : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateIfMissing()
    {
        if (FindAnyObjectByType<MusicMuteKey>() != null) return;

        GameObject go = new GameObject("Music mute key (auto)");
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<MusicMuteKey>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (!keyboard[MusicPlayer.MuteKey].wasPressedThisFrame) return;

        MusicPlayer.ToggleMuteFromKeyPress();
    }
}
