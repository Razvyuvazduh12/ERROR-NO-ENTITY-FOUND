using System.Collections.Generic;
using UnityEngine;

public class ResponseButton : MonoBehaviour
{
    public string OptionText { get; private set; }
    private Color normalTint;
    private Color hoverTint;
    private List<SpriteRenderer> renderers;
    private bool isHovered;

    
    public void Setup(ResponseOption option)
    {
        OptionText = option.text;
        normalTint = option.tint;
        hoverTint = option.hoverTint;
    }

    public void SetRenderers(List<SpriteRenderer> rends)
    {
        renderers = rends;
    }

    public void SetHovered(bool hovered)
    {
        if (hovered == isHovered) return; // skip repainting every frame if nothing changed
        isHovered = hovered;

        Color target = hovered ? hoverTint : normalTint;
        foreach (SpriteRenderer sr in renderers)
        {
            if (sr != null) sr.color = target;
        }
    }
}