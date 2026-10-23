using UnityEngine;

/// <summary>
/// 切削岩石生成参数。挂在任意带 MeshFilter 的物体上（ProBuilder 白盒、基础体等），取其网格的凸包作为原石，
/// 用多组平面依次切削（每刀保留平面内侧，切口补成平面 facet），得到大块平整 facet 组成的低模岩石。
/// 棱边两侧 normalSmoothWidth 米内做法线平滑（几何上插支撑环划出平滑带，过渡曲线在着色器 RockGen/FacetRock 中计算），不用法线贴图。
/// 在 Inspector 中点“生成岩石”，由编辑器端 FacetRockBuilder 生成子物体 &lt;名字&gt;_FacetRock（普通 Mesh）。所有长度为世界尺寸（米）。
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(MeshFilter))]
public class FacetRockGenerator : MonoBehaviour
{
    [Tooltip("随机种子：相同种子与参数每次生成结果一致")]
    public int seed = 1;

    [Header("大刀（决定大轮廓）")]
    [Tooltip("大刀数量：方向在球面上均匀分布（Fibonacci 球面 + 随机旋转 + 抖动）")]
    [Range(0, 64)] public int bigCuts = 10;
    [Tooltip("大刀切深范围（米）：从该方向上原石最突出处往里切的深度")]
    public Vector2 bigDepth = new Vector2(0.5f, 2.5f);
    [Tooltip("大刀方向抖动（度）")]
    [Range(0f, 90f)] public float bigJitter = 25f;

    [Header("崩口（切掉突出的尖角）")]
    [Tooltip("崩口数量：每刀选一个尖锐的顶点（越尖越容易被选中），沿其平均法线加抖动的方向切掉它")]
    [Range(0, 128)] public int chipCuts = 20;
    [Tooltip("崩口切深范围（米）")]
    public Vector2 chipDepth = new Vector2(0.2f, 0.8f);
    [Tooltip("崩口方向抖动（度）")]
    [Range(0f, 90f)] public float chipJitter = 30f;

    [Header("质量约束（不满足的刀会重新随机，最多 8 次）")]
    [Tooltip("新产生的边最短长度（米）。同时保证法线平滑带有足够空间")]
    public float minEdge = 0.4f;
    [Tooltip("新切面与相邻面的最小转角（度）：太小时两面近乎共面，形成碎面")]
    [Range(0f, 45f)] public float minAngle = 12f;

    [Header("法线平滑")]
    [Tooltip("法线平滑宽度（米）：每条棱两侧这个距离内法线从 facet 法线过渡到棱的平分法线，带外 facet 保持平整。短边、窄面处自动收窄。0 = 全硬边")]
    public float normalSmoothWidth = 0.3f;

    [Header("外观")]
    [Tooltip("材质，为空时使用 Assets/Art/RockGen/Materials/M_RockGen_Facet（RockGen/FacetRock）")]
    public Material material;
    [Tooltip("facet 贴图 UV 一次平铺覆盖的世界尺寸（米），每个 facet 按自身平面投影")]
    public float uvTileSize = 4f;

    [Tooltip("实时预览：已生成过岩石时，修改参数后自动重建网格（不保存网格资源；点“生成岩石”保存）")]
    public bool livePreview = true;

    [Tooltip("生成后隐藏原网格的渲染")]
    public bool hideSource = true;

    [Tooltip("上次生成的岩石物体（自动填写）")]
    public GameObject output;
}
