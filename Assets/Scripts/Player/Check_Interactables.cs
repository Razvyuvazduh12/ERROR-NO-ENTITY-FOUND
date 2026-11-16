using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactableLayer;
    [SerializeField] private InputActionReference interactAction;

    private IInteractable currentTarget;

    void OnEnable()
    {
        if (interactAction == null || interactAction.action == null)
        {
            Debug.LogError("Interact Action reference is missing or broken!");
            return;
        }

        interactAction.action.Enable();
        interactAction.action.performed += OnInteractPerformed;
        Debug.Log("Interact action subscribed successfully.");
    }

    void OnDisable()
    {
        if (interactAction != null && interactAction.action != null)
        {
            interactAction.action.performed -= OnInteractPerformed;
            interactAction.action.Disable();
        }
    }

    void Update()
    {
        CheckForInteractable();

        // Raw keyboard check, bypassing the action system entirely
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            Debug.Log("Raw E key press detected (Input System is reading keyboard fine).");
        }
    }

    void CheckForInteractable()
    {
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer))
        {
            currentTarget = hit.collider.GetComponent<IInteractable>();
        }
        else
        {
            currentTarget = null;
        }
    }

    void OnInteractPerformed(InputAction.CallbackContext context)
    {
        Debug.Log("Interact action fired! Control used: " + context.control.name);
        currentTarget?.Interact();
    }
}