using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransitionOnReveal : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The SentenceBuilder whose isFinished flag we're watching")]
    [SerializeField] private SentenceBuilder targetSentenceBuilder;

    [Header("Scene To Load")]
    [Tooltip("Build Index of the scene to load (File > Build Settings)")]
    [SerializeField] private int sceneIndex = 1;

    private bool hasTriggered = false;

    private void Update()
    {
        if (hasTriggered) return;

        if (targetSentenceBuilder != null && targetSentenceBuilder.finishedRendering)
        {
            hasTriggered = true;
            SceneManager.LoadScene(sceneIndex);
        }
    }
}