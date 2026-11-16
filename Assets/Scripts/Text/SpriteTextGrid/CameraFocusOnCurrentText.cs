using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFocusOnCurrentText : MonoBehaviour
{
    [SerializeField] private FirstPersonController playerController; // owns yaw (player body) and pitch (verticalRotation)
    [SerializeField] private float rotateSpeed = 5f; // higher = snappier turn

    private Coroutine focusRoutine;

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            FocusOnCurrentText();
        }
    }

    void FocusOnCurrentText()
    {
        SentenceBuilder current = SentenceBuilder.CurrentActive;
        if (current == null)
        {
            Debug.Log("No FONT object has been revealed yet — nothing to focus on.");
            return;
        }

        if (playerController == null || playerController.cam == null)
        {
            Debug.LogError("CameraFocusOnCurrentText: assign Player Controller (with its Cam set) in the Inspector.");
            return;
        }

        if (focusRoutine != null) StopCoroutine(focusRoutine);
        focusRoutine = StartCoroutine(SmoothLookAt(current.transform.position));
    }

    // Drives the SAME yaw/pitch state FirstPersonController.HandleMouseLook() reads from every
    // frame, instead of rotating the camera Transform directly — that's what was causing the
    // snap-back, since HandleMouseLook() unconditionally re-applies its own stored pitch each
    // frame regardless of what anything else just set the camera to.
    IEnumerator SmoothLookAt(Vector3 targetPosition)
    {
        Transform cam = playerController.cam;
        Vector3 direction = targetPosition - cam.position;
        if (direction.sqrMagnitude < 0.0001f) yield break; // already basically on top of the target

        Quaternion targetWorldRot = Quaternion.LookRotation(direction);
        Vector3 targetEuler = targetWorldRot.eulerAngles;

        float targetYaw = targetEuler.y;
        float targetPitch = targetEuler.x > 180f ? targetEuler.x - 360f : targetEuler.x; // -180..180 range so clamping/lerp behaves
        targetPitch = Mathf.Clamp(targetPitch, -playerController.maxLookAngle, playerController.maxLookAngle);

        float startYaw = playerController.transform.eulerAngles.y;
        float startPitch = playerController.CurrentPitch;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * rotateSpeed;
            float yaw = Mathf.LerpAngle(startYaw, targetYaw, t);
            float pitch = Mathf.LerpAngle(startPitch, targetPitch, t);
            playerController.SetLookRotation(yaw, pitch);
            yield return null;
        }

        playerController.SetLookRotation(targetYaw, targetPitch);
    }
}