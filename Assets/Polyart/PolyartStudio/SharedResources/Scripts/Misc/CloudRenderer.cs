using UnityEngine;

public class CloudRenderer : MonoBehaviour
{
    public Texture2D cloud1;
    public Texture2D cloud2;
    public ComputeShader cloudShader;
    public float cookieSize = 30;

    // Cloud transformation parameters
    public Vector2 cloud1Pan = Vector2.zero;
    public Vector2 cloud2Pan = Vector2.zero;
    public float cloud1Speed = 1.0f;
    public float cloud2Speed = 1.2f;

    [Range(0.1f, 3f)] public float cloud1Contrast = 1.0f;
    [Range(0.1f, 3f)] public float cloud2Contrast = 1.0f;

    [Range(0f, 1f)] public float cloud1Influence = 0.5f;
    [Range(0f, 1f)] public float cloud2Influence = 0.5f;

    // Tiling parameters
    [Range(0.1f, 10f)] public float cloud1Tiling = 1f;
    [Range(0.1f, 10f)] public float cloud2Tiling = 1f;

    // Offset parameters
    [Range(-1f, 1f)] public float cloud1OffsetX = 0f;
    [Range(-1f, 1f)] public float cloud1OffsetY = 0f;

    [Range(-1f, 1f)] public float cloud2OffsetX = 0f;
    [Range(-1f, 1f)] public float cloud2OffsetY = 0f;

    private Light directionalLight;
    private RenderTexture finalClouds;
    private int kernelHandle;

    private void CreateRendertexture(int width, int height)
    {
        if (finalClouds == null)
        {
            finalClouds = new RenderTexture(width, height, 0, RenderTextureFormat.Default)
            {
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                useMipMap = false, // Enable mipmaps for smooth transitions
                autoGenerateMips = false, // Disable auto mipmap generation
                depth = 0,
                depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None                
            };
            finalClouds.Create();
        }
    }
    void Start()
    {

        int finalWidth = Mathf.Max(cloud1.width, cloud2.width);
        int finalHeight = Mathf.Max(cloud1.height, cloud2.height);

        // Ensure RenderTexture is initialized
        if (finalClouds == null || finalClouds.width != finalWidth || finalClouds.height != finalHeight)
        {
            CreateRendertexture(finalWidth, finalHeight);
        }

        // Get the compute shader kernel
        kernelHandle = cloudShader.FindKernel("CSMain");

        // Set textures in the shader
        cloudShader.SetTexture(kernelHandle, "_Cloud1", cloud1);
        cloudShader.SetTexture(kernelHandle, "_Cloud2", cloud2);
        cloudShader.SetTexture(kernelHandle, "_FinalClouds", finalClouds);

        // Set texture sizes
        cloudShader.SetInts("_Cloud1Size", cloud1.width, cloud1.height);
        cloudShader.SetInts("_Cloud2Size", cloud2.width, cloud2.height);
        cloudShader.SetInts("_FinalSize", finalClouds.width, finalClouds.height);

        directionalLight = GetComponent<Light>();
        if (directionalLight != null)
        {
            directionalLight.cookie = finalClouds;
            directionalLight.cookieSize = cookieSize;
        }
    }

    private void OnValidate()
    {
        directionalLight = GetComponent<Light>();
        if (directionalLight != null)
        {
            directionalLight.cookieSize = cookieSize;
        }
    }

    void Update()
    {
        cloud1Pan += new Vector2(0.01f, 0.003f) * Time.deltaTime * cloud1Speed;
        cloud2Pan += new Vector2(0.013f, -0.001f) * Time.deltaTime * cloud2Speed;

        // Update shader parameters every frame
        cloudShader.SetFloats("_Cloud1Pan", cloud1Pan.x, cloud1Pan.y);
        cloudShader.SetFloats("_Cloud2Pan", cloud2Pan.x, cloud2Pan.y);
        cloudShader.SetFloat("_Cloud1Contrast", cloud1Contrast);
        cloudShader.SetFloat("_Cloud2Contrast", cloud2Contrast);
        cloudShader.SetFloat("_Cloud1Influence", cloud1Influence);
        cloudShader.SetFloat("_Cloud2Influence", cloud2Influence);
        // Pass tiling parameters
        cloudShader.SetFloat("_Cloud1Tiling", cloud1Tiling);
        cloudShader.SetFloat("_Cloud2Tiling", cloud2Tiling);
        // Pass offset parameters
        cloudShader.SetVector("_CloudOffsets", new Vector4(cloud1OffsetX, cloud1OffsetY, cloud2OffsetX, cloud2OffsetY));


        // Dispatch compute shader (Divide by 8 since numthreads is 8x8)
        cloudShader.Dispatch(kernelHandle, finalClouds.width / 8, finalClouds.height / 8, 1);
    }
}
