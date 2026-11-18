import math
from pathlib import Path

import bpy


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'assets/fx/akari_rain_cues'
SOURCE = ROOT / 'assets/source/akari_rain_cues'
QA = ROOT / 'build/qa_story/akari_rain_blender_20261030/assets'
for folder in (OUTPUT, SOURCE, QA):
    folder.mkdir(parents=True, exist_ok=True)
(SOURCE / '.gdignore').touch()

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)


def material(name, rgb, emission=0):
    value = bpy.data.materials.new(name)
    value.diffuse_color = (*rgb, 1)
    value.use_nodes = True
    shader = value.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*rgb, 1)
    shader.inputs['Roughness'].default_value = .42
    shader.inputs['Metallic'].default_value = .12
    shader.inputs['Emission Color'].default_value = (*rgb, 1)
    shader.inputs['Emission Strength'].default_value = emission
    return value


ink = material('Outline / existing #101829', (.006, .009, .022))
amber = material('Falling rain / existing #ffbd70', (1, .51, .16), .22)
violet = material('Rebound / existing #c6a0ff', (.56, .35, 1), .25)
models = []


def stroke(parent, name, points, color, radius=.045):
    for suffix, depth, surface, z in [(' outline', radius + .035, ink, 0), (' light', radius, color, .055)]:
        curve = bpy.data.curves.new(name + suffix, 'CURVE')
        curve.dimensions = '3D'
        curve.bevel_depth = depth
        curve.bevel_resolution = 3
        spline = curve.splines.new('POLY')
        spline.points.add(len(points) - 1)
        for target, point in zip(spline.points, points):
            target.co = (point[0], point[1], z + .08 * point[1], 1)
        obj = bpy.data.objects.new(name + suffix, curve)
        bpy.context.collection.objects.link(obj)
        obj.parent = parent
        obj.data.materials.append(surface)


def model(name, reference):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj['reference_art'] = reference
    obj['purpose'] = 'Stationary hollow warning; never a projectile or a safe-area marker'
    models.append(obj)
    return obj


drop = model('Rain origin', 'char/v3/fx/akari/easy_rain_drop_v1.png')
for segment in range(8):
    points = []
    for sample in range(15):
        angle = (segment + .08 + .84 * sample / 14) * math.tau / 8
        points.append((.86 * math.sin(angle) * (.74 - .26 * math.cos(angle)), .96 * math.cos(angle) + .13))
    stroke(drop, 'Broken droplet contour', points, amber)
stroke(drop, 'Downward fold', [(-.27, .14), (0, -.14), (.27, .14)], amber, .035)
stroke(drop, 'Downward point', [(-.27, -.16), (0, -.44), (.27, -.16)], amber, .035)

splash = model('Rain rebound', 'assets/fx/akari_splash.svg')
for side in (-1, 1):
    for segment in range(3):
        points = []
        for sample in range(12):
            t = (segment + .08 + .84 * sample / 11) / 3
            points.append((side * (1.13 * t), -.65 + 1.4 * math.sin(t * math.pi / 2)))
        stroke(splash, 'Split upward arc', points, violet, .05)
    stroke(splash, 'Fixed rebound tip', [(side * .76, .70), (side * 1.13, .75), (side * 1.08, .37)], violet, .043)
for side in (-1, 1):
    stroke(splash, 'Impact crown', [(side * .52, -.62), (side * .22, -.82), (side * .06, -.82)], violet, .04)

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.render.threads_mode = 'FIXED'
scene.render.threads = 4
scene.render.resolution_x = scene.render.resolution_y = 192
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.view_settings.view_transform = 'Standard'
scene.world.color = (.18, .18, .18)
bpy.ops.object.camera_add(location=(0, 0, 7))
camera = bpy.context.object
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 2.8
scene.camera = camera
for name, location, energy, size in [('Soft key', (-3, 4, 5), 420, 5), ('Edge light', (3, -1, 4), 190, 3)]:
    bpy.ops.object.light_add(type='AREA', location=location)
    light = bpy.context.object
    light.name = name
    light.data.energy = energy
    light.data.shape = 'DISK'
    light.data.size = size
    light.rotation_euler = (-light.location).to_track_quat('-Z', 'Y').to_euler()

for selected, filename in [(drop, 'fall.png'), (splash, 'rebound.png')]:
    for root in models:
        for child in root.children:
            child.hide_render = root != selected
    scene.render.filepath = str(OUTPUT / filename)
    bpy.ops.render.render(write_still=True)

for index, root in enumerate(models):
    root.location.x = -1.55 + index * 3.1
    for child in root.children:
        child.hide_render = False
camera.data.ortho_scale = 6.2
scene.render.resolution_x, scene.render.resolution_y = 768, 384
scene.render.filepath = str(QA / 'blender_cues.png')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'akari_rain_cues.blend'))
bpy.ops.render.render(write_still=True)
print('RAIN_CUES_COMPLETE: two transparent warning sprites and editable Blender source')
