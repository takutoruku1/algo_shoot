import math
from pathlib import Path

import bpy


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'assets/fx/akari_clock'
SOURCE = ROOT / 'assets/source/akari_clock'
for folder in (OUTPUT, SOURCE):
    folder.mkdir(parents=True, exist_ok=True)
(SOURCE / '.gdignore').touch()
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.context.preferences.filepaths.save_version = 0
bpy.context.preferences.filepaths.file_preview_type = 'NONE'


def material(name, color, metallic=0, emission=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    node = mat.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value = (*color, 1)
    node.inputs['Metallic'].default_value = metallic
    node.inputs['Roughness'].default_value = .3
    node.inputs['Emission Color'].default_value = (*color, 1)
    node.inputs['Emission Strength'].default_value = emission
    return mat


ink = material('Existing outline #101829', (.006, .009, .022), .35)
red = material('Clock hand red #ff7568', (.7, .065, .045), .45)
face = material('Dark red enamel', (.15, .012, .018), .25)
light = material('Existing warm white #fff3f2', (1, .82, .78), .25, .2)
steel = material('Office clock steel', (.13, .16, .2), .7)


def finish(obj, name, mat, bevel=.02):
    obj.name = name
    obj.data.materials.append(mat)
    modifier = obj.modifiers.new('Machined bevel', 'BEVEL')
    modifier.width = bevel
    modifier.segments = 3
    obj.modifiers.new('Face normals', 'WEIGHTED_NORMAL')
    return obj


def disc(name, radius, depth, z, mat):
    bpy.ops.mesh.primitive_cylinder_add(vertices=128, radius=radius, depth=depth, location=(0, 0, z))
    return finish(bpy.context.object, name, mat)


def block(name, position, size, mat, angle=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=position)
    obj = bpy.context.object
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_euler.z = angle
    return finish(obj, name, mat, .012)


disc('Collision footprint radius 1', 1, .19, 0, ink)
rim = disc('Raised red bezel', .97, .16, .09, red)
dial = disc('Recessed dial', .82, .07, .16, face)
for i in range(12):
    angle = i * math.tau / 12
    block(f'Hour index {i + 1}', (.73 * math.cos(angle), .73 * math.sin(angle), .215),
          (.14 if i % 3 == 0 else .09, .035, .035), light, angle)
disc('Axle socket', .51, .10, .22, ink)
cap = disc('Danger axle', .44, .12, .28, red)
cross = [block('Raised contact cross', (0, 0, .36), (.57, .11, .05), light, angle)
         for angle in (math.pi / 4, -math.pi / 4)]
for i in range(4):
    angle = math.pi / 4 + i * math.pi / 2
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=.045,
        location=(.9 * math.cos(angle), .9 * math.sin(angle), .2))
    finish(bpy.context.object, 'Bezel screw', steel, .005)

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 48
scene.cycles.use_denoising = True
scene.render.threads_mode = 'FIXED'
scene.render.threads = 4
scene.render.resolution_x = scene.render.resolution_y = 512
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.view_settings.view_transform = 'Standard'
scene.world.color = (.13, .13, .13)
bpy.ops.object.camera_add(location=(0, 0, 8))
camera = bpy.context.object
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 2.1
scene.camera = camera
for location, energy, size in [((-3, 4, 6), 450, 4), ((4, -2, 4), 180, 3)]:
    bpy.ops.object.light_add(type='AREA', location=location)
    obj = bpy.context.object
    obj.data.energy, obj.data.size = energy, size
    obj.rotation_euler = (-obj.location).to_track_quat('-Z', 'Y').to_euler()

scene['reference'] = 'src/AkariBossTechnique.cs ExtendingClock; src/AkariEasyTechniqueArt.cs; existing 12 hour dial and red hands'
scene['collision_radius_model_units'] = 1.0
scene['ortho_scale'] = 2.1
scene.render.filepath = str(OUTPUT / 'danger.png')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'akari_clock.blend'), relative_remap=False)
bpy.ops.render.render(write_still=True)
for obj in cross:
    obj.hide_render = True
for obj in (rim, dial, cap):
    obj.data.materials.clear()
    obj.data.materials.append(steel)
scene.render.filepath = str(OUTPUT / 'neutral.png')
bpy.ops.render.render(write_still=True)
print('AKARI_CLOCK_COMPLETE: danger and neutral dials; editable Blender source; radius=1, ortho=2.1')
