悬崖岩块生成图（世界尺寸，已计入物体缩放；展开带坐标：x = 沿侧面周长，y = 侧面高度比例 0 底 → 1 顶；网格 UV0 与之对应）
T_RockGen_ID.png     单元随机色，白点为散点（锥顶）
T_RockGen_Joint.png  离单元边界的距离，越暗越靠近节理缝
T_RockGen_Height.png 网格实际外凸/内凹量，灰度映射 [-0.250, 1.358] m
T_RockGen_Height.exr 同上，32 位浮点，单位米
T_RockGen_Noise.png  调制凸出量与节理深度的噪声
参数：seed=1 spacing=4 verticalScale=0.35 verticalDensity=2.5 bulge=1.25 bulgeVariation=0.5 jointDepth=0.25 noiseScale=7.5
展开带：周长 45.170 m，平均高 19.990 m，散点 33
