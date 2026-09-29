using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Slants a UI graphic horizontally into a parallelogram for the arcade look.
/// </summary>
[RequireComponent(typeof(Graphic))]
public class UISkew : BaseMeshEffect
{
    [SerializeField, Range(-30f, 30f)] private float angle = 12f;

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;

        float slant = Mathf.Tan(angle * Mathf.Deg2Rad);
        float centerY = graphic.rectTransform.rect.center.y;
        UIVertex vertex = default;

        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            vertex.position.x += (vertex.position.y - centerY) * slant;
            vh.SetUIVertex(vertex, i);
        }
    }
}
