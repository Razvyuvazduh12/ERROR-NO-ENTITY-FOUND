using System.Collections.Generic;
using UnityEngine;

public class ResponseButton : MonoBehaviour
{
    public string OptionText { get; private set; }
    public bool Locked { get; private set; } // an answer that was already used in this branch loop

    private Color normalTint;
    private Color hoverTint;
    private List<SpriteRenderer> renderers;
    private bool isHovered;

    // used = this answer has already been picked once, so it is drawn in its dead colour and
    // the group refuses to treat it as hovered/clickable.
    public void Setup(ResponseOption option, bool used = false)
    {
        OptionText = option.text;
        Locked = used;
        normalTint = used ? option.usedTint : option.tint;
        hoverTint = used ? option.usedTint : option.hoverTint;
    }

    public void SetRenderers(List<SpriteRenderer> rends)
    {
        renderers = rends;
    }

    public void SetHovered(bool hovered)
    {
        if (Locked) hovered = false;      // a used answer never lights up
        if (hovered == isHovered) return; // skip repainting every frame if nothing changed
        isHovered = hovered;

        Color target = hovered ? hoverTint : normalTint;
        foreach (SpriteRenderer sr in renderers)
        {
            if (sr != null) sr.color = target;
        }
    }
}
