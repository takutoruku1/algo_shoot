import bpy
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
QA = ROOT / 'build/qa_story/title_depth/assets'
SOURCE = ROOT / 'assets/source/title'
OUTPUT = ROOT / 'assets/ui/title_background.glb'


def material(name, color, metal=0, rough=.5, emission=0):
    result = bpy.data.materials.new(name)
    shader = result.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Metallic'].default_value = metal
    shader.inputs['Roughness'].default_value = rough
    shader.inputs['Emission Color'].default_value = (*color, 1)
    shader.inputs['Emission Strength'].default_value = emission
    return result


def box(name, x, y, depth, width, height, thickness, bevel, surface, parent):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0))
    obj = bpy.context.object
    obj.name, obj.parent = name, parent
    obj.dimensions = (width, thickness, height)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.location = (x, -depth, y)
    obj.data.materials.append(surface)
    modifier = obj.modifiers.new('Soft machined edge', 'BEVEL')
    modifier.width, modifier.segments = bevel, 3
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    for face in obj.data.polygons:
        face.use_smooth = True
    modifier = obj.modifiers.new('Face normals', 'WEIGHTED_NORMAL')
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def line(name, points, radius, surface, parent):
    data = bpy.data.curves.new(name, 'CURVE')
    data.dimensions = '3D'
    data.bevel_depth, data.bevel_resolution = radius, 2
    spline = data.splines.new('POLY')
    spline.points.add(len(points) - 1)
    for target, (x, y, depth) in zip(spline.points, points):
        target.co = (x, -depth, y, 1)
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    data.materials.append(surface)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.convert(target='MESH')
    obj.select_set(False)
    return obj


def photo(parent, index, width, height, top):
    x0, x1, y0, y1 = -width / 2, width / 2, top - height, top
    data = bpy.data.meshes.new('Existing SNS photograph')
    data.from_pydata([(x0, -.057, y0), (x1, -.057, y0), (x1, -.057, y1), (x0, -.057, y1)], [], [(0, 1, 2, 3)])
    uv = data.uv_layers.new(name='UVMap')
    col, row = index % 2, index // 2
    # Inset each atlas tile to keep neighboring photographs out of mipmap borders.
    atlas_aspect = texture.image.size[0] * 3 / (texture.image.size[1] * 2)
    half_crop = min(1, (width / height) / atlas_aspect) * .25 - .003
    u0, u1 = col / 2 + .25 - half_crop, col / 2 + .25 + half_crop
    v0, v1 = 1 - (row + 1) / 3 + .003, 1 - row / 3 - .003
    coords = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
    for loop in data.loops:
        uv.data[loop.index].uv = coords[loop.vertex_index]
    obj = bpy.data.objects.new('PostPhoto', data)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    data.materials.append(photos)


def card(index, sx, sy, pixels, distance, yaw, roll, photo_index):
    name = f'TimelinePost{index:02d}'
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)
    root.location = ((sx - 640) * distance / FOCAL, distance - 12, (360 - sy) * distance / FOCAL)
    root.rotation_euler = (math.radians(-6 if index % 2 else 5), math.radians(roll), math.radians(yaw))
    width = pixels * distance / FOCAL
    height = width * (.92 if photo_index >= 0 else .52)
    edge = width * .024
    box('Silver edge', 0, 0, .018 - edge / 2, width, height, edge, .026, rim, root)
    box('Midnight glass', 0, 0, .022, width - .028, height - .028, .035, .024, body, root)
    pad = width * .075
    left, top = -width / 2 + pad, height / 2 - pad
    radius = width * .035
    ring = [(left + radius + math.cos(i * math.tau / 32) * radius,
             top - radius + math.sin(i * math.tau / 32) * radius, .061) for i in range(33)]
    line('Avatar', ring, width * .006, accent, root)
    for n, length in enumerate((.34, .22)):
        box('Profile line', left + width * .12 + length * width / 2, top - width * (.018 + n * .045),
            .054, length * width, width * .012, .009, .004, text_mat, root)
    for n in range(3):
        box('More', width / 2 - pad - n * width * .029, top - radius, .058,
            width * .008, width * .008, .009, .003, text_mat, root)
    if photo_index >= 0:
        photo(root, photo_index, width - pad * 2, width * .46, top - width * .14)
        bottom = top - width * .67
    else:
        bottom = top - width * .17
    for n, length in enumerate((.66, .47)):
        box('Caption', left + length * width / 2, bottom - n * width * .047, .057,
            length * width, width * .013, .008, .004, text_mat, root)
    heart = []
    for i in range(49):
        t = math.tau * i / 48
        heart.append((width * .33 + 16 * math.sin(t) ** 3 * width * .0016,
                      -height / 2 + pad + (13 * math.cos(t) - 5 * math.cos(2*t) - 2 * math.cos(3*t) - math.cos(4*t)) * width * .0016,
                      .064))
    line('Like', heart, width * .004, accent, root)
    box('Light on upper edge', 0, height / 2 - .022, .037, width * .87, .009, .008, .003, glint, root)
    bpy.ops.object.select_all(action='DESELECT')
    for child in root.children:
        child.select_set(True)
    bpy.context.view_layer.objects.active = root.children[0]
    bpy.ops.object.join()
    bpy.context.object.name = name + 'Surface'
    bpy.ops.object.select_all(action='DESELECT')
    return {'name': name, 'screen': [sx, sy], 'distance': distance, 'photo': photo_index}


bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
QA.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
(SOURCE / '.gdignore').touch()
FOCAL = 360 / math.tan(math.radians(40) / 2)
body = material('Midnight glass', (.025, .044, .065), .36, .27)
rim = material('Brushed blue silver', (.18, .31, .41), .72, .24)
text_mat = material('Quiet silver type', (.29, .39, .47), .1, .48, .15)
accent = material('Ice blue marks', (.22, .5, .64), .35, .32, .35)
glint = material('Edge light', (.12, .34, .44), .25, .3, .45)
photos = material('Existing story photographs', (.72, .78, .84), 0, .72)
shader = photos.node_tree.nodes.get('Principled BSDF')
texture = photos.node_tree.nodes.new('ShaderNodeTexImage')
texture.image = bpy.data.images.load(str(ROOT / 'assets/sns/story_posts_v2.png'))
photos.node_tree.links.new(texture.outputs['Color'], shader.inputs['Base Color'])
photos.node_tree.links.new(texture.outputs['Color'], shader.inputs['Emission Color'])
shader.inputs['Emission Strength'].default_value = .08

layout = [
    (-35, 190, 420, 5.8, -49, 6, 2), (1300, 640, 390, 6.8, 49, -12, 1),
    (245, -45, 260, 13, -34, 5, 0), (610, 42, 205, 15, 28, -9, 3),
    (1135, 120, 265, 11, -40, -8, 5), (370, 620, 235, 12.5, 39, -10, 4),
    (610, 535, 200, 15, -32, -8, 0), (120, 525, 215, 16, -37, 6, 1),
    (1250, 345, 210, 17, 35, -8, 2), (645, 300, 140, 20, 28, -10, -1),
    (300, 260, 160, 23, -28, -9, -1), (950, 690, 215, 19, -23, -9, 5),
    (100, 65, 95, 30, -15, -8, -1), (420, 105, 115, 28, 24, -8, 1),
    (710, 170, 105, 34, -25, -7, 5), (935, 40, 90, 38, 24, -8, -1),
    (1130, 410, 110, 32, -18, -10, 4), (510, 450, 95, 31, 24, -8, -1),
    (475, 670, 105, 30, -18, -9, 2), (195, 350, 105, 34, 18, -8, 3),
    (740, 635, 100, 36, 22, -7, -1), (1250, 20, 95, 35, -24, -10, 0),
    (520, 220, 67, 43, -21, -8, -1), (630, 415, 72, 41, 22, -10, 1),
    (80, 680, 80, 42, 20, -8, -1), (1025, 300, 78, 44, -20, -9, -1),
]
report = {'blender': bpy.app.version_string, 'source_art': 'assets/sns/story_posts_v2.png',
          'posts': [card(i, *args) for i, args in enumerate(layout)]}
bpy.context.view_layer.update()
bpy.ops.export_scene.gltf(filepath=str(OUTPUT), export_format='GLB', export_yup=True,
                          export_cameras=False, export_lights=False)
report['bytes'] = OUTPUT.stat().st_size
(QA / 'geometry.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples, scene.cycles.device = 24, 'CPU'
scene.cycles.use_denoising = True
scene.world.color = (.045, .065, .09)
scene.view_settings.view_transform = 'AgX'
scene.render.resolution_x, scene.render.resolution_y = 1280, 720
scene.render.resolution_percentage = 100
bpy.ops.object.light_add(type='AREA', location=(-4, -8, 8))
bpy.context.object.data.energy, bpy.context.object.data.size = 1100, 8
bpy.context.object.rotation_euler = (Vector((0, 6, 0)) - bpy.context.object.location).to_track_quat('-Z', 'Y').to_euler()
bpy.ops.object.camera_add(location=(0, -12, 0))
scene.camera = bpy.context.object
scene.camera.rotation_euler = (math.pi / 2, 0, 0)
scene.camera.data.type = 'PERSP'
scene.camera.data.lens = 36 * FOCAL / 1280
scene.camera.data.clip_end = 120
bpy.ops.file.pack_all()
bpy.context.preferences.filepaths.save_version = 0
scene.render.filepath = str(QA / 'background_preview.png')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'title_background.blend'))
bpy.ops.render.render(write_still=True)
print('TITLE_BACKGROUND_ASSET ' + json.dumps(report))
