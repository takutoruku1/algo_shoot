import bpy
import json
import math
import struct
from pathlib import Path
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets/fx/rei_rally.glb"
QA = ROOT / "build/qa_story/rei_rally_3d/assets"


def surface(name, color, metallic, roughness):
    result = bpy.data.materials.new(name)
    shader = result.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    return result


def group(name):
    result = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(result)
    return result


def point(x, y, z):
    return (x, -z, y)


def mesh(name, vertices, faces, material, parent, smooth=False):
    data = bpy.data.meshes.new(name)
    data.from_pydata([point(*vertex) for vertex in vertices], [], faces)
    assert not data.validate()
    data.update()
    result = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(result)
    result.parent = parent
    data.materials.append(material)
    for polygon in data.polygons:
        polygon.use_smooth = smooth
        assert polygon.area > 0.00000001
    return result


def sphere(name, radius, material, parent, segments=40, rings=24):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=radius)
    result = bpy.context.object
    result.name = name
    result.parent = parent
    result.data.materials.append(material)
    for polygon in result.data.polygons:
        polygon.use_smooth = True
    return result


def torus(name, radius, tube, material, parent, tilt=0):
    vertices, faces = [], []
    for i in range(64):
        a = math.tau * i / 64
        for j in range(8):
            b = math.tau * j / 8
            r = radius + math.cos(b) * tube
            x, y, z = r * math.cos(a), r * math.sin(a), math.sin(b) * tube
            vertices.append((x * math.cos(tilt) + z * math.sin(tilt), y,
                             z * math.cos(tilt) - x * math.sin(tilt)))
    for i in range(64):
        for j in range(8):
            faces.append((i * 8 + j, ((i + 1) % 64) * 8 + j,
                          ((i + 1) % 64) * 8 + (j + 1) % 8, i * 8 + (j + 1) % 8))
    return mesh(name, vertices, faces, material, parent, True)


def cone(name, direction, material, parent):
    axis = Vector(direction).normalized()
    tangent = axis.cross(Vector((0, 0, 1))).normalized()
    bitangent = axis.cross(tangent)
    vertices = [tuple(axis * .995)]
    vertices += [tuple(axis * .57 + .145 * (math.cos(i * math.tau / 8) * tangent
                 + math.sin(i * math.tau / 8) * bitangent)) for i in range(8)]
    faces = [(0, i + 1, (i + 1) % 8 + 1) for i in range(8)]
    faces.append(tuple(reversed(range(1, 9))))
    return mesh(name, vertices, faces, material, parent)


def star(name, material, parent, reverse=False):
    outline = [(math.sin(i * math.pi / 5) * (.43 if i % 2 == 0 else .19),
                math.cos(i * math.pi / 5) * (.43 if i % 2 == 0 else .19)) for i in range(10)]
    outline.reverse()
    sign = -1 if reverse else 1
    vertices = [(0, 0, sign * .942)]
    vertices += [(x, y, sign * (math.sqrt(.91 ** 2 - x * x - y * y) + .025)) for x, y in outline]
    faces = [(0, i + 1, (i + 1) % 10 + 1) for i in range(10)]
    if reverse:
        faces = [tuple(reversed(face)) for face in faces]
    return mesh(name, vertices, faces, material, parent)


def ribbon(name, a, b, width, radius, material, parent):
    direction = (Vector(b) - Vector(a)).normalized()
    normal = Vector((-direction.y, direction.x)) * width / 2
    vertices, faces = [], []
    for i in range(9):
        center = Vector(a).lerp(Vector(b), i / 8)
        for side in (-1, 1):
            p = center + normal * side
            vertices.append((p.x, p.y, math.sqrt(radius ** 2 - p.length_squared) + .018))
    for i in range(8):
        faces.append((i * 2, i * 2 + 2, i * 2 + 3, i * 2 + 1))
    return mesh(name, vertices, faces, material, parent, True)


def prism(name, outline, front, back, material, parent, bevel=0):
    area = sum(outline[i][0] * outline[(i + 1) % len(outline)][1]
               - outline[(i + 1) % len(outline)][0] * outline[i][1] for i in range(len(outline)))
    if area < 0:
        outline = list(reversed(outline))
    count = len(outline)
    vertices = [(x, y, z) for z in (back, front) for x, y in outline]
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    faces += [(i, (i + 1) % count, (i + 1) % count + count, i + count) for i in range(count)]
    result = mesh(name, vertices, faces, material, parent)
    if bevel:
        bpy.context.view_layer.objects.active = result
        modifier = result.modifiers.new("Edge bevel", 'BEVEL')
        modifier.width = bevel
        modifier.segments = 2
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return result


def join(name, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    objects[0].name = name
    return objects[0]


def make_wall(parent, body, face, metal):
    upper = [(-.08, .5), (-.02, .33), (-.10, .17), (.04, .05)]
    left = [(-.5, .06), (-.3, .13), (-.15, -.03), (.04, .05)]
    lower = [(.04, .05), (.13, -.12), (-.03, -.29), (.02, -.5)]
    right = [(.04, .05), (.22, -.04), (.35, .04), (.5, -.02)]
    outlines = [
        [(-.5, .5)] + upper + list(reversed(left[:-1])),
        upper[:1] + [(.5, .5)] + list(reversed(right)) + list(reversed(upper[1:-1])),
        right + [(.5, -.5)] + list(reversed(lower[1:])),
        lower + [(-.5, -.5)] + left[:-1],
    ]
    for index, outline in enumerate(outlines):
        center = sum((Vector(p) for p in outline), Vector((0, 0))) / len(outline)
        inset = [tuple(Vector(p).lerp(center, .007)) for p in outline]
        pieces = [prism(f"Shard{index}", inset, .135, -.175, body, parent, .009)]
        inner = [tuple(Vector(p).lerp(center, .065)) for p in outline]
        pieces.append(prism(f"Face{index}", inner, .15, .132, face, parent, .006))
        for j, a in enumerate(outline):
            b = outline[(j + 1) % len(outline)]
            vertical = abs(a[0] - b[0]) < .0001 and abs(a[0]) == .5
            horizontal = abs(a[1] - b[1]) < .0001 and abs(a[1]) == .5
            if not (vertical or horizontal):
                continue
            inward = Vector((-math.copysign(.032, a[0]), 0)) if vertical else Vector((0, -math.copysign(.032, a[1])))
            edge = [tuple(Vector(a) * .995), tuple(Vector(b) * .995),
                    tuple(Vector(b) * .995 + inward), tuple(Vector(a) * .995 + inward)]
            pieces.append(prism(f"Rim{index}_{j}", edge, .175, .13, metal, parent, .005))
        shard = join(f"Shard{index}", pieces)
        bpy.ops.object.select_all(action='DESELECT')
        shard.select_set(True)
        bpy.context.view_layer.objects.active = shard
        bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')


def build():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    jade = surface("RallyJadeEnamel", (.035, .39, .25), .45, .2)
    gold = surface("RallyChampagne", (.83, .63, .29), .78, .23)
    dark = surface("RallyObsidian", (.065, .018, .06), .5, .27)
    red = surface("RallyCoralSpikes", (.72, .075, .17), .45, .25)
    warning = surface("RallyIvoryMark", (.95, .77, .66), .28, .25)
    lilac = surface("RallyLilacShell", (.31, .17, .46), .45, .23)
    silver = surface("RallySilver", (.68, .66, .83), .72, .2)
    wall_body = surface("RallyWallEdge", (.055, .035, .10), .65, .26)
    wall_face = surface("RallyWallEnamel", (.23, .095, .33), .48, .25)

    returned = group("ReturnBall")
    sphere("ReturnCore", .91, jade, returned)
    torus("ReturnBand", .925, .04, gold, returned, .5)
    star("ReturnStar", gold, returned)
    star("ReturnStarBack", gold, returned, True)

    danger = group("DangerBall")
    sphere("DangerCore", .69, dark, danger)
    spikes = []
    for i in range(8):
        angle = i * math.tau / 8
        spikes.append(cone(f"Spike{i}", (math.cos(angle), math.sin(angle), 0), red, danger))
    for i in range(4):
        angle = i * math.tau / 4 + math.pi / 4
        spikes.append(cone(f"BackSpike{i}", (math.cos(angle) * .8, math.sin(angle) * .8, -.6), red, danger))
    join("DangerSpikes", spikes)
    ribbon("DangerCrossA", (-.22, -.22), (.22, .22), .12, .69, warning, danger)
    ribbon("DangerCrossB", (-.22, .22), (.22, -.22), .12, .692, warning, danger)

    hidden = group("HiddenBall")
    sphere("HiddenCore", .93, lilac, hidden)
    torus("HiddenBand", .946, .025, silver, hidden, -.35)
    for i in range(3):
        dot = sphere(f"HiddenDot{i}", .055, silver, hidden, 12, 8)
        x = (i - 1) * .19
        dot.location = point(x, 0, math.sqrt(.93 ** 2 - x * x) + .005)

    wall = group("WallTile")
    make_wall(wall, wall_body, wall_face, gold)
    return [returned, danger, hidden, wall]


def validate(groups):
    result = {}
    for root in groups:
        vertices, triangles = [], 0
        for obj in root.children:
            if obj.type != 'MESH':
                continue
            obj.data.calc_loop_triangles()
            triangles += len(obj.data.loop_triangles)
            vertices += [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
        bounds = [[min(v[axis] for v in vertices), max(v[axis] for v in vertices)] for axis in range(3)]
        radius = max(vertex.length for vertex in vertices)
        if root.name != "WallTile":
            assert radius <= 1.00001, (root.name, radius)
        result[root.name] = {"triangles": triangles, "radius": radius, "blender_bounds": bounds,
                             "nodes": [child.name for child in root.children]}
    QA.mkdir(parents=True, exist_ok=True)
    (QA / "geometry.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


def preview(groups):
    for index, root in enumerate(groups):
        root.location = ((index - 1.5) * 2.45, 0, 0)
    groups[-1].scale = (1.25, 1.25, 1.85)
    groups[-1].rotation_euler.z = -.23
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 48
    scene.render.resolution_x = 1440
    scene.render.resolution_y = 450
    scene.render.resolution_percentage = 100
    scene.world.color = (.14, .14, .14)
    scene.view_settings.view_transform = 'AgX'
    bpy.ops.object.camera_add(location=(0, -12, 3.0))
    camera = bpy.context.object
    camera.rotation_euler = (Vector((0, 0, 0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 10.3
    scene.camera = camera
    for location, energy, color, size in [((-4, -5, 5), 950, (1, .87, .67), 5),
                                         ((5, -2, 1), 650, (.48, .72, 1), 4),
                                         ((0, 3, 4), 1100, (.63, .48, 1), 3)]:
        bpy.ops.object.light_add(type='AREA', location=location)
        light = bpy.context.object
        light.data.energy = energy
        light.data.color = color
        light.data.shape = 'DISK'
        light.data.size = size
        light.rotation_euler = (-light.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = str(QA / "models.png")
    bpy.ops.render.render(write_still=True)


groups = build()
bpy.context.view_layer.update()
report = validate(groups)
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(OUTPUT), export_format='GLB', export_yup=True,
                          export_cameras=False, export_lights=False)
raw = OUTPUT.read_bytes()
length = struct.unpack_from('<I', raw, 12)[0]
document = json.loads(raw[20:20 + length])
assert not document.get("images") and not document.get("textures")
assert all(root.name in [node.get("name") for node in document["nodes"]] for root in groups)
assert all(buffer.get("uri") is None for buffer in document["buffers"])
assert all(abs(value - expected) < .000001
           for node in document["nodes"] if node.get("name") in {root.name for root in groups}
           for key, default in (("translation", [0, 0, 0]), ("rotation", [0, 0, 0, 1]), ("scale", [1, 1, 1]))
           for value, expected in zip(node.get(key, default), default))
print(json.dumps({"output": str(OUTPUT), "bytes": len(raw), "geometry": report}, indent=2))
preview(groups)
