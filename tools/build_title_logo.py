import bpy
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/source/title'
OUTPUT = ROOT / 'assets/ui/title_logo.glb'
QA = ROOT / 'build/qa_story/title_typography/logo'


def linear(code):
    values = [int(code[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v < .04045 else ((v + .055) / 1.055) ** 2.4 for v in values)


def material(name, color, metal, roughness):
    surface = bpy.data.materials.new(name)
    shader = surface.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*linear(color), 1)
    shader.inputs['Metallic'].default_value = metal
    shader.inputs['Roughness'].default_value = roughness
    return surface


bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
SOURCE.mkdir(parents=True, exist_ok=True)
QA.mkdir(parents=True, exist_ok=True)
(SOURCE / '.gdignore').touch()
edge = material('Cool silver bevel', '8ea7c0', .72, .24)
face = material('Pearlescent face', 'ffffff', .32, .3)
data = bpy.data.curves.new('Original Refrain italic lettering', 'FONT')
data.body = 'Refrain'
data.font = bpy.data.fonts.load(str(ROOT / 'assets/fonts/CormorantGaramond-Italic.ttf'))
data.extrude, data.bevel_depth, data.bevel_resolution = .0075, .003, 3
data.resolution_u = 12
wordmark = bpy.data.objects.new('Refrain', data)
bpy.context.collection.objects.link(wordmark)
wordmark.rotation_euler.x = math.pi / 2
data.materials.append(edge)
wordmark.select_set(True)
bpy.context.view_layer.objects.active = wordmark
bpy.ops.object.convert(target='MESH')
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
minimum = Vector(tuple(min(v.co[i] for v in wordmark.data.vertices) for i in range(3)))
maximum = Vector(tuple(max(v.co[i] for v in wordmark.data.vertices) for i in range(3)))
factor = 10 / (maximum.x - minimum.x)
center = (minimum + maximum) / 2
for vertex in wordmark.data.vertices:
    vertex.co = (vertex.co - center) * factor
wordmark.data.materials.append(face)
wordmark.data.update()
for polygon in wordmark.data.polygons:
    polygon.material_index = 1 if polygon.normal.y < -.995 else 0
    polygon.use_smooth = abs(polygon.normal.y) < .995
stops = [(0, linear('6f8fd5')), (.28, linear('b5a6e3')), (.5, linear('f1d4e1')),
         (.7, linear('fbf5ee')), (1, linear('9bd8f0'))]
colors = wordmark.data.color_attributes.new(name='PearlTint', type='FLOAT_COLOR', domain='CORNER')
height = (maximum.z - minimum.z) * factor
for loop in wordmark.data.loops:
    t = max(0, min(1, wordmark.data.vertices[loop.vertex_index].co.z / height + .5))
    for (a, ca), (b, cb) in zip(stops, stops[1:]):
        if a <= t <= b:
            weight = (t - a) / (b - a)
            colors.data[loop.index].color = (*[x + (y - x) * weight for x, y in zip(ca, cb)], 1)
            break
attribute = face.node_tree.nodes.new('ShaderNodeVertexColor')
attribute.layer_name = colors.name
face.node_tree.links.new(attribute.outputs['Color'], face.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
bpy.context.view_layer.update()
bpy.ops.export_scene.gltf(filepath=str(OUTPUT), export_format='GLB', export_yup=True,
                          export_cameras=False, export_lights=False)
report = {'font': 'assets/fonts/CormorantGaramond-Italic.ttf', 'text': 'Refrain',
          'width': 10, 'height': height, 'depth': (maximum.y - minimum.y) * factor,
          'vertices': len(wordmark.data.vertices), 'bytes': OUTPUT.stat().st_size}
(QA / 'geometry.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples, scene.cycles.device = 32, 'CPU'
scene.cycles.use_denoising = True
scene.world.color = (.13, .14, .18)
scene.view_settings.view_transform = 'AgX'
scene.render.resolution_x, scene.render.resolution_y = 1280, 540
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
for name, location, energy, color, size in [
    ('Pearl softbox', (-3, -5, 6), 1050, (.85, .94, 1), 5),
    ('Lilac fill', (5, -4, 0), 550, (1, .8, .95), 5),
    ('Silver rim', (0, 1, 5), 900, (.7, .9, 1), 3),
]:
    bpy.ops.object.light_add(type='AREA', location=location)
    light = bpy.context.object
    light.name, light.data.energy, light.data.color, light.data.size = name, energy, color, size
    light.rotation_euler = (-light.location).to_track_quat('-Z', 'Y').to_euler()
bpy.ops.object.camera_add(location=(0, -22, 0))
scene.camera = bpy.context.object
scene.camera.rotation_euler = (math.pi / 2, 0, 0)
scene.camera.data.type, scene.camera.data.ortho_scale = 'ORTHO', 11.4
bpy.ops.file.pack_all()
bpy.context.preferences.filepaths.save_version = 0
scene.render.filepath = str(QA / 'blender_logo.png')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'title_logo.blend'))
bpy.ops.render.render(write_still=True)
print('TITLE_LOGO ' + json.dumps(report))
