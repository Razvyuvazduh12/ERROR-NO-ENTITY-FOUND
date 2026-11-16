using UnityEngine;

/// <summary>
/// Makes an object inspectable.
///
/// Put this on the SAME GameObject as the Collider that the player's raycast hits —
/// exactly like Doorknob_Intr sits next to the doorknob's collider. PlayerInteractor
/// looks up IInteractable on the hit collider's own GameObject, so a child collider
/// does not work unless it carries its own copy of this component.
///
/// Pressing E while looking at it hands the object to ItemInspector, which lifts it
/// in front of the camera so the mouse can turn it. Press E again to put it back.
/// </summary>
[DisallowMultipleComponent]
public class InspectableItem : MonoBehaviour, IInteractable
{
    [Tooltip("Optional name, for logging / future UI. Leave empty to use the GameObject's name.")]
    [SerializeField] private string displayName = "";

    public string DisplayName =>
        string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;

    void Awake()
    {
        // Create the inspector (and connect its input) before the first interaction,
        // instead of lazily in the middle of the interact callback.
        _ = ItemInspector.Instance;
    }

    public void Interact()
    {
        ItemInspector inspector = ItemInspector.Instance;
        if (inspector != null) inspector.BeginInspect(this);
    }
}
