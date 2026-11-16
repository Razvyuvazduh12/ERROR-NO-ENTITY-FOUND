using UnityEngine;


public class Trigger_deault : MonoBehaviour
{
    [SerializeField] private Collider triggerCollider;
    [SerializeField] private SentenceBuilder sentenceBuilder;

    private bool hasStarted = false;

    private void Start()
    {
        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null)
            triggerCollider.isTrigger = true;

        if (sentenceBuilder != null)
            sentenceBuilder.gameObject.SetActive(false); // fully disabled until triggered — matches how the rest of the queue hides its "next" objects
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasStarted) return;
        if (!other.CompareTag("Player")) return;

        hasStarted = true;
        Debug.Log("f");

        if (sentenceBuilder != null)
            sentenceBuilder.Reveal(); // Reveal() already safely reactivates a previously-disabled object
    }
}