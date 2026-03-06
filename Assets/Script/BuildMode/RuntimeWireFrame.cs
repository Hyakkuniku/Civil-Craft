using UnityEngine;
using UnityEngine.Rendering; // NEW: Required to talk to URP

[RequireComponent(typeof(MeshFilter))]
public class RuntimeWireframe : MonoBehaviour
{
    public Color lineColor = Color.blue;
    [Range(0f, 1f)] public float transitionAlpha = 1f;

    private Material lineMaterial;
    private Mesh mesh;

    void Start()
    {
        mesh = GetComponent<MeshFilter>().sharedMesh;

        // Sprites/Default is safe from build-stripping and works perfectly in URP
        Shader shader = Shader.Find("Sprites/Default");
        
        // Fallback to URP Unlit just in case
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

        lineMaterial = new Material(shader);
        lineMaterial.hideFlags = HideFlags.HideAndDontSave;

        // Set up alpha blending
        lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        lineMaterial.SetInt("_ZWrite", 0);
    }

    // NEW: Subscribe to URP's rendering event when enabled
    void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += DrawWireframe;
    }

    // NEW: Unsubscribe when disabled to prevent memory leaks!
    void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= DrawWireframe;
    }

    // NEW: URP's modern replacement for OnRenderObject()
    void DrawWireframe(ScriptableRenderContext context, Camera cam)
    {
        if (!lineMaterial || !mesh || transitionAlpha <= 0f) return;
        if (cam != Camera.main && cam.cameraType != CameraType.SceneView) return;

        lineMaterial.SetPass(0);
        
        Color currentColor = lineColor;
        currentColor.a *= transitionAlpha; 

        // Apply to material just in case the shader relies on it
        if (lineMaterial.HasProperty("_BaseColor"))
            lineMaterial.SetColor("_BaseColor", currentColor);
        else
            lineMaterial.SetColor("_Color", currentColor);

        GL.PushMatrix();
        GL.MultMatrix(transform.localToWorldMatrix);
        GL.Begin(GL.LINES);
        
        // FIX: Explicitly tell the GL pipeline the exact color and alpha of the lines!
        GL.Color(currentColor); 

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