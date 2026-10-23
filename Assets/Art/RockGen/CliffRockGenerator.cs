using UnityEngine;

/// <summary>
/// 悬崖岩块生成参数。挂在一个 ProBuilder 白盒块上（顶面一圈、底面一圈、中间一圈四边形侧面）。
/// 在 Inspector 中点“生成岩石”，由编辑器端 CliffRockBuilder 在其下生成带竖向节理的岩石网格，
/// 并把撒点、沃罗诺伊单元、节理、高度、噪声等图保存到 Assets/Art/RockGen/Generated/。
/// 生成的岩石只负责外观；白盒块本身保留碰撞，玩法尺寸不受岩石凹凸影响。
/// </summary>
[DisallowMultipleComponent]
public class CliffRockGenerator : MonoBehaviour
{
    [Tooltip("随机种子：相同种子与参数每次生成结果一致")]
    public int seed = 1;

    [Tooltip("撒点最小间距（世界尺寸，米；已计入物体缩放），决定节理岩板的宽度")]
    public float spacing = 0.8f;

    [Tooltip("纵向压缩系数：沃罗诺伊划分时高度乘以此值，只影响单元形状。越小单元边界越接近竖线，节理越竖直")]
    [Range(0.05f, 1f)] public float verticalScale = 0.35f;

    [Tooltip("纵向散点密度系数：撒点时纵向间距除以此值，只影响纵向排几层。1 = 纵向间距为 spacing / verticalScale；越大纵向层数越多，避免节理从顶通到底")]
    [Range(0.25f, 8f)] public float verticalDensity = 1f;

    [Tooltip("转角避让修改器：散点离侧面转角竖线的最小水平距离，单位为 spacing 的倍数。过近的散点会被推开，避免转角处切出极窄碎块而翻折。0 = 不避让")]
    [Range(0f, 0.5f)] public float cornerMargin = 0.25f;

    [Tooltip("散点处顶点沿外法线凸出的基准距离（世界尺寸，米）")]
    public float bulge = 0.25f;

    [Tooltip("凸出量随噪声变化的幅度（0 = 全部相同）")]
    [Range(0f, 1f)] public float bulgeVariation = 0.5f;

    [Tooltip("外凸限制修改器：锥顶凸出量不超过 系数 × 散点到单元边界、顶边、底边、转角竖线的最近距离。避免贴边散点的锥面被拉平或翻折。越大限制越松")]
    [Range(0.2f, 3f)] public float bulgeLimit = 1f;

    [Tooltip("水平外凸修改器：外凸方向中保留侧面法线的比例。0 = 纯水平外凸（顶点高度不变，斜面上的锥顶不会被抬过顶边）；1 = 沿侧面法线外凸")]
    [Range(0f, 1f)] public float normalKeep = 0f;

    [Tooltip("顶边倒角修改器：倒角系数。倒角面宽度（横截面里倒角斜边的长度）= 系数 × 平均侧面高度。0 = 不倒角。只作用于顶环，底环不受影响")]
    [Range(0f, 0.3f)] public float bevel = 0f;

    [Tooltip("顶边倒角修改器：倒角面相对顶面的倾角（度）。小于 45° 更平缓，像风化磨圆的顶边；大于 45° 更陡。外倾、悬挑的侧壁也保持同样的坡度")]
    [Range(5f, 85f)] public float bevelAngle = 45f;

    /// <summary>法线平滑方式。</summary>
    public enum NormalMode
    {
        [Tooltip("平直着色：每个三角形用自己的面法线")] Flat,
        [Tooltip("全平滑：位置重合的顶点共享相邻面的平均法线")] Smooth,
        [Tooltip("岩板分区平滑：岩板内部平滑，节理缝、转角、顶面、倒角之间硬边")] Plates,
    }

    [Tooltip("法线平滑修改器：Flat = 平直着色（每个三角形一块均匀明暗）；Smooth = 全平滑（所有相邻面之间柔和过渡）；Plates = 岩板分区平滑（一块岩板一个大明暗）")]
    public NormalMode normalMode = NormalMode.Smooth;

    [Tooltip("顶点色烘焙：曲率增益。R 通道 = 0.5 + 曲率 × 增益，越大棱边亮线、凹缝暗部越明显")]
    [Range(0.2f, 8f)] public float curvatureGain = 1.2f;

    [Tooltip("顶面草地过渡宽度（米）：草地只在顶面内，从顶面外缘向内这段距离里由岩石线性过渡到草地，再往内全是草。0 = 整个顶面都是草、外缘直接切换")]
    public float grassBlend = 1f;

    [Tooltip("顶边扰动修改器：岩板顶部外凸量，单位为该岩板的凸出量。0 = 不外凸。让顶边轮廓顺应侧面节理，俯视呈锯齿状")]
    [Range(0f, 1.5f)] public float topJag = 0.6f;

    [Tooltip("顶边扰动修改器：节理与顶边交点处向内凹的深度（米）。0 = 不内凹。注意白盒碰撞不随之内凹")]
    public float topNotch = 0.15f;

    [Tooltip("顶边扰动修改器：峰点两侧各多插一点，轮廓更圆润")]
    public bool topJagRound = false;

    [Tooltip("顶边锐角剪平：俯视顶边轮廓上外凸且夹角小于此值（度）的尖点被剪平（切角：删去尖端，两条边上各取一点以直线相连），包括顶边扰动的峰点与白盒顶环的凸转角。0 = 不剪平。顶边扰动有坡度限制，峰点夹角不小于 90°")]
    [Range(0f, 179f)] public float topSharpAngle = 150f;

    [Tooltip("顶边锐角剪平：剪平强度。越大切点离尖端越远，剪掉的越多。0 = 不剪平")]
    [Range(0f, 1f)] public float topSmoothStrength = 0.5f;

    [Tooltip("顶边点最小间距（米，按展开带弧长）：顶环转角、节理与顶边的交点、顶边扰动的峰点 / 圆润点相互间距都不小于此值，过近的节理交点会合并。0 = 不限制")]
    public float topMinGap = 0.5f;

    [Tooltip("单元边界（节理缝）顶点向内凹陷的最大距离（世界尺寸，米），顶边和底边不凹陷")]
    public float jointDepth = 0.05f;

    [Tooltip("噪声的特征尺度（世界尺寸，米）")]
    public float noiseScale = 1.5f;

    /// <summary>贴图方式。</summary>
    public enum TextureMode
    {
        [Tooltip("调试纹理：侧面显示沃罗诺伊 ID 图，其余灰色")] Debug,
        [Tooltip("草地岩石：顶面草地，其余岩石")] GrassRock,
        [Tooltip("原神风格：RockGen/GenshinCliff 风格化着色器，读顶点色，草地只在顶面内（A 通道）")] Genshin,
    }

    [Tooltip("贴图修改器：Debug = 调试纹理（侧面 ID 图，UV 与保存的图对应）；GrassRock = 顶面草地、侧面 / 倒角 / 底面岩石，按世界尺寸平铺；Genshin = 原神风格着色器")]
    public TextureMode textureMode = TextureMode.Genshin;

    [Tooltip("原神风格材质（Genshin 模式）。为空时使用 Assets/Art/RockGen/Materials/M_RockGen_Genshin（蒙德悬崖调参）")]
    public Material genshinMaterial;

    [Tooltip("顶面草地材质（GrassRock 模式）。为空时使用 Assets/Art/RockGen/Materials/M_RockGen_Grass")]
    public Material grassMaterial;

    [Tooltip("侧面 / 倒角 / 底面岩石材质（GrassRock 模式）。为空时使用 Assets/Art/RockGen/Materials/M_RockGen_Rock")]
    public Material rockMaterial;

    [Tooltip("草地贴图一次平铺覆盖的世界尺寸（米）")]
    public float grassTileSize = 4f;

    [Tooltip("岩石贴图一次平铺覆盖的世界尺寸（米）")]
    public float rockTileSize = 8f;

    [Tooltip("顶面撒草修改器：在顶面撒草叶（岩石的子物体 <名字>_Grass）")]
    public bool grassScatter = true;

    [Tooltip("顶面撒草：每平方米草叶数（只撒在纯草地上，即草地过渡带以内）")]
    public float grassDensity = 30f;

    [Tooltip("顶面撒草：草叶基准高度（米），实际为 0.7～1.3 倍")]
    public float grassHeight = 0.45f;

    [Tooltip("顶面撒草：草叶材质。为空时使用 Assets/Art/GenshinTree/Materials/M_GT_Grass（Stylized/GenshinToon，双面、带风动）")]
    public Material grassBladeMaterial;

    [Tooltip("保存的图宽度（像素），高度按侧面展开带的长宽比计算")]
    public int mapWidth = 1024;

    [Tooltip("实时预览：已生成过岩石时，修改参数后自动重建网格（不重新出图，保存的图与调试材质沿用上次；点“生成岩石”完整更新）")]
    public bool livePreview = true;

    [Tooltip("生成后隐藏白盒块的渲染（碰撞保留）")]
    public bool hideSource = true;

    [Tooltip("上次生成的岩石物体（自动填写）")]
    public GameObject output;

    [Tooltip("上次生成时保存图与材质的目录（自动填写）")]
    public string outputFolder;
}
