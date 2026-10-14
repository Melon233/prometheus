# 风格化岩石插件：把白盒物体非破坏性地"挂载"成风格化岩石，参数修改后实时重新生成。
#
# 数据关系：
#   白盒.stylized_rock（StylizedRockSettings）保存全部参数与生成结果的引用 result；
#   岩石.stylized_rock_source 反指回白盒。岩石是白盒的子物体（父逆矩阵为单位阵），移动/绕 Z 旋转白盒时岩石直接跟随，无需重算。
# 更新时机：任一参数变化 → 立即重算；白盒网格被编辑后 → 手动点"重新生成"（避免编辑白盒时频繁重算）。
# 详细说明见仓库 Docs/Workflow/BlenderStylizedRock.md。
bl_info = {
    "name": "Stylized Rock",
    "author": "prometheus",
    "version": (1, 0, 0),
    "blender": (4, 2, 0),  # 用到 numpy、BVHTree 与 color_attributes 的 active/default 名称接口
    "location": "属性编辑器 > 物体 > 风格化岩石；3D 视图侧栏 > 岩石",
    "description": "白盒 → 风格化岩石，可挂载/卸载、实时调参",
    "category": "Object",
}

import bpy
from bpy.props import BoolProperty, FloatProperty, IntProperty, PointerProperty
from . import generator

OUT_COL = "StylizedRocks"  # 生成的岩石统一放进这个集合，方便整体显隐/导出


# ---------------- 生成 / 卸载 ----------------
def owned_rock(wb):
    """返回白盒当前挂载的岩石物体；引用为空，或岩石已不再指回本白盒（例如白盒是 Shift+D 复制出来的，引用沿用了原件的岩石）时返回 None。"""
    rock = wb.stylized_rock.result
    return rock if rock is not None and rock.stylized_rock_source == wb else None


def out_collection(scene):
    """取得（必要时创建并挂到场景下）岩石输出集合。"""
    col = bpy.data.collections.get(OUT_COL) or bpy.data.collections.new(OUT_COL)
    if col.name not in scene.collection.children: scene.collection.children.link(col)
    return col


def regenerate(wb, context):
    """按白盒当前参数重新生成岩石：已有岩石物体只替换网格与修改器（保留物体本身，选择/显隐状态不丢）；没有则新建。"""
    s = wb.stylized_rock
    context.view_layer.update()  # 刚移动/编辑过的白盒 matrix_world 要刷新后才是正确的世界变换
    bm, mods = generator.build(wb, s)
    me = bpy.data.meshes.new(wb.name + "_Rock")
    bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = True
    me.color_attributes.active_color_name = me.color_attributes.default_color_name = generator.TOP_MASK  # 视口顶点色显示与 FBX 导出都取这一层
    if s.material: me.materials.append(s.material)

    rock = owned_rock(wb)
    if rock is None:
        rock = bpy.data.objects.new(wb.name + "_Rock", me)
        rock.stylized_rock_source = wb
        s.result = rock
    else:
        old = rock.data; rock.data = me
        if old.users == 0: bpy.data.meshes.remove(old)
    if not rock.users_collection: out_collection(context.scene).objects.link(rock)  # 新建的，或被用户从场景里删掉后仍被引用着的岩石
    # 作为白盒子物体，父逆矩阵与自身变换都为单位阵：网格已是白盒局部坐标，世界位置与生成时一致
    rock.parent = wb; rock.matrix_parent_inverse.identity(); rock.matrix_basis.identity()

    # 修改器由插件全权管理：每次重建（用户手动加在岩石上的修改器会被清掉，需要叠加效果请在导出前另行处理）
    rock.modifiers.clear()
    bv = rock.modifiers.new("Bevel", 'BEVEL')
    bv.width = mods['bevel_width']; bv.segments = mods['bevel_segments']; bv.profile = 0.6
    bv.limit_method = 'ANGLE'; bv.angle_limit = 0.436332  # 25°：只倒切面之间的硬折角，三角化产生的近共面折线不倒
    bv.harden_normals = mods['harden']
    if mods['subsurf']: rock.modifiers.new("Subsurf", 'SUBSURF').levels = mods['subsurf']
    if mods['harden']: rock.modifiers.new("WeightedNormal", 'WEIGHTED_NORMAL').keep_sharp = True  # 让大切面着色平整


def unmount(wb):
    """卸载：删除岩石物体及其网格，恢复白盒的正常显示。"""
    rock = owned_rock(wb)
    if rock is not None:
        me = rock.data
        bpy.data.objects.remove(rock, do_unlink=True)
        if me.users == 0: bpy.data.meshes.remove(me)
    wb.stylized_rock.result = None
    wb.display_type = 'TEXTURED'; wb.hide_render = False; wb.hide_set(False)


# ---------------- 属性更新回调 ----------------
def on_enabled(self, context):
    """挂载开关：打开 → 生成岩石并隐藏白盒（白盒设为线框、不参与渲染，需要编辑时用"显示白盒"按钮叫出来）；关闭 → 卸载。"""
    wb = self.id_data
    if self.enabled:
        regenerate(wb, context)
        wb.display_type = 'WIRE'; wb.hide_render = True; wb.hide_set(True)
    else:
        unmount(wb)


def on_param(self, context):
    """任一生成参数变化：已挂载时立即重算。"""
    if self.enabled: regenerate(self.id_data, context)


class StylizedRockSettings(bpy.types.PropertyGroup):
    """挂在白盒物体上的岩石参数。数值参数中 0 表示"自动"（按白盒尺寸推算）。"""
    enabled: BoolProperty(name="挂载岩石", description="打开时为该白盒生成岩石，关闭时删除岩石并恢复白盒显示", default=False, update=on_enabled)
    seed: IntProperty(name="种子", description="随机种子：换一个就是另一块石头", default=0, min=0, update=on_param)
    material: PointerProperty(name="材质", type=bpy.types.Material, description="生成岩石使用的材质，留空则不赋材质", update=on_param)
    # 体量
    blend: FloatProperty(name="融合度", description="白盒台阶、侧挑块被抹平成连续岩体的程度：0 = 贴合白盒，1 = 高度方向强平滑（约 0.2×总高）", default=0.5, min=0.0, max=1.0, update=on_param)
    crown: FloatProperty(name="顶部起伏", description="顶部轮廓高低错落的幅度（米）：0 = 平顶（长草），越大岩峰越参差", default=0.0, min=0.0, soft_max=4.0, unit='LENGTH', update=on_param)
    # 切面
    facet: FloatProperty(name="切面尺寸", description="大切面的平均尺寸（米），决定网格疏密，0 = 自动", default=0.0, min=0.0, soft_max=10.0, unit='LENGTH', update=on_param)
    cut: FloatProperty(name="切面起伏", description="每个顶点随机向内收的最大比例：越大切面朝向越杂乱、折线越深", default=0.1, min=0.0, max=0.4, update=on_param)
    flatten: FloatProperty(name="平切强度", description="局部平切的刀数（相对网格顶点数）：越大大平面越多", default=0.6, min=0.0, soft_max=2.0, update=on_param)
    tilt: FloatProperty(name="平切倾斜", description="平切刀面相对局部表面的随机倾斜：越大平切面越偏离原表面方向", default=0.3, min=0.0, max=1.0, update=on_param)
    # 节理
    joints: IntProperty(name="竖向节理", description="竖向 V 形裂缝的条数", default=3, min=0, soft_max=12, update=on_param)
    joint_depth: FloatProperty(name="节理深度", description="竖向裂缝处向内收的比例", default=0.15, min=0.0, max=0.5, update=on_param)
    strata: FloatProperty(name="横向层理", description="按环整体内收的幅度：越大水平层理缝越明显", default=0.4, min=0.0, max=1.0, update=on_param)
    # 质感
    roundness: FloatProperty(name="圆度", description="0 = 硬朗悬崖（窄倒角、切面平整），>0.5 加细分磨圆成石块", default=0.2, min=0.0, max=1.0, update=on_param)
    bevel: FloatProperty(name="倒角宽度", description="棱边倒角宽度（米），0 = 按切面尺寸与圆度自动", default=0.0, min=0.0, soft_max=1.0, unit='LENGTH', update=on_param)
    # 顶部遮罩（写入 TopMask 顶点色 R 通道）
    top_start: FloatProperty(name="顶部起始", description="面法线 Z 超过该值开始计入顶部遮罩（0.75 ≈ 41° 坡）", default=0.75, min=-1.0, max=1.0, update=on_param)
    top_full: FloatProperty(name="顶部完全", description="面法线 Z 超过该值顶部遮罩为 1（0.9 ≈ 26° 坡）", default=0.9, min=-1.0, max=1.0, update=on_param)
    # 生成结果（内部使用，不在面板上编辑）
    result: PointerProperty(type=bpy.types.Object)


# ---------------- 操作符 ----------------
class STYLIZEDROCK_OT_regenerate(bpy.types.Operator):
    """白盒网格被编辑后手动刷新：重新生成选中白盒（或选中岩石对应的白盒）的岩石"""
    bl_idname = "stylized_rock.regenerate"
    bl_label = "重新生成"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        wbs = {source_of(ob) for ob in context.selected_objects}
        done = [wb for wb in wbs if wb is not None and wb.stylized_rock.enabled]
        for wb in done: regenerate(wb, context)
        self.report({'INFO'}, f"已重新生成 {len(done)} 块岩石")
        return {'FINISHED'}


class STYLIZEDROCK_OT_regenerate_all(bpy.types.Operator):
    """重新生成当前场景中所有已挂载的岩石"""
    bl_idname = "stylized_rock.regenerate_all"
    bl_label = "全部重新生成"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        done = [ob for ob in context.scene.objects if ob.type == 'MESH' and ob.stylized_rock.enabled]
        for wb in done: regenerate(wb, context)
        self.report({'INFO'}, f"已重新生成 {len(done)} 块岩石")
        return {'FINISHED'}


class STYLIZEDROCK_OT_toggle_whitebox(bpy.types.Operator):
    """显示/隐藏当前岩石对应的白盒（挂载后白盒默认隐藏；编辑白盒形状时叫出来，改完点"重新生成"）"""
    bl_idname = "stylized_rock.toggle_whitebox"
    bl_label = "显示/隐藏白盒"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        wb = source_of(context.object)
        wb.hide_set(not wb.hide_get())
        return {'FINISHED'}


# ---------------- 面板 ----------------
def source_of(ob):
    """面板/操作符的作用对象：选中的是岩石则返回它的白盒，选中的是普通网格则返回自身。"""
    if ob is None or ob.type != 'MESH': return None
    return ob.stylized_rock_source or ob


def draw_settings(layout, context):
    """属性编辑器与 3D 侧栏共用的面板内容。"""
    wb = source_of(context.object)
    if wb is None:
        layout.label(text="请选择一个网格白盒"); return
    s = wb.stylized_rock
    if wb is not context.object: layout.label(text=f"白盒：{wb.name}", icon='MESH_CUBE')
    layout.prop(s, "enabled")
    col = layout.column(); col.use_property_split = True; col.use_property_decorate = False
    col.active = s.enabled
    col.prop(s, "seed"); col.prop(s, "material")
    # 分组：体量 / 切面 / 节理 / 质感 / 顶部遮罩
    for title, names in (("体量", ("blend", "crown")), ("切面", ("facet", "cut", "flatten", "tilt")), ("节理", ("joints", "joint_depth", "strata")), ("质感", ("roundness", "bevel"))):
        col.separator(); col.label(text=title)
        for n in names: col.prop(s, n)
    col.separator(); col.label(text="顶部遮罩")
    col.prop(s, "top_start"); col.prop(s, "top_full")
    row = layout.row(align=True)
    row.operator(STYLIZEDROCK_OT_regenerate.bl_idname, icon='FILE_REFRESH')
    row.operator(STYLIZEDROCK_OT_regenerate_all.bl_idname)
    if s.enabled: layout.operator(STYLIZEDROCK_OT_toggle_whitebox.bl_idname, icon='HIDE_OFF' if wb.hide_get() else 'HIDE_ON')


class STYLIZEDROCK_PT_object(bpy.types.Panel):
    """属性编辑器 > 物体 标签页中的岩石面板（类似修改器的位置）。"""
    bl_label = "风格化岩石"
    bl_space_type = 'PROPERTIES'
    bl_region_type = 'WINDOW'
    bl_context = "object"

    def draw(self, context): draw_settings(self.layout, context)


class STYLIZEDROCK_PT_view3d(bpy.types.Panel):
    """3D 视图侧栏（N 面板）中的岩石面板，边看边调更方便。"""
    bl_label = "风格化岩石"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "岩石"

    def draw(self, context): draw_settings(self.layout, context)


classes = (StylizedRockSettings, STYLIZEDROCK_OT_regenerate, STYLIZEDROCK_OT_regenerate_all, STYLIZEDROCK_OT_toggle_whitebox, STYLIZEDROCK_PT_object, STYLIZEDROCK_PT_view3d)


def register():
    for c in classes: bpy.utils.register_class(c)
    bpy.types.Object.stylized_rock = PointerProperty(type=StylizedRockSettings)
    bpy.types.Object.stylized_rock_source = PointerProperty(type=bpy.types.Object, name="岩石来源白盒")


def unregister():
    del bpy.types.Object.stylized_rock_source
    del bpy.types.Object.stylized_rock
    for c in reversed(classes): bpy.utils.unregister_class(c)
