import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/source/mina_archive'
OUTPUT = ROOT / 'assets/fx/mina_archive.glb'
QA = ROOT / 'build/qa_story/mina_archive/assets'
for directory in (SOURCE, OUTPUT.parent, QA):
    directory.mkdir(parents=True, exist_ok=True)
(SOURCE / '.gdignore').touch()
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)


def material(name, color, metal=0, rough=.3, emission=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Metallic'].default_value = metal
    bsdf.inputs['Roughness'].default_value = rough
    bsdf.inputs['Emission Color'].default_value = (*color, 1)
    bsdf.inputs['Emission Strength'].default_value = emission
    return mat


silver = material('Porcelain alloy', (.21, .35, .44), .65, .28)
glass = material('Stored voices - midnight glass', (.018, .052, .085), .4, .23)
light = material('Memory windows', (.11, .36, .48), .25, .26, .35)
gold = material('Unsent archive seams', (.8, .55, .23), .7, .23, .12)
parts = []


def box(name, at, size, mat, bevel=.035):
    bpy.ops.mesh.primitive_cube_add(size=1, location=at)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    if bevel:
        modifier = obj.modifiers.new('Machined edge', 'BEVEL')
        modifier.width, modifier.segments = bevel, 2
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    parts.append(obj)
    return obj


box('Archive glass volume', (0, 0, 1.45), (5.6, 2.6, 2.8), glass)
for z in (.12, 1.02, 1.92, 2.82):
    box('Cantilever floor plate', (0, 0, z), (6, 3, .1), silver)
    box('Illuminated floor edge', (0, -1.52, z), (5.9, .03, .025), light, .008)
for x in (-2.75, -.92, .92, 2.75):
    box('Load bearing mullion', (x, -1.36, 1.47), (.075, .12, 2.7), silver)
for row in range(3):
    for column in range(6):
        x, z = -2.35 + column * .94, .55 + row * .9
        box('Unsent window', (x, -1.315, z), (.72, .035, .68), light if (row + column) % 4 == 0 else glass, .015)
        box('Window memory line', (x + .27, -1.34, z), (.035, .025, .56), light, .006)
for x in (-2.82, 2.82):
    for row in range(3):
        for depth in (-.8, 0, .8):
            box('Side memory window', (x, depth, .55 + row * .9), (.035, .48, .4), light, .01)
box('Copper archive spine', (2.86, .85, 1.47), (.12, .2, 2.9), gold)
box('Archive roof slab', (0, 0, 2.88), (5.5, 2.8, .06), glass)

bpy.ops.object.select_all(action='DESELECT')
for part in parts:
    part.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
floor = bpy.context.object
floor.name = 'ArchiveFloor'
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
# Godot supplies a fixed-depth oblique projection, independent of each floor's width.
points = [v.co.copy() for v in floor.data.vertices]
lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
for vertex in floor.data.vertices:
    p = vertex.co.copy()
    vertex.co = ((p.x - lo.x) / (hi.x - lo.x), (p.y - lo.y) / (hi.y - lo.y), (p.z - lo.z) / (hi.z - lo.z))

bpy.ops.export_scene.gltf(filepath=str(OUTPUT), export_format='GLB', export_yup=True,
                          export_cameras=False, export_lights=False)
bpy.context.preferences.filepaths.save_version = 0
bpy.context.preferences.filepaths.file_preview_type = 'NONE'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'mina_archive.blend'))
report = {'blender': bpy.app.version_string, 'vertices': len(floor.data.vertices),
          'triangles': sum(len(p.vertices) - 2 for p in floor.data.polygons), 'bytes': OUTPUT.stat().st_size,
          'source_palette': 'char/bg2/route/blender/mina_mid.png',
          'godot_bounds': {'x': [0, 1], 'y': [0, 1], 'z': [-1, 0]}}
(QA / 'geometry.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('MINA_ARCHIVE ' + json.dumps(report))
