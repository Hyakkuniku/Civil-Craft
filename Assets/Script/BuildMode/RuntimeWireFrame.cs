using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class RuntimeWireframe : MonoBehaviour
{
    public Color lineColor = Color.blue;

    private Material lineMaterial;
    private Mesh mesh;

    void Start()
    {
        mesh = GetComponent<MeshFilter>().sharedMesh;

        Shader shader = Shader.Find("Hidden/Internal-Colored");
        lineMaterial = new Material(shader);
        lineMaterial.hideFlags = HideFlags.HideAndDontSave;

        lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        lineMaterial.SetInt("_ZWrite", 0);
    }

    void OnRenderObject()
    {
        if (!lineMaterial || !mesh) return;

        lineMaterial.SetPass(0);
        lineMaterial.SetColor("_Color", lineColor);

        GL.PushMatrix();
        GL.MultMatrix(transform.localToWorldMatrix);
        GL.Begin(GL.LINES);

        int[] triangles = mesh.triangles;
        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = vertices[triangles[i]];
            Vector3 v1 = vertices[triangles[i + 1]];
            Vector3 v2 = vertices[triangles[i + 2]];

            GL.Vertex(v0);
            GL.Vertex(v1);

            GL.Vertex(v1);
            GL.Vertex(v2);

            GL.Vertex(v2);
            GL.Vertex(v0);
        }

        GL.End();
        GL.PopMatrix();
    }
}
