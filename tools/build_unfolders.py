import argparse
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'char/ui/blender'
SOURCE = ROOT / 'assets/source/unfolders'
QA = ROOT / 'build/qa_story/unfolder_blender_20261030'
ROTATION_QA = ROOT / 'build/qa_story/unfolder_rotation_20261030'
ROTATION_FRAMES = 72
ROTATION_SIZE = 128
ROTATION_COLUMNS = 8


def linear(hex_color):
    values = [int(hex_color[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in values)


class Art:
    def mat(self, name, color, metal=0, rough=.4, emit=0):
        material = bpy.data.materials.new(name)
        node = material.node_tree.nodes.get('Principled BSDF')
        rgb = (*linear(color), 1)
        node.inputs['Base Color'].default_value = rgb
        node.inputs['Metallic'].default_value = metal
        node.inputs['Roughness'].default_value = rough
        node.inputs['Coat Weight'].default_value = .24
        if emit:
            node.inputs['Emission Color'].default_value = rgb
            node.inputs['Emission Strength'].default_value = emit
        return material

    def mesh(self, name, vertices, faces, material, bevel=0):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(vertices, [], faces)
        mesh.update()
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.data.materials.append(material)
        if bevel:
            mod = obj.modifiers.new('Machined edge', 'BEVEL')
            mod.width, mod.segments = bevel, 3
            mod = obj.modifiers.new('Face normals', 'WEIGHTED_NORMAL')
            mod.keep_sharp = True
        return obj

    def prism(self, name, points, z, depth, material, bevel=.03):
        n = len(points)
        vertices = [(x, y, z + dz) for dz in (-depth / 2, depth / 2) for x, y in points]
        faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
        faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
        return self.mesh(name, vertices, faces, material, bevel)

    def facet(self, name, points, z, depth, material):
        n = len(points)
        cx, cy = sum(p[0] for p in points) / n, sum(p[1] for p in points) / n
        vertices = [(x, y, z) for x, y in points] + [(cx, cy, z + depth)]
        return self.mesh(name, vertices, [(i, (i + 1) % n, n) for i in range(n)] + [tuple(reversed(range(n)))], material)

    def ring(self, name, outer, inner, z, depth, material, bevel=.02):
        n = len(outer)
        vertices = [(x, y, z + dz) for dz in (-depth / 2, depth / 2) for loop in (outer, inner) for x, y in loop]
        faces = []
        for i in range(n):
            j = (i + 1) % n
            faces += [(i, j, j + 2 * n, i + 2 * n), (i + n, i + 3 * n, j + 3 * n, j + n),
                      (i + 2 * n, j + 2 * n, j + 3 * n, i + 3 * n), (i, i + n, j + n, j)]
        return self.mesh(name, vertices, faces, material, bevel)

    def tube(self, name, points, radius, material, closed=False):
        curve = bpy.data.curves.new(name, 'CURVE')
        curve.dimensions, curve.resolution_u = '3D', 16
        curve.bevel_depth, curve.bevel_resolution = radius, 4
        spline = curve.splines.new('POLY')
        spline.points.add(len(points) - 1)
        for vertex, position in zip(spline.points, points):
            vertex.co = (*position, 1)
        spline.use_cyclic_u = closed
        obj = bpy.data.objects.new(name, curve)
        bpy.context.collection.objects.link(obj)
        obj.data.materials.append(material)
        return obj

    def ellipsoid(self, name, center, scale, material):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, location=center)
        obj = bpy.context.object
        obj.name, obj.scale = name, scale
        obj.data.materials.append(material)
        for face in obj.data.polygons:
            face.use_smooth = True
        return obj


def scaled(points, scale, offset=(0, 0)):
    return [(x * scale + offset[0], y * scale + offset[1]) for x, y in points]


def diamond(x, y, width, height):
    return [(x, y + height), (x - width, y), (x, y - height), (x + width, y)]


def build_akari(api):
    gold = api.mat('Champagne gold edging', 'C99A51', .72, .28)
    light_gold = api.mat('Polished folded edges', 'F3D8A4', .6, .24)
    ivory = api.mat('Ivory enamel letter', 'E8DFC8', .12, .3)
    shadow = api.mat('Recessed oxblood ink', '300D22', .25, .32)
    ruby = api.mat('Split ruby seal', 'BA2249', .35, .24)
    teal = api.mat('Teal inlay', '54969A', .6, .25)
    frame = [(0, 1.82), (-.36, 1.1), (-1.23, .86), (-1.28, -.62),
             (-.42, -1.18), (0, -1.85), (.42, -1.18), (1.28, -.62), (1.23, .86), (.36, 1.1)]
    api.prism('Dark structural backing', scaled(frame, .87), -.15, .22, shadow)
    api.ring('Continuous raised gold shield', frame, scaled(frame, .84), .04, .30, gold)
    for i, a in enumerate(frame):
        b = frame[(i + 1) % len(frame)]
        api.facet('Cut gold perimeter facet ' + str(i),
                  [a, b, (b[0] * .87, b[1] * .87), (a[0] * .87, a[1] * .87)],
                  .20, .10, light_gold if i % 3 == 0 else gold)
    for sign in (-1, 1):
        points = [(sign * .85, .94), (sign * 1.40, 1.19), (sign * 1.78, .86),
                  (sign * 1.38, .68), (sign * 1.62, .03), (sign * 1.87, -.49),
                  (sign * 1.63, -.34), (sign * 1.19, .46)]
        api.prism('Folded ivory side wing', points, -.03, .14, ivory)
        api.tube('Gold side wing fold', [(sign * 1.05, .92, .12), (sign * 1.45, .89, .18),
                                        (sign * 1.37, .58, .14), (sign * 1.72, -.24, .07)], .055, gold)
        api.tube('Teal wing seam', [(sign * 1.21, .79, .19), (sign * 1.46, .76, .19)], .023, teal)
    envelope = [(-1.02, -.65), (1.02, -.65), (1.02, .73), (-1.02, .73)]
    api.prism('Envelope gold side wall', envelope, .18, .24, light_gold, .045)
    api.prism('Deep split in letter', scaled(envelope, .91), .33, .10, shadow, .02)
    panels = [([(-.94, .66), (.90, .66), (-.08, -.05)], .41),
              ([(-.94, .59), (-.23, -.10), (-.94, -.55)], .36),
              ([(.94, .50), (.19, -.11), (.94, -.53)], .40),
              ([(-.88, -.58), (-.15, -.06), (.10, -.22), (.87, -.58)], .37)]
    for index, (points, z) in enumerate(panels):
        api.prism('Separate enamel envelope flap ' + str(index), points, z, .11, ivory, .025)
    crack = [(-.66, -.62), (-.06, -.09), (.07, .10), (.55, .41), (.74, .65), (.54, .37), (.15, -.18), (-.40, -.62)]
    api.prism('Ruby ink visible inside the split', crack, .43, .05, ruby, .008)
    for y, height in ((1.38, .30), (-1.42, .28)):
        api.facet('Pointed gold end cap', diamond(0, y, .15, height), .24, .16, light_gold)
        api.facet('Teal end cap inlay', diamond(0, y, .065, height * .65), .39, .03, teal)
    api.facet('Central ruby seal', diamond(.02, -.11, .14, .19), .53, .09, ruby)


def heart():
    return [(16 * math.sin(t) ** 3 / 16,
             (13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)) / 16 + .12)
            for t in [2 * math.pi * i / 64 for i in range(64)]]


def build_mina(api):
    steel = api.mat('Polished silver heart frame', 'C6D7E0', .75, .23)
    bright = api.mat('Ceramic white folded lace', 'E5E6F4', .1, .32)
    dark = api.mat('Black violet protective armor', '171829', .48, .34)
    blue = api.mat('Glacial blue crystal', '258CC6', .38, .21, .08)
    pale = api.mat('Ice facet highlight', 'A1EEFF', .3, .24, .10)
    navy = api.mat('Deep sapphire cut facets', '17598C', .45, .24)
    contour = heart()
    for i in range(32):
        a, b = contour[i * 2], contour[(i * 2 + 2) % 64]
        points = [(a[0] * 1.45, a[1] * 1.45), (b[0] * 1.45, b[1] * 1.45),
                  (b[0] * 1.15, b[1] * 1.15), (a[0] * 1.15, a[1] * 1.15)]
        api.facet('Pleated white lace ' + str(i), points, -.10, .11 if i % 2 else .18, bright)
    api.prism('Black heart cavity', scaled(contour, 1.20), -.03, .30, dark, .03)
    api.ring('Silver heart retaining frame', scaled(contour, 1.22), scaled(contour, 1.04), .13, .24, steel)
    # Each half keeps a separate faceted mesh, leaving the fracture visibly recessed.
    left = [(-.06, .69), (-.28, .97), (-.61, 1.02), (-.91, .77), (-1.0, .42),
            (-.84, .03), (-.46, -.48), (-.05, -.96), (-.17, -.43), (-.02, -.17), (-.16, .12), (.02, .39)]
    right = [(.16, .62), (.37, .94), (.69, .95), (.94, .64), (.93, .26),
             (.65, -.16), (.17, -.78), (.02, -.96), (.14, -.43), (.30, -.16), (.12, .14), (.26, .40)]
    for half, points in enumerate((left, right)):
        center = (-.45 if half == 0 else .52, .34)
        for i, p in enumerate(points):
            q = points[(i + 1) % len(points)]
            material = (blue, pale, navy, blue, pale)[i % 5]
            api.mesh('Crystal heart cut ' + str(half) + '_' + str(i),
                     [(p[0], p[1], .28), (q[0], q[1], .28), (*center, .62)], [(0, 1, 2)], material)
    for sign in (-1, 1):
        for i, y in enumerate((.71, .13, -.38)):
            x = sign * (1.02 - .12 * i)
            points = [(x, y + .24), (x + sign * .51, y + .11), (x + sign * .35, y - .36), (x - sign * .14, y - .17)]
            api.prism('Folded dark side armor', points, .22, .15, dark)
            api.facet('Silver thorn facet', [(x, y + .21), (x + sign * .33, y + .05), (x - sign * .1, y - .13)], .34, .15, steel)
        api.prism('Black trailing ribbon', [(sign * 1.10, .14), (sign * 1.42, .03),
                  (sign * 1.90, -.83), (sign * 1.56, -.60), (sign * 1.52, -.98)], -.12, .10, dark)
        api.facet('Blue side clasp', diamond(sign * 1.21, .57, .12, .19), .45, .12, pale)
    for y, h in ((1.43, .43), (-1.35, .42)):
        api.facet('Silver heart crest', diamond(0, y, .22, h), .16, .19, steel)
        api.facet('Black crest recess', diamond(0, y, .10, h * .64), .36, .04, dark)


def bubble_outline():
    points = []
    for cx, cy, start in ((.56, .58, 0), (-.56, .58, 90), (-.56, -.50, 180), (.56, -.50, 270)):
        for i in range(9):
            a = math.radians(start + i * 90 / 8)
            points.append((cx + .87 * math.cos(a), cy + .87 * math.sin(a)))
    points[23:27] = [(-1.48, -1.63), (-.64, -1.37)]
    return points


def build_panel(api, kind):
    accent = {'akari': 'E2B97A', 'koharu': '8CD7C6', 'rei': 'C49CE9', 'final': '9BCBE4'}[kind]
    trim = api.mat('Satin silver with character accent', accent, .62, .29)
    side = api.mat('Dark titanium side wall', '283348', .7, .33)
    face = api.mat('Black blue ceramic face', '11172D', .18, .3)
    white = api.mat('Raised pearl symbol', 'EFF3F4', .08, .3, .28)
    outline = bubble_outline()
    api.prism('Thick speech shield housing', outline, -.03, .40, side, .08)
    api.prism('Recessed ceramic face', scaled(outline, .91), .22, .07, face, .045)
    api.ring('Beveled silver shield rim', outline, scaled(outline, .88), .21, .18, trim, .035)
    if kind == 'akari':
        api.prism('Raised folded paper plane', [(-.83, .38), (.88, .86), (.42, -.77), (.02, -.28), (-.19, .02)], .39, .11, white)
        api.tube('Plane division', [(-.25, -.12, .49), (.72, .72, .49)], .055, face)
        api.tube('Unsent message slash', [(-.69, -.79, .52), (.92, .97, .52)], .105, white)
    elif kind == 'koharu':
        eye = [(x, .12 + sign * .48 * math.sin(math.pi * (x + .94) / 1.88))
               for sign, indices in ((1, range(17)), (-1, range(16, -1, -1)))
               for i in indices for x in [-.94 + 1.88 * i / 16]]
        api.prism('Raised open eye', eye, .40, .13, white, .035)
        api.ellipsoid('Dark eye pupil', (0, .12, .49), (.34, .37, .08), face)
    elif kind == 'rei':
        points = [(-.85, .75), (.38, .75), (.56, .62), (.50, .47), (.67, .36), (.65, .15),
                  (.55, .08), (.67, -.04), (.58, -.28), (.17, -.30), (.22, -.71), (.07, -.93),
                  (-.12, -.92), (-.45, -.32), (-.73, -.29), (-.85, -.10)]
        api.prism('Raised disapproval hand', points, .41, .15, white, .065)
    else:
        for x in (-.65, 0, .65):
            api.ellipsoid('Raised silent dot', (x, .02, .40), (.23, .23, .13), white)


def setup(api, reference):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = scene.render.resolution_y = 256
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.film_transparent = True
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium High Contrast'
    world = bpy.data.worlds.new('Cool studio ambient')
    scene.world = world
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (.18, .22, .29, 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = .5
    for name, position, energy, size, color in [
            ('Large warm key', (-3.5, 4.5, 6), 650, 4, (1, .86, .72)),
            ('Cool broad fill', (4, .5, 4), 300, 3.5, (.55, .76, 1)),
            ('Crisp upper rim', (-1, 3, 1), 470, 2, (.8, .9, 1)),
            ('Lower silver reflection', (1, -4, 3), 160, 3, (.68, .74, 1))]:
        data = bpy.data.lights.new(name, 'AREA')
        data.energy, data.shape, data.size, data.color = energy, 'DISK', size, color
        obj = bpy.data.objects.new(name, data)
        bpy.context.collection.objects.link(obj)
        obj.location = position
        obj.rotation_euler = (-Vector(position)).to_track_quat('-Z', 'Y').to_euler()
    data = bpy.data.cameras.new('Near frontal artifact camera')
    obj = bpy.data.objects.new('Camera', data)
    bpy.context.collection.objects.link(obj)
    obj.location = (.28, -.55, 9)
    obj.rotation_euler = (math.atan2(.55, 9), math.atan2(.28, 9), 0)
    data.type, data.ortho_scale = 'ORTHO', 3.35 if reference.name.startswith('panel_') else 4.05
    scene.camera = obj
    image = bpy.data.images.load(str(reference))
    image.pack()
    collection = bpy.data.collections.new('Original artwork reference')
    scene.collection.children.link(collection)
    ref = bpy.data.objects.new('Reference artwork (not rendered)', None)
    ref.empty_display_type = 'IMAGE'
    ref.data, ref.empty_display_size = image, 4.35
    ref.location = (5, 0, 0)
    ref.hide_render = True
    collection.objects.link(ref)
    scene['Original artwork'] = str(reference.relative_to(ROOT))
    scene['Game rendering'] = 'Transparent sprite; motion, collision radius and shield durability unchanged'


def build(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    api = Art()
    if name.startswith('panel_'):
        reference = ROOT / f'char/v3/{name}.png'
        build_panel(api, name.removeprefix('panel_'))
    else:
        reference = ROOT / f'char/ui/{name}_v1.png'
        kind = name.removeprefix('unfolder_')
        if kind in ('koharu', 'rei'):
            from unfolder_blender_forms import build_koharu, build_rei
            (build_koharu if kind == 'koharu' else build_rei)(api)
        else:
            (build_akari if kind == 'akari' else build_mina)(api)
    setup(api, reference)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.context.preferences.filepaths.file_preview_type = 'NONE'
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / f'{name}.blend'))
    scene = bpy.context.scene
    scene.render.filepath = str(OUTPUT / f'{name}.png')
    bpy.ops.render.render(write_still=True)
    scene.render.resolution_x = scene.render.resolution_y = 1024
    scene.render.filepath = str(QA / f'{name}_detail.png')
    bpy.ops.render.render(write_still=True)
    report = {'name': name, 'blender': bpy.app.version_string, 'reference': str(reference.relative_to(ROOT)),
              'mesh_objects': sum(o.type == 'MESH' for o in scene.objects), 'texture_size': [256, 256]}
    (QA / f'{name}_geometry.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('UNFOLDER_COMPLETE ' + json.dumps(report), flush=True)


def build_back(api, name):
    kind = name.split('_', 1)[1]
    is_panel = name.startswith('panel_')
    accent = {'akari': 'BB405D', 'koharu': '82D9B1', 'rei': 'AC77E0',
              'mina': '59CAE9', 'final': '9BCBE4'}[kind]
    warm = kind in ('akari', 'rei')
    metal = api.mat('Rear machined retaining ribs', 'C5A776' if warm else 'A1B1C6', .72, .36)
    dark = api.mat('Rear recessed metal shell', '4D3540' if warm else '30364C', .64, .43)
    enamel = api.mat('Rear inset character enamel', accent, .28, .3, .12)
    recess = api.mat('Rear engraved fastening slots', '161C2A', .35, .43)
    if is_panel:
        outline = scaled(bubble_outline(), .82)
        ends = [(-.85, .80), (.85, .80), (-.85, -.70), (.85, -.70)]
    elif kind == 'akari':
        outline = scaled([(0, 1.82), (-.36, 1.1), (-1.23, .86), (-1.28, -.62),
                          (-.42, -1.18), (0, -1.85), (.42, -1.18), (1.28, -.62),
                          (1.23, .86), (.36, 1.1)], .75)
        ends = [(-.72, .59), (.72, .59), (-.68, -.57), (.68, -.57), (0, 1.12), (0, -1.13)]
    elif kind == 'koharu':
        outline = scaled([(0, 1.46), (-.64, 1.04), (-.93, .48), (-1, -.08),
                          (-.70, -.85), (0, -1.50), (.70, -.85), (1, -.08),
                          (.93, .48), (.64, 1.04)], .82)
        ends = [(-.56, .60), (.56, .60), (-.53, -.55), (.53, -.55), (0, 1.03), (0, -1.06)]
    elif kind == 'rei':
        outline = [(math.cos(a) * r, math.sin(a) * r)
                   for i in range(10) for a in [math.pi / 2 + i * math.pi / 5]
                   for r in [1.51 if i % 2 == 0 else .66]]
        ends = [(math.cos(a) * 1.15, math.sin(a) * 1.15)
                for a in [math.pi / 2 + i * math.tau / 5 for i in range(5)]]
    else:
        outline = scaled(heart(), 1.01)
        ends = [(-.56, .51), (.56, .51), (-.27, -.46), (.27, -.46)]
    api.prism('Solid stepped rear housing', outline, -.20, .16, dark, .045)
    api.ring('Rear perimeter retaining lip', outline, scaled(outline, .91), -.292, .06, metal, .015)
    for index, (x, y) in enumerate(ends):
        api.tube(f'Rear load bearing rib {index}',
                 [(x * .20, y * .20, -.302), (x * .65, y * .65, -.338), (x, y, -.302)],
                 .042, metal)
        api.ellipsoid(f'Rear rivet {index}', (x, y, -.324), (.065, .065, .045), metal)
        api.prism(f'Rear screw slot {index}', [(x - .036, y - .009), (x + .036, y - .009),
                                             (x + .036, y + .009), (x - .036, y + .009)],
                  -.368, .009, recess, .003)
    hub = [(math.cos(i * math.tau / 6) * .39, math.sin(i * math.tau / 6) * .39) for i in range(6)]
    api.prism('Rear opaque central medallion', hub, -.315, .09, dark, .015)
    api.ring('Rear medallion recessed bezel', hub, scaled(hub, .79), -.357, .065, metal, .012)
    if kind == 'akari':
        emblem = [(-.22, -.13), (.22, -.13), (.22, .15), (-.22, .15)]
        api.prism('Rear inset letter seal', emblem, -.367, .023, enamel, .012)
        api.tube('Rear seal folded line', [(-.19, .12, -.382), (0, -.025, -.382), (.19, .12, -.382)], .016, metal)
    elif kind == 'koharu':
        api.facet('Rear emerald lozenge inset', diamond(0, 0, .13, .26), -.359, -.025, enamel)
        api.tube('Rear emerald retaining spine', [(0, -.20, -.39), (0, .20, -.39)], .018, metal)
    elif kind == 'rei':
        emblem = [(math.cos(a) * r, math.sin(a) * r)
                  for i in range(10) for a in [math.pi / 2 + i * math.pi / 5]
                  for r in [.27 if i % 2 == 0 else .12]]
        api.facet('Rear inset amethyst maker star', emblem, -.359, -.025, enamel)
    elif kind == 'mina':
        api.facet('Rear inset glacial heart', scaled(heart(), .25), -.359, -.025, enamel)
    else:
        for x in (-.16, 0, .16):
            api.ellipsoid('Rear inset silence mark', (x, 0, -.366), (.045, .045, .018), enamel)


def build_rotation(name):
    import numpy as np

    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / f'{name}.blend'))
    scene = bpy.context.scene
    build_back(Art(), name)
    bpy.context.view_layer.update()
    pivot = bpy.data.objects.new('Unfolder full spatial rotation', None)
    scene.collection.objects.link(pivot)
    for obj in list(scene.objects):
        if obj.type in ('MESH', 'CURVE'):
            matrix = obj.matrix_world.copy()
            obj.parent = pivot
            obj.matrix_world = matrix
    pivot.rotation_mode = 'XYZ'
    for frame in range(ROTATION_FRAMES + 1):
        angle = math.tau * frame / ROTATION_FRAMES
        pivot.rotation_euler = (math.radians(12) * math.sin(2 * angle), angle, 0)
        pivot.keyframe_insert(data_path='rotation_euler', frame=frame + 1)
    scene.frame_start, scene.frame_end = 1, ROTATION_FRAMES
    scene.render.fps, scene.render.fps_base = 12, 1
    scene.render.resolution_x = scene.render.resolution_y = ROTATION_SIZE
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.image_settings.color_depth = '8'
    scene.render.film_transparent = True
    scene['Rotation atlas'] = f'{ROTATION_COLUMNS} columns, 9 rows, top-left origin, row-major'
    scene['Rotation animation'] = '72 frames / 6 seconds; Y 360 degrees, X 12 degrees sin(2 angle)'
    scene['Static source'] = str((SOURCE / f'{name}.blend').relative_to(ROOT))
    frames_dir = ROTATION_QA / 'frames' / name
    frames_dir.mkdir(parents=True, exist_ok=True)
    camera_inverse = scene.camera.matrix_world.inverted()
    extent = 0
    for frame in range(ROTATION_FRAMES):
        scene.frame_set(frame + 1)
        for obj in scene.objects:
            if obj.type in ('MESH', 'CURVE'):
                transform = camera_inverse @ obj.matrix_world
                for corner in obj.bound_box:
                    point = transform @ Vector(corner)
                    extent = max(extent, abs(point.x), abs(point.y))
    scene.camera.data.ortho_scale = max(scene.camera.data.ortho_scale, extent * 2 * ROTATION_SIZE / (ROTATION_SIZE - 8))
    scene.frame_set(1)
    scene.render.filepath = str(frames_dir / '000.png')
    bpy.context.preferences.filepaths.save_version = 0
    bpy.context.preferences.filepaths.file_preview_type = 'NONE'
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'rotation' / f'{name}.blend'))
    rows = ROTATION_FRAMES // ROTATION_COLUMNS
    atlas = np.zeros((ROTATION_SIZE * rows, ROTATION_SIZE * ROTATION_COLUMNS, 4), dtype=np.float32)
    for frame in range(ROTATION_FRAMES):
        scene.frame_set(frame + 1)
        scene.render.filepath = str(frames_dir / f'{frame:03d}.png')
        bpy.ops.render.render(write_still=True)
        rendered = bpy.data.images.load(scene.render.filepath, check_existing=False)
        pixels = np.empty(ROTATION_SIZE * ROTATION_SIZE * 4, dtype=np.float32)
        rendered.pixels.foreach_get(pixels)
        bpy.data.images.remove(rendered)
        x = frame % ROTATION_COLUMNS * ROTATION_SIZE
        y = (rows - 1 - frame // ROTATION_COLUMNS) * ROTATION_SIZE
        atlas[y:y + ROTATION_SIZE, x:x + ROTATION_SIZE] = pixels.reshape(ROTATION_SIZE, ROTATION_SIZE, 4)
        print(f'UNFOLDER_ROTATION_FRAME {name} {frame + 1}/{ROTATION_FRAMES}', flush=True)
    texture = bpy.data.images.new(name + '_rotation_atlas', width=atlas.shape[1], height=atlas.shape[0], alpha=True)
    texture.colorspace_settings.name = 'sRGB'
    texture.pixels.foreach_set(atlas.ravel())
    texture.filepath_raw = str(OUTPUT / 'rotation' / f'{name}.png')
    texture.file_format = 'PNG'
    texture.save()
    bpy.data.images.remove(texture)
    for frame in (0, 18, 36, 54):
        scene.frame_set(frame + 1)
        scene.render.resolution_x = scene.render.resolution_y = 256
        scene.render.filepath = str(ROTATION_QA / f'{name}_angle_{frame * 5:03d}.png')
        bpy.ops.render.render(write_still=True)
    scene.render.resolution_x = scene.render.resolution_y = ROTATION_SIZE
    scene.frame_set(1)
    report = {'name': name, 'blender': bpy.app.version_string, 'frames': ROTATION_FRAMES,
              'frame_size': ROTATION_SIZE, 'columns': ROTATION_COLUMNS, 'rows': rows,
              'fps': 12, 'seconds': 6, 'ortho_scale': scene.camera.data.ortho_scale,
              'mesh_objects': sum(o.type == 'MESH' for o in scene.objects),
              'atlas': str((OUTPUT / 'rotation' / f'{name}.png').relative_to(ROOT))}
    (ROTATION_QA / f'{name}_rotation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('UNFOLDER_ROTATION_COMPLETE ' + json.dumps(report), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--rotation', action='store_true')
    parser.add_argument('--only', nargs='+', default=['unfolder_akari', 'unfolder_koharu', 'unfolder_rei', 'unfolder_mina',
                                                     'panel_akari', 'panel_koharu', 'panel_rei', 'panel_final'])
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    for folder in (OUTPUT, SOURCE, QA):
        folder.mkdir(parents=True, exist_ok=True)
    (SOURCE / '.gdignore').touch()
    sys.path.insert(0, str(ROOT / 'tools'))
    if args.rotation:
        for folder in (OUTPUT / 'rotation', SOURCE / 'rotation', ROTATION_QA):
            folder.mkdir(parents=True, exist_ok=True)
    for name in args.only:
        (build_rotation if args.rotation else build)(name)
