import bpy
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'assets/ui/title_ui.glb'
SOURCE = ROOT / 'assets/source/title'
QA = ROOT / 'build/qa_story/title_light/assets'


def mesh(name, pixels, center, depth, faces, surface, parent):
    data = bpy.data.meshes.new(name)
    data.from_pydata([(x - center[0], -depth, center[1] - y) for x, y in pixels], [], faces)
    assert not data.validate()
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    data.materials.append(surface)
    return obj


def card(name, filename, region, corners):
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)
    image = bpy.data.images.load(str(ROOT / 'char/bg2/title' / filename))
    x, y, width, height = region
    center = (x + width / 2, y + height / 2)
    surface = bpy.data.materials.new(name + ' artwork')
    shader = surface.node_tree.nodes.get('Principled BSDF')
    texture = surface.node_tree.nodes.new('ShaderNodeTexImage')
    texture.image = image
    surface.node_tree.links.new(texture.outputs['Color'], shader.inputs['Base Color'])
    surface.node_tree.links.new(texture.outputs['Alpha'], shader.inputs['Alpha'])
    shader.inputs['Roughness'].default_value = .8
    pixels = [(x, y), (x + width, y), (x + width, y + height), (x, y + height)]
    front = mesh(name + 'Artwork', pixels, center, .1, [(3, 2, 1, 0)], surface, root)
    uv = front.data.uv_layers.new(name='Original artwork')
    for loop in front.data.loops:
        px, py = pixels[loop.vertex_index]
        uv.data[loop.index].uv = (px / image.size[0], 1 - py / image.size[1])

    edge = bpy.data.materials.new(name + ' edge')
    shader = edge.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (.12, .16, .2, 1)
    shader.inputs['Metallic'].default_value = .25
    shader.inputs['Roughness'].default_value = .45
    data = bpy.data.meshes.new('Thin card edge')
    data.from_pydata([(px - center[0], -z, center[1] - py) for z in (-1.1, -.1) for px, py in corners], [],
                       [(0, 1, 2, 3)] + [(i, i + 4, (i + 1) % 4 + 4, (i + 1) % 4) for i in range(4)])
    assert not data.validate()
    data.update()
    body = bpy.data.objects.new(name + 'Edge', data)
    bpy.context.collection.objects.link(body)
    body.parent = root
    data.materials.append(edge)
    return root


bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
SOURCE.mkdir(parents=True, exist_ok=True)
QA.mkdir(parents=True, exist_ok=True)
(SOURCE / '.gdignore').touch()
photo = card('PhotoPost', 'title_photo_v3.png', (492, 42, 280, 421),
             [(509, 56), (756, 146), (758, 447), (509, 381)])
draft = card('DraftPost', 'title_draft_v3.png', (482, 341, 448, 334),
             [(515, 373), (899, 456), (899, 638), (514, 568)])
bpy.context.view_layer.update()
for root in (photo, draft):
    assert all(face.area > 0 for child in root.children for face in child.data.polygons)
bpy.ops.export_scene.gltf(filepath=str(OUTPUT), export_format='GLB', export_yup=True,
                          export_cameras=False, export_lights=False)
report = {'blender': bpy.app.version_string, 'groups': [photo.name, draft.name],
          'bytes': OUTPUT.stat().st_size, 'source_art': ['title_photo_v3.png', 'title_draft_v3.png']}
(QA / 'geometry.json').write_text(json.dumps(report, indent=2), encoding='utf-8')

photo.location = (-140, 0, 70)
draft.location = (170, 0, -120)
for root in (photo, draft):
    surface = root.children[0].data.materials[0]
    shader = surface.node_tree.nodes.get('Principled BSDF')
    texture = next(node for node in surface.node_tree.nodes if node.type == 'TEX_IMAGE')
    surface.node_tree.links.new(texture.outputs['Color'], shader.inputs['Emission Color'])
    shader.inputs['Emission Strength'].default_value = .8
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples, scene.cycles.device = 24, 'CPU'
scene.cycles.use_denoising = True
scene.world.color = (.08, .1, .13)
scene.view_settings.view_transform = 'AgX'
scene.render.resolution_x, scene.render.resolution_y = 960, 720
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
bpy.ops.object.light_add(type='AREA', location=(-200, -400, 500))
light = bpy.context.object
light.data.energy, light.data.size = 400000, 450
light.rotation_euler = (-light.location).to_track_quat('-Z', 'Y').to_euler()
bpy.ops.object.camera_add(location=(0, -1200, 0))
scene.camera = bpy.context.object
scene.camera.rotation_euler = (math.pi / 2, 0, 0)
scene.camera.data.type, scene.camera.data.ortho_scale = 'ORTHO', 850
scene.camera.data.clip_end = 2000
bpy.ops.file.pack_all()
bpy.context.preferences.filepaths.save_version = 0
scene.render.filepath = str(QA / 'cards_preview.png')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'title_screen.blend'))
bpy.ops.render.render(write_still=True)
print('TITLE_CARD_ASSET ' + json.dumps(report))
