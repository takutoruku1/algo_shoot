import bpy
import json
import math
from pathlib import Path
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets/ui/voice_medallion.glb"
QA = ROOT / "build/qa_story/voice_medallion/assets"
SEGMENTS = 64


def material(name, color, metallic, roughness):
    result = bpy.data.materials.new(name)
    shader = result.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    return result


def mesh(name, vertices, faces, surface, smooth=True):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    assert not data.validate()
    data.update()
    for polygon in data.polygons:
        polygon.use_smooth = smooth
        assert polygon.area > 0.0000001
    result = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(result)
    result.data.materials.append(surface)
    return result


def lathe(name, profile, surface, center=(0, 0), ribs=False, segments=SEGMENTS):
    vertices, faces, rings = [], [], []
    for radius, depth in profile:
        ring = []
        for i in range(segments if radius else 1):
            angle = math.tau * i / segments
            r = radius - (0.009 if ribs and radius >= 0.97 and i % 2 else 0)
            ring.append(len(vertices))
            vertices.append((center[0] + r * math.cos(angle), -depth,
                             center[1] + r * math.sin(angle)))
        rings.append(ring)
    for j, ring in enumerate(rings):
        next_ring = rings[(j + 1) % len(rings)]
        if len(ring) == len(next_ring) == 1:
            continue
        for i in range(segments):
            after = (i + 1) % segments
            if len(ring) == 1:
                faces.append((ring[0], next_ring[after], next_ring[i]))
            elif len(next_ring) == 1:
                faces.append((ring[i], ring[after], next_ring[0]))
            else:
                faces.append((ring[i], ring[after], next_ring[after], next_ring[i]))
    return mesh(name, vertices, faces, surface)


def portrait(surface):
    radius, depth = 0.76, 0.09
    vertices = [(0, -depth, 0)] + [
        (radius * math.cos(math.tau * i / SEGMENTS), -depth,
         radius * math.sin(math.tau * i / SEGMENTS)) for i in range(SEGMENTS)
    ]
    faces = [(0, i + 1, (i + 1) % SEGMENTS + 1) for i in range(SEGMENTS)]
    result = mesh("Portrait", vertices, faces, surface, False)
    uv = result.data.uv_layers.new(name="UVMap")
    for loop in result.data.loops:
        point = result.data.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = (0.5 + point.x / (2 * radius),
                                  0.5 + point.z / (2 * radius))
    return result


def seal_check(surface):
    outline = [(-0.104, 0.005), (-0.078, 0.031), (-0.025, -0.021),
               (0.076, 0.086), (0.104, 0.060), (-0.023, -0.076)]
    if sum(outline[i][0] * outline[(i + 1) % len(outline)][1]
           - outline[(i + 1) % len(outline)][0] * outline[i][1]
           for i in range(len(outline))) < 0:
        outline.reverse()
    vertices = [(0.68 + x, -depth, -0.66 + y)
                for depth in (0.195, 0.205) for x, y in outline]
    count = len(outline)
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    for i in range(count):
        j = (i + 1) % count
        faces.append((i, j, j + count, i + count))
    return mesh("SealCheck", vertices, faces, surface, False)


def back_relief(name, outlines, surface, front=-0.184, back=-0.15, bevel=True):
    vertices, faces = [], []
    for outline in outlines:
        if sum(outline[i][0] * outline[(i + 1) % len(outline)][1]
               - outline[(i + 1) % len(outline)][0] * outline[i][1]
               for i in range(len(outline))) < 0:
            outline = list(reversed(outline))
        offset, count = len(vertices), len(outline)
        vertices.extend((-x, -depth, y) for depth in (back, front) for x, y in outline)
        faces.append(tuple(offset + i for i in reversed(range(count))))
        faces.append(tuple(offset + count + i for i in range(count)))
        for i in range(count):
            j = (i + 1) % count
            faces.append((offset + i, offset + j, offset + count + j, offset + count + i))
    result = mesh(name, vertices, faces, surface, False)
    if not bevel:
        return result
    bpy.context.view_layer.objects.active = result
    bevel = result.modifiers.new("ReliefBevel", 'BEVEL')
    bevel.width = 0.005
    bevel.segments = 1
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    return result


def bezier(points, steps=10):
    a, b, c, d = (Vector(point) for point in points)
    return [tuple((1 - t) ** 3 * a + 3 * (1 - t) ** 2 * t * b
                  + 3 * (1 - t) * t ** 2 * c + t ** 3 * d)
            for t in [i / steps for i in range(steps + 1)]]


def ribbon(points, widths):
    left, right = [], []
    for i, point in enumerate(points):
        tangent = Vector(points[min(i + 1, len(points) - 1)]) - Vector(points[max(0, i - 1)])
        normal = Vector((-tangent.y, tangent.x)).normalized()
        half = widths[0] + (widths[1] - widths[0]) * i / (len(points) - 1)
        left.append(tuple(Vector(point) + normal * half))
        right.append(tuple(Vector(point) - normal * half))
    return left + list(reversed(right))


def reverse_design(face_surface, metal):
    lathe("BackFace", [(0, -0.15), (0.84, -0.15), (0.868, -0.143),
                       (0.868, -0.132), (0, -0.132)], face_surface)
    outer = lathe("BackRim", [(0.86, -0.163), (0.939, -0.163), (0.962, -0.148),
                              (0.948, -0.131), (0.866, -0.131), (0.849, -0.148)], metal)
    inner = lathe("BackRimInner", [(0.659, -0.157), (0.671, -0.163),
                                   (0.683, -0.157), (0.671, -0.148)], metal)
    bpy.ops.object.select_all(action='DESELECT')
    outer.select_set(True)
    inner.select_set(True)
    bpy.context.view_layer.objects.active = outer
    bpy.ops.object.join()

    stem = [(-0.29, -0.36), (-0.25, -0.345), (-0.235, -0.32), (-0.235, 0.31),
            (-0.25, 0.335), (-0.29, 0.35), (-0.29, 0.375), (-0.065, 0.375),
            (-0.065, 0.35), (-0.12, 0.335), (-0.14, 0.31), (-0.14, -0.32),
            (-0.12, -0.345), (-0.065, -0.36), (-0.065, -0.385), (-0.29, -0.385)]
    upper_bowl = bezier([(-0.17, 0.337), (0.36, 0.43), (0.38, 0.025), (-0.18, 0.035)], 16)
    leg = bezier([(-0.025, 0.04), (0.16, -0.02), (0.10, -0.42), (0.34, -0.35)], 12)
    flourish = bezier([(-0.29, -0.49), (-0.09, -0.41), (0.09, -0.57), (0.32, -0.47)], 12)
    emblem = back_relief("BackEmblem", [stem], metal, bevel=False)
    for outline in [ribbon(upper_bowl, (0.04, 0.036)), ribbon(leg, (0.047, 0.018)),
                    ribbon(flourish, (0.014, 0.007))]:
        stroke = back_relief("ReliefStroke", [outline], metal, bevel=False)
        bpy.context.view_layer.objects.active = emblem
        union = emblem.modifiers.new("JoinStrokes", 'BOOLEAN')
        union.operation, union.solver, union.object = 'UNION', 'EXACT', stroke
        bpy.ops.object.modifier_apply(modifier=union.name)
        bpy.data.objects.remove(stroke, do_unlink=True)
    bevel = emblem.modifiers.new("EmblemBevel", 'BEVEL')
    bevel.width, bevel.segments = 0.005, 1
    bpy.ops.object.modifier_apply(modifier=bevel.name)

    marks = []
    for i in range(32):
        angle = math.tau * i / 32
        direction = Vector((math.cos(angle), math.sin(angle)))
        sideways = Vector((-direction.y, direction.x))
        center = direction * 0.77
        half_length, half_width = (0.036, 0.012) if i % 8 == 0 else (0.018, 0.006)
        marks.append([tuple(center - direction * half_length - sideways * half_width),
                      tuple(center + direction * half_length - sideways * half_width),
                      tuple(center + direction * half_length + sideways * half_width),
                      tuple(center - direction * half_length + sideways * half_width)])
    back_relief("BackMarks", marks, metal, front=-0.163, back=-0.151)


bpy.ops.wm.read_factory_settings(use_empty=True)
dark_metal = material("Body", (0.085, 0.12, 0.15), 0.78, 0.32)
bright_metal = material("Bezel", (0.78, 0.68, 0.44), 0.8, 0.24)
enamel = material("Inlay", (0.045, 0.37, 0.26), 0.3, 0.23)
face_material = material("Portrait", (0.85, 0.89, 0.84), 0, 0.52)
seal_material = material("Seal", (0.035, 0.22, 0.16), 0.45, 0.24)
check_material = material("SealCheck", (0.82, 1, 0.91), 0.35, 0.22)
back_material = material("BackFace", (0.018, 0.074, 0.062), 0.32, 0.3)

lathe("Body", [(0, -0.13), (0.94, -0.13), (0.98, -0.105),
               (0.995, -0.07), (0.995, 0.045), (0.97, 0.075),
               (0, 0.075)], dark_metal, ribs=True)
lathe("Bezel", [(0.916, 0.071), (0.976, 0.071), (1, 0.091),
                (0.987, 0.119), (0.965, 0.14), (0.938, 0.14),
                (0.916, 0.116)], bright_metal)
lathe("Inlay", [(0.785, 0.076), (0.923, 0.076), (0.927, 0.093),
                (0.908, 0.108), (0.801, 0.108), (0.785, 0.093)], enamel)
lathe("InnerTrim", [(0.758, 0.083), (0.793, 0.083), (0.802, 0.103),
                    (0.787, 0.12), (0.773, 0.12), (0.758, 0.105)], bright_metal)
portrait_object = portrait(face_material)
lathe("Seal", [(0, 0.115), (0.16, 0.115), (0.19, 0.145),
               (0.19, 0.169), (0.166, 0.193), (0, 0.193)],
      seal_material, center=(0.68, -0.66), segments=32)
seal_check(check_material)
reverse_design(back_material, bright_metal)

objects = list(bpy.context.scene.objects)
triangles = sum(len(poly.vertices) - 2 for obj in objects for poly in obj.data.polygons)
assert triangles < 8000
assert all(obj.scale == Vector((1, 1, 1)) for obj in objects)
assert all(polygon.normal.y < -0.99 for polygon in portrait_object.data.polygons)
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
QA.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(OUTPUT), export_format='GLB', export_yup=True,
                         export_animations=False, export_cameras=False,
                         export_lights=False, export_texcoords=True)
report = {"blender_version": bpy.app.version_string, "triangles": triangles,
          "glb_bytes": OUTPUT.stat().st_size, "nodes": [obj.name for obj in objects],
          "front_axis": "+Z", "up_axis": "+Y", "portrait_radius": 0.76,
          "portrait_depth": 0.09, "portrait_uv": "top-left after glTF conversion"}
(QA / "asset.json").write_text(json.dumps(report, indent=2), encoding="utf-8")

image = bpy.data.images.load(str(ROOT / "char/v3/rei_face.png"))
nodes = face_material.node_tree.nodes
image_node = nodes.new("ShaderNodeTexImage")
image_node.image = image
face_material.node_tree.links.new(image_node.outputs["Color"],
                                 nodes.get("Principled BSDF").inputs["Base Color"])
scene = bpy.context.scene
scene.world = bpy.data.worlds.new("PreviewWorld")
scene.world.node_tree.nodes.get("Background").inputs[0].default_value = (0.09, 0.12, 0.14, 1)
scene.world.node_tree.nodes.get("Background").inputs[1].default_value = 0.65

for name, position, color, power, size in [
    ("Key", (-3, -4, 5), (1, 0.9, 0.72), 650, 4),
    ("Fill", (4, -2, 1), (0.55, 0.8, 1), 450, 3),
    ("Rim", (0, 2, 3), (0.5, 1, 0.8), 800, 3),
]:
    light = bpy.data.lights.new(name, "AREA")
    light.color, light.energy, light.size = color, power, size
    obj = bpy.data.objects.new(name, light)
    bpy.context.collection.objects.link(obj)
    obj.location = position
    obj.rotation_euler = (-obj.location).to_track_quat('-Z', 'Y').to_euler()

camera = bpy.data.objects.new("PreviewCamera", bpy.data.cameras.new("PreviewCamera"))
bpy.context.collection.objects.link(camera)
camera.location = (2.6, -8, 1.6)
camera.rotation_euler = (-camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.type, camera.data.ortho_scale = 'ORTHO', 2.65
scene.camera = camera
scene.render.engine = 'CYCLES'
scene.cycles.device, scene.cycles.samples = 'CPU', 24
scene.render.resolution_x = scene.render.resolution_y = 384
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.filepath = str(QA / "preview.png")
bpy.ops.render.render(write_still=True)
camera.location = (-2.2, 8, 1.4)
camera.rotation_euler = (-camera.location).to_track_quat('-Z', 'Y').to_euler()
for name in ("Key", "Fill", "Rim"):
    light = bpy.data.objects[name]
    light.location.y *= -1
    light.rotation_euler = (-light.location).to_track_quat('-Z', 'Y').to_euler()
scene.render.filepath = str(QA / "reverse.png")
bpy.ops.render.render(write_still=True)
print("VOICE_MEDALLION_ASSET " + json.dumps(report))
