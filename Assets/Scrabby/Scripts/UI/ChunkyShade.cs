using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Light from above, on whatever the button already looks like.
//
// For buttons that keep their own sprite and colour - the main menu's tile
// and glass - rather than wearing the white or blue key art. It changes no
// picture: it tints the button's own mesh from lighter at the top to darker
// at the bottom, so the same sprite reads as rounded rather than flat.
//
// Worked out from each corner's height, so the shading is one smooth ramp
// however the sprite is sliced, and it follows the button's colour when a
// tab is chosen or put back.
[RequireComponent(typeof(Graphic))]
public class ChunkyShade : BaseMeshEffect
{
    // How much lighter the top is (towards white) and how much darker the
    // bottom (towards black).
    private const float Light = 0.30f;
    private const float Shade = 0.24f;

    private static readonly List<UIVertex> vertices = new List<UIVertex>();

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0)
            return;

        vertices.Clear();
        vh.GetUIVertexStream(vertices);

        float bottom = float.MaxValue;
        float top = float.MinValue;

        foreach (UIVertex v in vertices)
        {
            bottom = Mathf.Min(bottom, v.position.y);
            top = Mathf.Max(top, v.position.y);
        }

        float span = Mathf.Max(top - bottom, 0.001f);

        for (int i = 0; i < vertices.Count; i++)
        {
            UIVertex v = vertices[i];
            float t = (v.position.y - bottom) / span;     // 0 bottom, 1 top

            Color c = v.color;
            Color lit = Color.Lerp(c, Color.white, Light);
            Color dark = Color.Lerp(c, Color.black, Shade);
            Color shaded = Color.Lerp(dark, lit, Mathf.SmoothStep(0f, 1f, t));
            shaded.a = c.a;

            v.color = shaded;
            vertices[i] = v;
        }

        vh.Clear();
        vh.AddUIVertexTriangleStream(vertices);
    }
}
