import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.dont_write_bytecode = True
from build_phone_ui import ROOT, group, join, material, mesh, point, prism, rgb, rounded, rounded_prism


source = ROOT / "assets/source/mina_call_button"
preview = ROOT / "build/qa_story/mina_call_button"
source.mkdir(parents=True, exist_ok=True)
preview.mkdir(parents=True, exist_ok=True)
(source / ".gdignore").touch()
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
root = group("MinaCallButton")

midnight = material("Midnight enamel", rgb("233358"), .22, .34)
silver = material("Brushed silver", rgb("b4c9e5"), .78, .24)
porcelain = material("Porcelain", rgb("e9e5f5"), .12, .36)
ink = material("Indigo ribbon", rgb("263960"), .24, .38)
silk = material("Periwinkle silk", rgb("6586c6"), .36, .33)
blue = material("Sapphire", rgb("268acb"), .35, .21)
ice = material("Ice facets", rgb("91e3ff"), .3, .19)
letters = material("Ivory lettering", rgb("fff8f0"), .05, .44)
for mat in (midnight, silver, porcelain, ink, silk, blue, ice, letters):
    mat.diffuse_color = mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value

rounded_prism("Metal case", 2.30, .51, .16, .035, -.071, silver, root, bevel=.010)
rounded_prism("Enamel face", 2.265, .475, .146, .059, .024, midnight, root, bevel=.009)
cx = -.805
rounded_prism("Porcelain inset", .439, .386, .09, .072, .050, porcelain, root, (cx, 0), .006)

for side, name in ((-1, "Left"), (1, "Right")):
    def ribbon_xy(x, y):
        return cx + side * x, y

    tail = [ribbon_xy(x, y) for x, y in ((.025, .007), (.099, -.01), (.145, -.144),
                                           (.096, -.125), (.067, -.159))]
    prism(name + " ribbon tail", tail, .096, .074, ink, root, .003)
    wing = [ribbon_xy(x, y) for x, y in ((.018, .009), (.148, .119), (.181, .095), (.171, -.079), (.146, -.089))]
    prism(name + " bow loop", wing, .106, .079, ink, root, .004)
    fold = [(*ribbon_xy(.019, .009), .110), (*ribbon_xy(.148, .112), .113),
            (*ribbon_xy(.169, .033), .139), (*ribbon_xy(.147, -.077), .112)]
    faces = [(0, 2, 1), (0, 3, 2)] if side > 0 else [(0, 1, 2), (0, 2, 3)]
    loop = mesh(name + " folded silk", fold, faces, silk, root)
    loop.data.materials.append(ink)
    loop.data.polygons[1].material_index = 1

def octagon(w, h, corner):
    return [(cx + x, y + .012) for x, y in ((-w+corner, -h), (w-corner, -h), (w, -h+corner), (w, h-corner),
                                              (w-corner, h), (-w+corner, h), (-w, h-corner), (-w, -h+corner))]

prism("Brooch setting", octagon(.065, .094, .015), .149, .101, silver, root, .003)
girdle = [(x, y, .151) for x, y in octagon(.055, .082, .013)]
table = [(x, y, .179) for x, y in octagon(.032, .052, .008)]
facets = [(i, (i+1) % 8, (i+1) % 8 + 8, i+8) for i in range(8)]
gem = mesh("Blue brooch facets", girdle + table, facets + [tuple(range(8, 16))], blue, root)
gem.data.materials.append(ice)
for i in (2, 3, 4):
    gem.data.polygons[i].material_index = 1

for i, h in enumerate((.071, .152, .101)):
    outline = rounded(.029, h, .012, (.845 + .052*i, .003), segments=4)
    prism("Voice bar " + str(i+1), outline, .081, .060, ice, root)

curve = bpy.data.curves.new("Call label", 'FONT')
curve.font = bpy.data.fonts.load(str(ROOT / "assets/fonts/ZenKakuGothicNew-Bold.ttf"))
curve.font.pack()
curve.body = "ミナを呼ぶ"
curve.align_x = 'CENTER'
curve.align_y = 'CENTER'
curve.resolution_u = 4
curve.extrude = .003
label = bpy.data.objects.new("Call label", curve)
bpy.context.collection.objects.link(label)
label.parent = root
label.rotation_euler.x = math.pi / 2
curve.materials.append(letters)
bpy.context.view_layer.update()
bounds = label.bound_box
left, right = min(p[0] for p in bounds), max(p[0] for p in bounds)
bottom, top = min(p[1] for p in bounds), max(p[1] for p in bounds)
size = min(1.15 / (right-left), .226 / (top-bottom))
label.scale = (size,) * 3
label.location = point(.075 - size*(left+right)/2, -size*(top+bottom)/2, .073)

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.world.color = (.20, .22, .27)
bpy.ops.object.camera_add(location=(.02, -5, .14))
camera = bpy.context.object
camera.rotation_euler = (-camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 2.48
scene.camera = camera
for loc, power, side_length in [((-2.2, -3, 3), 190, 3.2), ((2, -2, .5), 95, 2.3)]:
    bpy.ops.object.light_add(type='AREA', location=loc)
    light = bpy.context.object
    light.data.energy = power
    light.data.size = side_length
    light.rotation_euler = (-light.location).to_track_quat('-Z', 'Y').to_euler()
scene.render.film_transparent = True
scene.render.resolution_x, scene.render.resolution_y = 992, 272
scene.render.resolution_percentage = 100
scene.render.filepath = "//mina_call_button.png"
for area in bpy.context.screen.areas:
    if area.type == 'VIEW_3D':
        area.spaces.active.region_3d.view_perspective = 'CAMERA'
        area.spaces.active.shading.color_type = 'MATERIAL'
bpy.ops.object.select_all(action='DESELECT')
root.select_set(True)
bpy.context.view_layer.objects.active = root
bpy.context.preferences.filepaths.save_version = 0
bpy.context.preferences.filepaths.file_preview_type = 'NONE'
bpy.ops.wm.save_as_mainfile(filepath=str(source / "mina_call_button.blend"), relative_remap=False)
scene.render.filepath = str(preview / "blender.png")
bpy.ops.render.render(write_still=True)

# Godot draws the label at screen resolution; the editable Blender version retains its lettering.
join("Call button relief", [obj for obj in root.children if obj != label])
root.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(ROOT / "assets/ui/mina_call_button.glb"), export_format='GLB',
                          use_selection=True, export_yup=True, export_lights=False, export_cameras=False)
