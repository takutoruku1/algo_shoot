import bpy
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/source/title/mina_25d'
OUTPUT = ROOT / 'assets/ui/title_mina_25d.glb'
QA = ROOT / 'build/qa_story/title_mina_25d'
WIDTH, HEIGHT = 1672, 941


def smooth(a, b, value):
    t = max(0, min(1, (value - a) / (b - a)))
    return t * t * (3 - 2 * t)


def bell(x, y, cx, cy, rx, ry):
    return math.exp(-((x - cx) / rx) ** 2 - ((y - cy) / ry) ** 2)


def deformation(x, y, shape):
    rise = 1 - smooth(640, 920, y)
    if shape == 'Breath':
        chest = bell(x, y, 1210, 510, 200, 190) * smooth(350, 440, y)
        return ((x - 1210) * .003 * chest, -2.5 * rise, 1.2 * chest)
    if shape == 'HeadSway':
        weight = 1 - smooth(320, 450, y)
        return (-(y - 365) * .005 * weight, (x - 1140) * .005 * weight, 0)
    if shape.startswith('HairUpper'):
        weight = smooth(1240, 1610, x) * smooth(190, 300, y) * (1 - smooth(490, 620, y))
        weight *= smooth(.9, 1.25, math.hypot((x - 1290) / 135, (y - 490) / 100))
        phase = (x - 1240) / 215 + y / 370
        wave = math.sin(phase) if shape.endswith('Sin') else math.cos(phase)
        return (2.3 * wave * weight, 10 * wave * weight, 2.2 * wave * weight)
    if shape.startswith('HairLower'):
        weight = smooth(1450, 1660, x) * smooth(440, 575, y) * (1 - smooth(750, 850, y))
        phase = (x - 1340) / 185 + y / 410
        wave = math.sin(phase) if shape.endswith('Sin') else math.cos(phase)
        return (3.5 * wave * weight, 12 * wave * weight, 3 * wave * weight)
    if shape.startswith('HairFront'):
        front_distance = abs(x - (1048 + (y - 280) * .24)) / 29
        side_distance = abs(x - 902) / 32
        front = math.exp(-front_distance ** 2) * (1 - smooth(1.5, 2, front_distance))
        side = math.exp(-side_distance ** 2) * (1 - smooth(1.5, 2, side_distance)) * (1 - smooth(505, 550, y))
        weight = max(front, side) * smooth(345, 455, y) * (1 - smooth(650, 720, y))
        phase = y / 180
        wave = math.sin(phase) if shape.endswith('Sin') else math.cos(phase)
        return (2.6 * wave * weight, .8 * wave * weight, wave * weight)
    if shape == 'Ribbon':
        weight = smooth(1370, 1630, x) * smooth(690, 735, y) * (1 - smooth(815, 870, y))
        return (1.3 * weight, -5 * weight, 3 * weight)
    raise ValueError(shape)


def part_at(x, y):
    if y < 350 and x < 1300:
        return 'Head'
    if x > 1370 and 700 < y < 870:
        return 'Ribbon'
    if x > 1320 and y < 760 or x < 950 and 210 < y < 535:
        return 'BackHair'
    if 1000 < x < 1195 and 315 < y < 690:
        return 'FrontHair'
    return 'Body'


bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
SOURCE.mkdir(parents=True, exist_ok=True)
QA.mkdir(parents=True, exist_ok=True)
(SOURCE / '.gdignore').touch()
original = bpy.data.images.load(str(ROOT / 'char/bg2/title/title_clean_v3.png'))
cutout = bpy.data.images.load(str(ROOT / 'char/bg2/title/title_mina_cutout_v1.png'))
assert tuple(original.size) == tuple(cutout.size) == (WIDTH, HEIGHT)
surface = bpy.data.materials.new('Mina original illustration with cutout alpha')
shader = surface.node_tree.nodes.get('Principled BSDF')
art = surface.node_tree.nodes.new('ShaderNodeTexImage')
art.image = original
mask = surface.node_tree.nodes.new('ShaderNodeTexImage')
mask.image = cutout
surface.node_tree.links.new(art.outputs['Color'], shader.inputs['Base Color'])
surface.node_tree.links.new(art.outputs['Color'], shader.inputs['Emission Color'])
surface.node_tree.links.new(mask.outputs['Alpha'], shader.inputs['Alpha'])
shader.inputs['Emission Strength'].default_value = 1
shader.inputs['Roughness'].default_value = 1
root = bpy.data.objects.new('Mina25D', None)
bpy.context.collection.objects.link(root)
xs = list(range(720, WIDTH, 12)) + [WIDTH]
ys = list(range(0, HEIGHT, 12)) + [HEIGHT]
parts = {name: [] for name in ('Head', 'Body', 'BackHair', 'FrontHair', 'Ribbon')}
for row in range(len(ys) - 1):
    for column in range(len(xs) - 1):
        x0, x1 = xs[column:column + 2]
        y0, y1 = ys[row:row + 2]
        parts[part_at((x0 + x1) / 2, (y0 + y1) / 2)].append(((x0, y0), (x1, y0), (x1, y1), (x0, y1)))
shapes = ['Breath', 'HeadSway', 'HairUpperSin', 'HairUpperCos', 'HairLowerSin', 'HairLowerCos',
          'HairFrontSin', 'HairFrontCos', 'Ribbon']
for name, quads in parts.items():
    pixels, lookup, faces = [], {}, []
    for quad in quads:
        face = []
        for point in reversed(quad):
            if point not in lookup:
                lookup[point] = len(pixels)
                pixels.append(point)
            face.append(lookup[point])
        faces.append(face)
    vertices = []
    for x, y in pixels:
        depth = 6 + 12 * bell(x, y, 1130, 220, 160, 190) + 5 * bell(x, y, 1210, 560, 190, 220)
        vertices.append((x - WIDTH / 2, -depth, HEIGHT / 2 - y))
    data = bpy.data.meshes.new(name + 'DeformationMesh')
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.parent = root
    data.materials.append(surface)
    uv = data.uv_layers.new(name='Original pixels')
    for loop in data.loops:
        x, y = pixels[loop.vertex_index]
        uv.data[loop.index].uv = (x / WIDTH, 1 - y / HEIGHT)
    obj.shape_key_add(name='Basis')
    for name in shapes:
        shape = obj.shape_key_add(name=name)
        shape.slider_min = -1
        for i, (x, y) in enumerate(pixels):
            dx, dy, dz = deformation(x, y, name)
            shape.data[i].co = Vector(vertices[i]) + Vector((dx, -dz, -dy))
    assert not data.validate()

bpy.context.view_layer.update()
# Godot binds the original RGB and generated alpha separately, without baking a replacement illustration.
image_links = [(link.from_socket, link.to_socket) for link in surface.node_tree.links]
surface.node_tree.links.clear()
bpy.ops.export_scene.gltf(filepath=str(OUTPUT), export_format='GLB', export_yup=True,
                          export_cameras=False, export_lights=False, export_morph=True,
                          export_morph_normal=False, export_morph_tangent=False)
for source, target in image_links:
    surface.node_tree.links.new(source, target)
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples, scene.cycles.device = 24, 'CPU'
scene.cycles.use_denoising = True
scene.world.color = (0, 0, 0)
scene.view_settings.view_transform = 'Standard'
scene.render.resolution_x, scene.render.resolution_y = WIDTH, HEIGHT
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
bpy.ops.object.camera_add(location=(0, -1600, 0))
scene.camera = bpy.context.object
scene.camera.rotation_euler = (math.pi / 2, 0, 0)
scene.camera.data.type, scene.camera.data.ortho_scale = 'ORTHO', WIDTH
scene.camera.data.clip_end = 2500
bpy.ops.file.pack_all()
bpy.context.preferences.filepaths.save_version = 0
scene.render.filepath = str(QA / 'blender_mina.png')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'mina_25d.blend'))
bpy.ops.render.render(write_still=True)
report = {'blender': bpy.app.version_string, 'parts': list(parts), 'shape_keys': shapes,
          'vertices': sum(len(obj.data.vertices) for obj in root.children),
          'source_art': original.filepath, 'bytes': OUTPUT.stat().st_size}
(QA / 'geometry.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('MINA_25D ' + json.dumps(report))
