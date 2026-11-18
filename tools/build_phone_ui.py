import bpy
import json
import math
import struct
from pathlib import Path
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets/ui/phone_ui.glb"
QA = ROOT / "build/qa_story/phone_ui_3d/assets"
COLORS = ["6dc9d0", "db91b1", "a5c9a7", "e3bd76", "a5b9e4", "e1a18a", "8ebfb7", "baa2df", "e8a6c8", "ade48e"]


def rgb(code):
    values = [int(code[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(value / 12.92 if value < .04045 else ((value + .055) / 1.055) ** 2.4 for value in values)


def mix(a, b, weight):
    return tuple(x * (1 - weight) + y * weight for x, y in zip(a, b))


def material(name, color, metallic, roughness):
    result = bpy.data.materials.new(name)
    shader = result.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    return result


def point(x, y, z):
    return (x, -z, y)


def group(name):
    result = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(result)
    return result


def mesh(name, vertices, faces, surface, parent, smooth=False):
    data = bpy.data.meshes.new(name)
    data.from_pydata([point(*vertex) for vertex in vertices], [], faces)
    assert not data.validate()
    data.update()
    result = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(result)
    result.parent = parent
    data.materials.append(surface)
    for face in data.polygons:
        face.use_smooth = smooth
        assert face.area > 1e-10
    return result


def rounded(width, height, radius, center=(0, 0), segments=12):
    points = []
    for x, y, start in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
        for index in range(segments + 1):
            angle = math.radians(start + 90 * index / segments)
            points.append((center[0] + x * (width / 2 - radius) + radius * math.cos(angle),
                           center[1] + y * (height / 2 - radius) + radius * math.sin(angle)))
    return points


def prism(name, outline, front, back, surface, parent, bevel=0):
    area = sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(outline, outline[1:] + outline[:1]))
    if area < 0:
        outline = list(reversed(outline))
    count = len(outline)
    vertices = [(x, y, depth) for depth in (back, front) for x, y in outline]
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    faces += [(i, (i + 1) % count, (i + 1) % count + count, i + count) for i in range(count)]
    result = mesh(name, vertices, faces, surface, parent)
    if bevel:
        bpy.context.view_layer.objects.active = result
        modifier = result.modifiers.new("Machined edge", 'BEVEL')
        modifier.width = bevel
        modifier.segments = 3
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        normal = result.modifiers.new("Weighted face normals", 'WEIGHTED_NORMAL')
        normal.keep_sharp = True
        normal.weight = 50
        bpy.ops.object.modifier_apply(modifier=normal.name)
    return result


def rounded_prism(name, width, height, radius, front, back, surface, parent, center=(0, 0), bevel=.005):
    return prism(name, rounded(width, height, radius, center), front, back, surface, parent, bevel)


def ring(name, profiles, surface, parent):
    vertices = []
    for width, height, radius, depth in profiles:
        vertices += [(x, y, depth) for x, y in rounded(width, height, radius)]
    count = len(vertices) // len(profiles)
    faces = []
    for profile in range(len(profiles)):
        following = (profile + 1) % len(profiles)
        for index in range(count):
            faces.append((profile * count + index, profile * count + (index + 1) % count,
                          following * count + (index + 1) % count, following * count + index))
    return mesh(name, vertices, faces, surface, parent, True)


def stroke(name, points, width, surface, parent, closed=False, depth=.085):
    data = bpy.data.curves.new(name, 'CURVE')
    data.dimensions = '3D'
    data.resolution_u = 1
    data.bevel_depth = width / 2
    data.bevel_resolution = 2
    data.resolution_u = 2
    data.use_fill_caps = True
    spline = data.splines.new('POLY')
    spline.points.add(len(points) - 1)
    for control, p in zip(spline.points, points):
        control.co = (*point(p[0], p[1], depth), 1)
    spline.use_cyclic_u = closed
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    data.materials.append(surface)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target='MESH')
    return bpy.context.object


def arc(name, center, radius, start, end, width, surface, parent):
    segments = max(12, round((end - start) / math.tau * 48))
    closed = abs(end - start - math.tau) < .00001
    count = segments if closed else segments + 1
    return stroke(name, [(center[0] + radius * math.cos(start + (end - start) * i / segments),
                          center[1] + radius * math.sin(start + (end - start) * i / segments))
                         for i in range(count)], width, surface, parent, closed)


def dot(name, x, y, radius, surface, parent):
    return prism(name, [(x + math.cos(i * math.tau / 20) * radius,
                         y + math.sin(i * math.tau / 20) * radius) for i in range(20)], .092, .07,
                 surface, parent, .003)


def join(name, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    objects[0].name = name
    return objects[0]


def make_phone():
    root = group("Phone")
    graphite = material("PhoneGraphite", rgb("192936"), .65, .3)
    metal = material("PhoneTitanium", rgb("8b9eac"), .83, .25)
    black = material("PhoneBezel", rgb("081019"), .18, .3)
    glass = material("PhoneGlassEdge", rgb("6e939e"), .65, .16)
    rounded_prism("Chassis", 5.08, 7.48, .52, -.04, -.24, graphite, root, bevel=.022)
    ring("Rim", [(5.08, 7.48, .52, -.055), (5.08, 7.48, .52, -.035),
                 (5.058, 7.458, .509, -.012), (4.99, 7.39, .475, -.012),
                 (4.99, 7.39, .475, -.055)], metal, root)
    ring("Bezel", [(4.99, 7.39, .475, -.04), (4.99, 7.39, .475, -.01),
                   (4.825, 7.225, .3925, -.002), (4.8, 7.2, .38, -.002),
                   (4.8, 7.2, .38, -.04)], black, root)
    ring("GlassEdge", [(4.825, 7.225, .3925, -.008), (4.825, 7.225, .3925, 0),
                       (4.8, 7.2, .38, 0), (4.8, 7.2, .38, -.008)], glass, root)
    for index, (x, y, height) in enumerate(((-2.56, 2.12, .3), (-2.56, 1.55, .6), (2.56, 1.5, .68))):
        rounded_prism(f"SideButton{index}", .045, height, .02, -.07, -.175,
                      metal, root, (x, y), .008)
    for index, (x, y) in enumerate(((-2.535, 2.65), (-2.535, -2.65), (2.535, 2.65), (2.535, -2.65))):
        rounded_prism(f"Antenna{index}", .018, .024, .004, -.024, -.235, black, root, (x, y), .002)
    return root


def make_app(index):
    root = group(f"App{index}")
    accent = rgb(COLORS[index])
    body = material(f"App{index}Enamel", mix(rgb("142332"), accent, .25), .15, .48)
    metal = material(f"App{index}Titanium", mix(accent, rgb("c7d8df"), .45), .75, .25)
    relief = material(f"App{index}Relief", mix(accent, rgb("f3f2e9"), .8), .35, .23)
    rounded_prism(f"App{index}_Base", .88, .88, .23, .06, -.07, body, root, bevel=.017)
    ring(f"App{index}_Rim", [(.88, .88, .23, -.01), (.879, .879, .2295, .025),
                            (.864, .864, .222, .045), (.846, .846, .213, .045),
                            (.853, .853, .2165, .025)], metal, root)
    parts = []
    def line(points, width=.028, closed=False):
        parts.append(stroke(f"Glyph{index}_{len(parts)}", points, width, relief, root, closed))
    def circle(center, radius, start=0, end=math.tau, width=.028):
        parts.append(arc(f"Glyph{index}_{len(parts)}", center, radius, start, end, width, relief, root))
    def disc(x, y, radius):
        parts.append(dot(f"Glyph{index}_{len(parts)}", x, y, radius, relief, root))
    if index == 0:
        outline = rounded(.46, .34, .075, (0, .03))
        # Put the tail into the lower edge instead of crossing two separate outline strokes.
        outline = [p for p in outline if not (abs(p[1] + .14) < .0001 and -.12 < p[0] < .02)]
        corner = next(i for i, p in enumerate(outline) if abs(p[1] + .14) < .0001 and p[0] < -.12)
        outline[corner + 1:corner + 1] = [(-.11, -.14), (-.11, -.235), (.015, -.14)]
        line(outline, .03, True)
        for x in (-.11, 0, .11):
            disc(x, .035, .026)
    elif index == 1:
        line(rounded(.4, .36, .045, (0, -.06)), .03, True)
        circle((0, .12), .105, 0, math.pi, .03)
        w, half = .026, .09
        plus = [(-w, half), (w, half), (w, w), (half, w), (half, -w),
                (w, -w), (w, -half), (-w, -half), (-w, -w), (-half, -w), (-half, w), (-w, w)]
        parts.append(prism("Plus", [(x, y - .06) for x, y in plus], .1, .07, relief, root, .006))
    elif index == 2:
        for bar in range(3):
            height = .17 + bar * .12
            parts.append(rounded_prism(f"Bar{bar}", .1, height, .012, .105, .066,
                                      relief, root, (-.16 + bar * .16, -.22 + height / 2), .006))
    elif index == 3:
        line(rounded(.5, .34, .06, (0, -.01)), .03, True)
        parts.append(rounded_prism("CameraTop", .19, .09, .025, .098, .072, relief, root, (-.055, .195), .006))
        circle((.01, -.01), .105)
        disc(.01, -.01, .04)
    elif index == 4:
        cursor = [(-.19, .275), (.195, -.015), (.067, -.033), (.124, -.224),
                  (.025, -.159), (-.008, -.065), (-.086, -.205)]
        parts.append(prism("Cursor", cursor, .107, .067, relief, root, .006))
        parts.append(mesh("CursorFold", [(-.174, .249, .111), (.048, -.026, .155),
                                        (.177, -.01, .111), (-.009, -.062, .113)],
                          [(0, 1, 2), (0, 3, 1)], metal, root))
    elif index == 5:
        line([(-.23, .2), (-.11, .12), (0, .25), (.11, .12), (.23, .2),
              (.18, 0), (-.18, 0)], .03, True)
        line([(-.17, -.06), (.17, -.06)], .03)
        for x in (-.12, .04):
            line([(x, -.14), (x + .08, -.22), (x, -.3)], .03)
    elif index == 6:
        circle((0, 0), .22)
        circle((0, 0), .11)
        disc(0, 0, .03)
        for direction in ((0, 1), (0, -1), (-1, 0), (1, 0)):
            line([(direction[0] * .19, direction[1] * .19),
                  (direction[0] * .29, direction[1] * .29)], .028)
    elif index == 7:
        for x in (-1, 1):
            for y in (-1, 1):
                line([(x * .16, y * .26), (x * .26, y * .26), (x * .26, y * .16)], .028)
        circle((0, .08), .08)
        circle((0, -.18), .15, 0, math.pi)
        line([(-.15, -.18), (.15, -.18)], .028)
    elif index == 8:
        line([(-.13, -.12), (-.13, .20), (.15, .26), (.15, -.06)], .045)
        line([(-.13, .11), (.15, .17)], .04)
        disc(-.19, -.14, .075)
        disc(.09, -.08, .075)
    else:
        circle((0, .14), .25, math.pi, 2 * math.pi, .028)
        line([(-.25, .14), (.25, .14)], .028)
        for x in (-.16, 0, .16):
            line([(0, .12), (x, -.045 if x else -.09)], .018)
        line([(-.11, -.10), (-.15, -.23), (-.02, -.125)], .028)
    join(f"App{index}_Symbol", parts)
    return root


def validate(groups):
    report = {}
    for root in groups:
        vertices, triangles = [], 0
        for obj in root.children:
            assert obj.type == 'MESH'
            obj.data.calc_loop_triangles()
            triangles += len(obj.data.loop_triangles)
            vertices.extend(obj.matrix_world @ vertex.co for vertex in obj.data.vertices)
        bounds = [[min(vertex[axis] for vertex in vertices), max(vertex[axis] for vertex in vertices)] for axis in range(3)]
        assert root.location.length < 1e-8 and root.rotation_euler.to_matrix().is_identity
        if root.name.startswith("App"):
            assert bounds[0][0] >= -.4401 and bounds[0][1] <= .4401
            assert bounds[2][0] >= -.4401 and bounds[2][1] <= .4401
        report[root.name] = {"triangles": triangles, "blender_bounds": bounds,
                             "children": [child.name for child in root.children]}
    QA.mkdir(parents=True, exist_ok=True)
    (QA / "geometry.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


def export(groups):
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(OUTPUT), export_format='GLB', export_yup=True,
                              export_cameras=False, export_lights=False)
    raw = OUTPUT.read_bytes()
    length = struct.unpack_from('<I', raw, 12)[0]
    document = json.loads(raw[20:20 + length])
    encoded = json.dumps(document, separators=(',', ':')).encode('utf-8')
    encoded += b' ' * ((4 - len(encoded) % 4) % 4)
    binary = raw[20 + length:]
    OUTPUT.write_bytes(struct.pack('<4sII', b'glTF', 2, 20 + len(encoded) + len(binary))
                       + struct.pack('<I4s', len(encoded), b'JSON') + encoded + binary)
    assert not document.get("images") and not document.get("textures") and not document.get("cameras")
    assert all(buffer.get("uri") is None for buffer in document["buffers"])
    for node in document["nodes"]:
        if node.get("name") not in {root.name for root in groups}:
            continue
        for key, default in (("translation", [0, 0, 0]), ("rotation", [0, 0, 0, 1]), ("scale", [1, 1, 1])):
            assert all(abs(value - wanted) < .000001 for value, wanted in zip(node.get(key, default), default))
        if node["name"].startswith("App"):
            assert sorted(document["nodes"][i]["name"] for i in node["children"]) == [
                f'{node["name"]}_Base', f'{node["name"]}_Rim', f'{node["name"]}_Symbol']


def preview(groups):
    groups[0].location = (-3.0, 0, 0)
    groups[0].scale = (.82, .82, .82)
    groups[0].rotation_euler.z = -.26
    for index, root in enumerate(groups[1:]):
        root.location = (index % 4 * 1.4 + .2, -.2, .95 - index // 4 * 1.75)
        root.rotation_euler.z = -.16
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 48
    scene.render.resolution_x = 1500
    scene.render.resolution_y = 850
    scene.render.resolution_percentage = 100
    scene.world.color = (.085, .11, .14)
    scene.view_settings.view_transform = 'AgX'
    bpy.ops.object.camera_add(location=(1.2, -18, 4.2))
    camera = bpy.context.object
    camera.rotation_euler = (Vector((0, 0, 0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 11.6
    scene.camera = camera
    for location, energy, color, size in [((-4, -5, 6), 1300, (1, .94, .84), 5),
                                         ((4, -3, 2), 900, (.57, .76, 1), 4),
                                         ((0, 3, 4), 1400, (.54, .72, 1), 4)]:
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


if __name__ == "__main__":
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    groups = [make_phone()] + [make_app(index) for index in range(len(COLORS))]
    bpy.context.view_layer.update()
    report = validate(groups)
    export(groups)
    print(json.dumps({"output": str(OUTPUT), "bytes": OUTPUT.stat().st_size, "geometry": report}, indent=2))
    preview(groups)
