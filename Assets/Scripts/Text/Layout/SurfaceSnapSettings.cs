using UnityEngine;

/// <summary>
/// Shared settings for "paint the text onto whatever geometry is around it".
/// Lives inside SentenceBuilder and ResponseButtonGroup so both use the exact same rules.
///
/// Off by default: an untouched text object keeps the original flat-grid behaviour pixel for pixel.
/// </summary>
[System.Serializable]
public class SurfaceSnapSettings
{
    [Header("Sticking")]
    [Tooltip("Master switch. Off = the original flat grid, nothing is probed or moved.")]
    public bool stickToSurfaces = false;

    [Tooltip("How far in front of the text plane the probe starts, and how far past it it keeps looking. " +
             "Surfaces further away than this are ignored. Raise it if your text object sits far off the wall.")]
    [Range(0.01f, 5f)]
    public float probeDepth = 0.5f;

    [Tooltip("Which layers count as a surface. Everything by default.")]
    public LayerMask surfaceMask = ~0;

    [Tooltip("Pushes every letter this far off the surface along its normal so it can't z-fight with the wall.")]
    [Range(0f, 0.2f)]
    public float surfaceOffset = 0.01f;

    [Header("Corner turning")]
    [Tooltip("When a letter's straight probe finds nothing, rotate the probe to look for an adjacent face " +
             "(the next side of a cube, the floor under a wall) and continue the text there.")]
    public bool turnCorners = true;

    [Tooltip("How far the probe may rotate while hunting for the next face, in degrees. " +
             "90 covers cube edges and wall-to-floor; 135 also catches sharper folds.")]
    [Range(0f, 180f)]
    public float maxTurnAngle = 135f;

    [Tooltip("Granularity of the corner hunt, in degrees. Smaller = finer, but more raycasts per letter.")]
    [Range(5f, 90f)]
    public float turnStep = 15f;

    [Tooltip("Draw the probe line and every letter's surface normal in the Scene view while this object is selected.")]
    public bool drawGizmos = true;
}
