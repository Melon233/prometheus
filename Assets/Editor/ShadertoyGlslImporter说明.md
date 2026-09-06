# Shadertoy GLSL 导入器

入口菜单为 `Prometheus/Shader/Shadertoy GLSL Importer`。第一版支持带 `mainImage(out vec4 fragColor, in vec2 fragCoord)` 的常见 Shadertoy GLSL（允许额外空格和换行），并将 `iResolution`、`iTime`、`iMouse` 转换为 Unity URP 参数，自动生成 Shader 与 Material。`iMouse` 当前默认为零向量。

当前明确不支持 Buffer、`iChannel0~3`、VR 和 Cubemap 入口。生成资源默认写入 `Assets/EditorRes/ShadertoyGenerated`，生成成功后会选中材质。

转换器会将 GLSL 的单值向量构造展开为 Unity HLSL 可接受的完整构造，例如 `vec3(0)` 转换为 `float3(0, 0, 0)`，`vec3(diffuse)` 转换为 `float3(diffuse, diffuse, diffuse)`。
