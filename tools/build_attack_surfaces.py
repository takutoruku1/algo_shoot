"""Bake the cross sections used for active enemy beams, panels and circular fields."""
from pathlib import Path
import bpy

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets/fx/attack_surfaces"
SOURCE = ROOT / "assets/source/attack_surfaces"
OUTPUT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
(SOURCE / ".gdignore").touch()
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
surface = bpy.data.materials.new("Pearlescent silver")
surface.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (.52, .52, .52, 1)
surface.node_tree.nodes.get("Principled BSDF").inputs["Roughness"].default_value = .29

bpy.ops.mesh.primitive_cube_add(size=2)
plate = bpy.context.object
plate.name = "Raised panel"
plate.scale.z = .17
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
bevel = plate.modifiers.new("Rounded border", "BEVEL")
bevel.width, bevel.segments = .15, 6
plate.modifiers.new("Face normals", "WEIGHTED_NORMAL")

bpy.ops.mesh.primitive_uv_sphere_add(segments=96, ring_count=48)
lens = bpy.context.object
lens.name = "Domed lens"
lens.scale.z = .48
for face in lens.data.polygons:
    face.use_smooth = True
bpy.ops.mesh.primitive_cylinder_add(vertices=96, radius=1, depth=4, rotation=(0, 1.5707963, 0))
rail = bpy.context.object
rail.name = "Light rail cross section"
for face in rail.data.polygons:
    face.use_smooth = len(face.vertices) == 4
bpy.ops.mesh.primitive_torus_add(major_segments=96, minor_segments=32, major_radius=.89, minor_radius=.11)
collar = bpy.context.object
collar.name = "Contact rim"
for face in collar.data.polygons:
    face.use_smooth = True
models = {"panel": plate, "lens": lens, "rail": rail, "rim": collar}
for obj in models.values():
    obj.data.materials.append(surface)
    obj.hide_render = True

scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 32
scene.render.threads_mode, scene.render.threads = "FIXED", 4
scene.render.resolution_x = scene.render.resolution_y = 128
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"
scene.world.color = (.18, .18, .18)
bpy.ops.object.camera_add(location=(0, 0, 5))
scene.camera = bpy.context.object
scene.camera.rotation_euler = (0, 0, 0)
scene.camera.data.type, scene.camera.data.ortho_scale = "ORTHO", 2
for location, power, size in [((-2.5, 3, 3), 280, 2), ((1, -2, 2), 45, 3)]:
    bpy.ops.object.light_add(type="AREA", location=location)
    light = bpy.context.object
    light.data.energy, light.data.size = power, size
    light.rotation_euler = (-light.location).to_track_quat("-Z", "Y").to_euler()

for name, obj in models.items():
    obj.hide_render = False
    scene.render.filepath = str(OUTPUT / (name + ".png"))
    bpy.ops.render.render(write_still=True)
    obj.hide_render = True
for i, obj in enumerate(models.values()):
    obj.hide_render = False
    obj.location.x = (i - 1) * 4
scene.camera.location.x = 2
scene.camera.data.ortho_scale = 18
bpy.context.preferences.filepaths.save_version = 0
bpy.context.preferences.filepaths.file_preview_type = 'NONE'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "attack_surfaces.blend"), relative_remap=False)
