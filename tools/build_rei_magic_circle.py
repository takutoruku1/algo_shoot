import argparse
import json
import math
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets/source/rei_magic_circle"
OUTPUT = ROOT / "assets/fx/rei_magic_circle"
PREVIEW = ROOT / "build/qa_story/rei_magic_circle_20261030"
TAU = math.tau


def xyz(point):
    return (point[0], -point[2], point[1])


def polar(radius, angle, depth=0):
    return (radius * math.sin(angle), radius * math.cos(angle), depth)


def local(points, angle, radius=0, depth=0):
    return [(x * math.cos(angle) + (y + radius) * math.sin(angle),
             -x * math.sin(angle) + (y + radius) * math.cos(angle), depth)
            for x, y in points]


def material(name, color, emission=0, metallic=0):
    result = bpy.data.materials.new(name)
    result.diffuse_color = (*color, 1)
    shader = result.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = .3
    shader.inputs["Emission Color"].default_value = (*color, 1)
    shader.inputs["Emission Strength"].default_value = emission
    return result


class Geometry:
    def __init__(self):
        self.batches = {}

    def add(self, parent, mat, vertices, faces):
        vertices_out, faces_out = self.batches.setdefault((parent, mat), ([], []))
        offset = len(vertices_out)
        vertices_out.extend(xyz(v) for v in vertices)
        faces_out.extend(tuple(offset + i for i in face) for face in faces)

    def line(self, parent, mat, points, width, height=None, closed=False):
        height = height or width * .6
        half = width / 2
        profile = [(-half * .7, height / 2), (half * .7, height / 2),
                   (half, height * .15), (half, -height * .15),
                   (half * .7, -height / 2), (-half * .7, -height / 2),
                   (-half, -height * .15), (-half, height * .15)]
        vertices, faces = [], []
        count = len(points)
        for i, point in enumerate(points):
            before = Vector(points[(i - 1) % count] if closed or i else points[i])
            after = Vector(points[(i + 1) % count] if closed or i < count - 1 else points[i])
            tangent = (after - before).normalized()
            normal = Vector((-tangent.y, tangent.x, 0))
            for across, front in profile:
                vertices.append(tuple(Vector(point) + normal * across + Vector((0, 0, front))))
        for i in range(count if closed else count - 1):
            nxt = (i + 1) % count
            for j in range(8):
                faces.append((i * 8 + j, nxt * 8 + j, nxt * 8 + (j + 1) % 8,
                              i * 8 + (j + 1) % 8))
        if not closed:
            faces.extend([tuple(reversed(range(8))), tuple((count - 1) * 8 + i for i in range(8))])
        self.add(parent, mat, vertices, faces)

    def arc(self, parent, mat, radius, start, end, depth, width, height=None):
        segments = max(3, math.ceil(abs(end - start) / TAU * 200))
        closed = abs(end - start - TAU) < .00001
        points = [polar(radius, start + (end - start) * i / segments, depth)
                  for i in range(segments if closed else segments + 1)]
        self.line(parent, mat, points, width, height, closed)

    def star(self, parent, mat, radius, inner, depth, angle=0, width=.04, center=(0, 0)):
        points = [polar(radius if i % 2 == 0 else inner, angle + TAU * i / 10, depth)
                  for i in range(10)]
        points = [(x + center[0], y + center[1], z) for x, y, z in points]
        self.line(parent, mat, points, width, width * .75, True)

    def gem(self, parent, mat, center, size, angle=0, star=False):
        count = 10 if star else 4
        outline = []
        for i in range(count):
            radius = size * (.46 if star and i % 2 else 1)
            a = angle + TAU * i / count
            outline.append((center[0] + radius * math.sin(a),
                            center[1] + radius * math.cos(a), center[2]))
        vertices = [tuple(Vector(center) + Vector((0, 0, size * .34))), *outline,
                    tuple(Vector(center) - Vector((0, 0, size * .22)))]
        faces = []
        for i in range(count):
            faces.extend([(0, (i + 1) % count + 1, i + 1),
                          (count + 1, i + 1, (i + 1) % count + 1)])
        self.add(parent, mat, vertices, faces)

    def finish(self):
        result = []
        for (parent, mat), (vertices, faces) in self.batches.items():
            name = parent.name + "_" + mat.name
            data = bpy.data.meshes.new(name)
            data.from_pydata(vertices, [], faces)
            assert not data.validate(), name
            data.update()
            data.calc_loop_triangles()
            assert all(poly.area > 1e-10 for poly in data.polygons), name
            obj = bpy.data.objects.new(name, data)
            bpy.context.collection.objects.link(obj)
            obj.parent = parent
            data.materials.append(mat)
            result.append(obj)
        return result


def empty(name, parent=None):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    obj.empty_display_type = 'PLAIN_AXES'
    obj.empty_display_size = .3
    return obj


def build():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    gold = material("StarlightGold", (.72, .39, .085), .7, .72)
    violet = material("VioletLight", (.19, .025, .75), 2.5)
    pearl = material("LavenderLight", (.62, .38, 1), 2.3)
    cyan = material("EchoLight", (.16, .62, .86), 1.8)
    ink = material("AmethystMetal", (.065, .018, .16), .16, .68)
    root = empty("ReiMagicCircle")
    root["motif"] = "Rei / five-pointed stars / quotation / echo"
    root["front_axis_gltf"] = "+Z"
    root["loop_seconds"] = 8.0
    crest = empty("StarCrown", root)
    orbit = empty("OrbitRing", root)
    echo = empty("QuotationRing", root)
    core = empty("StarSigil", root)
    halo = empty("InnerEcho", root)
    g = Geometry()

    g.arc(crest, gold, 3.32, 0, TAU, -.08, .043)
    g.arc(crest, pearl, 3.27, 0, TAU, -.065, .018)
    g.arc(crest, ink, 3.39, 0, TAU, -.1, .07, .065)
    for i in range(5):
        a = i * TAU / 5
        center = polar(3.49, a, .08)
        g.gem(crest, gold, center, .205, a, star=True)
        g.star(crest, pearl, .31, .155, .04, a, .019, center[:2])
        points = [(-.20, .05), (-.30, .21), (0, .53), (.30, .21), (.20, .05)]
        g.line(crest, gold, local(points, a, 3.45, -.02), .031)
        g.line(crest, violet, local([(-.35, -.1), (-.46, .18), (0, .75),
                                    (.46, .18), (.35, -.1)], a, 3.45, -.13), .026)
        g.line(crest, pearl, local([(0, .55), (0, .94)], a, 3.45, -.14), .018)
        for sign in (-1, 1):
            g.line(crest, gold, local([(sign * .31, .23), (sign * .39, .37),
                                      (sign * .14, .66)], a, 3.45, -.17), .017)

    for i in range(10):
        a = i * TAU / 10
        g.arc(orbit, violet, 3.87, a + .085, a + TAU / 10 - .085, -.36, .045)
        g.arc(orbit, gold, 3.96, a + .15, a + TAU / 10 - .10, -.38, .017)
        g.line(orbit, pearl, [polar(3.8, a + .10, -.34), polar(3.87, a + .085, -.34),
                              polar(3.94, a + .10, -.34)], .021)
        g.gem(orbit, cyan, polar(3.87, a, -.35), .067, a)
    for i in range(100):
        a = i * TAU / 100
        long = i % 5 == 0
        g.line(echo, gold if long else pearl,
               [polar(3.02, a, .02), polar(3.16 if long else 3.09, a, .02)],
               .022 if long else .011)
    g.arc(echo, gold, 2.98, 0, TAU, .015, .021)
    g.arc(echo, violet, 2.43, 0, TAU, .04, .033)
    g.arc(echo, pearl, 2.38, 0, TAU, .06, .014)
    for i in range(10):
        a = i * TAU / 10
        # Paired quotation strokes echo Rei's existing quotation-pin projectile.
        for offset in (-.10, .09):
            quote = [(offset - .055, .095), (offset + .055, .095),
                     (offset + .055, -.025), (offset - .025, -.12)]
            g.line(echo, pearl, local(quote, a, 2.70, .075), .037, .026)
        for sign in (-1, 1):
            g.line(echo, gold, local([(sign * .25, .16), (sign * .32, 0),
                                     (sign * .25, -.16)], a, 2.70, .04), .018)
        g.gem(echo, cyan, polar(2.70, a + TAU / 20, .06), .051, a)
        g.line(echo, gold, [polar(2.55, a + TAU / 20, .035),
                            polar(2.61, a + TAU / 20, .035)], .016)
        g.line(echo, gold, [polar(2.79, a + TAU / 20, .035),
                            polar(2.85, a + TAU / 20, .035)], .016)

    g.arc(halo, violet, 2.13, 0, TAU, -.25, .023)
    g.arc(halo, gold, 2.19, 0, TAU, -.24, .013)
    for i in range(5):
        a = i * TAU / 5
        g.arc(halo, pearl, 1.96, a + .18, a + TAU / 5 - .18, -.2, .023)
        g.line(halo, violet, local([(-.53, -.20), (-.28, .13), (0, .27),
                                   (.28, .13), (.53, -.20)], a, 1.87, -.22), .021)
        g.gem(halo, gold, polar(2.15, a, -.20), .09, a)
        for sign in (-1, 1):
            g.line(halo, gold, local([(sign * .12, -.05), (sign * .22, -.20),
                                     (sign * .22, -.43)], a, 1.87, -.22), .016)

    g.star(core, ink, 1.82, .89, .27, width=.10)
    g.star(core, gold, 1.82, .89, .31, width=.059)
    g.star(core, pearl, 1.69, .82, .33, width=.022)
    g.star(core, violet, 1.48, .72, .26, width=.022)
    g.arc(core, gold, .53, 0, TAU, .37, .023)
    g.arc(core, pearl, .60, 0, TAU, .34, .016)
    g.gem(core, gold, (0, 0, .41), .41, star=True)
    g.star(core, pearl, .46, .21, .37, width=.014)
    for i in range(5):
        a = i * TAU / 5
        g.line(core, gold, [polar(.65, a, .3), polar(1.36, a, .3)], .017)
        g.gem(core, pearl, polar(1.03, a, .32), .062, a)
        g.line(core, pearl, local([(-.12, 0), (0, .16), (.12, 0)], a, 1.30, .34), .019)
        g.line(core, gold, [polar(.98, a + TAU / 10, .28),
                            polar(1.57, a + TAU / 10, .28)], .017)
        g.gem(core, violet, polar(1.65, a + TAU / 10, .29), .09, a)

    meshes = g.finish()
    scene = bpy.context.scene
    scene.render.fps = 30
    scene.frame_start = 1
    scene.frame_end = 241
    # Repeated sectors match at the loop boundary without rapidly spinning a full turn.
    for obj, degrees in ((orbit, 36), (echo, -36), (core, 72), (halo, -72)):
        for frame, angle in ((1, 0), (241, math.radians(degrees))):
            obj.rotation_euler.y = angle
            obj.keyframe_insert(data_path='rotation_euler', index=1, frame=frame)
        action = obj.animation_data.action
        action.name = obj.name + "_Orbit"
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for curve in bag.fcurves:
                        for key in curve.keyframe_points:
                            key.interpolation = 'LINEAR'
        obj["repeat_sectors"] = 10 if obj in (orbit, echo) else 5
        obj["loop_rotation_degrees"] = degrees
    scene.frame_set(1)
    return root, meshes


def camera(name, location, target, ortho):
    data = bpy.data.cameras.new(name)
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.location = xyz(location)
    obj.rotation_euler = (Vector(xyz(target)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    data.type = 'ORTHO'
    data.ortho_scale = ortho
    data.lens = 50
    return obj


def setup_render():
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.image_settings.color_depth = '8'
    scene.render.film_transparent = True
    scene.view_settings.view_transform = 'AgX'
    scene.world.color = (.05, .035, .08)
    for name, location, energy, color, size in (
        ("PearlKey", (1, 4, 7), 650, (.8, .77, 1), 7),
        ("GoldRim", (-5, 1, 3), 450, (1, .68, .35), 5),
        ("VioletFill", (4, -2, 4), 500, (.45, .28, 1), 5),
    ):
        data = bpy.data.lights.new(name, 'AREA')
        data.energy, data.color, data.shape, data.size = energy, color, 'DISK', size
        obj = bpy.data.objects.new(name, data)
        bpy.context.collection.objects.link(obj)
        obj.location = xyz(location)
        obj.rotation_euler = (-obj.location).to_track_quat('-Z', 'Y').to_euler()
    front = camera("Front_Transparent", (0, 0, 14), (0, 0, 0), 9.75)
    hero = camera("Perspective_Preview", (6, -6.5, 13), (0, 0, 0), 10.4)
    scene.camera = front
    tree = bpy.data.node_groups.new("Rei_Starlight_Compositor", 'CompositorNodeTree')
    scene.compositing_node_group = tree
    tree.interface.new_socket(name="Image", in_out='OUTPUT', socket_type='NodeSocketColor')
    render = tree.nodes.new('CompositorNodeRLayers')
    glow = tree.nodes.new('CompositorNodeGlare')
    glow.inputs['Type'].default_value = 'Fog Glow'
    glow.inputs['Quality'].default_value = 'High'
    glow.inputs['Threshold'].default_value = .6
    glow.inputs['Strength'].default_value = .65
    glow.inputs['Size'].default_value = .2
    separate = tree.nodes.new('CompositorNodeSeparateColor')
    peak = tree.nodes.new('ShaderNodeMath')
    peak.operation = 'MAXIMUM'
    peak2 = tree.nodes.new('ShaderNodeMath')
    peak2.operation = 'MAXIMUM'
    alpha = tree.nodes.new('ShaderNodeMath')
    alpha.operation = 'MAXIMUM'
    alpha.use_clamp = True
    falloff = tree.nodes.new('ShaderNodeMath')
    falloff.name = 'GlowFalloff'
    falloff.operation = 'POWER'
    falloff.inputs[1].default_value = .6
    clear = tree.nodes.new('ShaderNodeMath')
    clear.name = 'ClearTransparentBackground'
    clear.operation = 'SUBTRACT'
    clear.inputs[1].default_value = .018
    clear.use_clamp = True
    combine = tree.nodes.new('CompositorNodeSetAlpha')
    combine.inputs['Type'].default_value = 'Replace Alpha'
    output = tree.nodes.new('NodeGroupOutput')
    tree.links.new(render.outputs['Image'], glow.inputs['Image'])
    tree.links.new(glow.outputs['Glare'], separate.inputs['Image'])
    tree.links.new(separate.outputs['Red'], peak.inputs[0])
    tree.links.new(separate.outputs['Green'], peak.inputs[1])
    tree.links.new(peak.outputs[0], peak2.inputs[0])
    tree.links.new(separate.outputs['Blue'], peak2.inputs[1])
    tree.links.new(peak2.outputs[0], falloff.inputs[0])
    tree.links.new(falloff.outputs[0], clear.inputs[0])
    tree.links.new(clear.outputs[0], alpha.inputs[0])
    tree.links.new(render.outputs['Alpha'], alpha.inputs[1])
    tree.links.new(glow.outputs['Image'], combine.inputs['Image'])
    tree.links.new(alpha.outputs[0], combine.inputs['Alpha'])
    tree.links.new(combine.outputs['Image'], output.inputs['Image'])
    render.location, glow.location, output.location = (-280, 0), (0, 0), (260, 0)
    return front, hero


def render(path, resolution=1536):
    scene = bpy.context.scene
    scene.render.resolution_x = resolution
    scene.render.resolution_y = resolution
    scene.render.resolution_percentage = 100
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def export(root, meshes):
    bpy.context.scene.name = 'ReiCircle_Loop'
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    for obj in root.children_recursive:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    path = OUTPUT / "rei_magic_circle.glb"
    bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB', use_selection=True,
                             export_yup=True, export_cameras=False, export_lights=False,
                             export_texcoords=False, export_extras=True,
                             export_animation_mode='SCENE', export_force_sampling=True,
                             export_anim_slide_to_zero=True,
                             export_anim_scene_split_object=False,
                             export_nla_strips_merged_animation_name='ReiCircle_Loop')
    data = path.read_bytes()
    assert data[:4] == b'glTF'
    length = struct.unpack_from('<I', data, 12)[0]
    gltf = json.loads(data[20:20 + length])
    assert len(gltf.get('animations', [])) == 1
    assert not gltf.get('images')
    assert all('uri' not in buffer for buffer in gltf['buffers'])
    assert len(gltf['materials']) <= 5
    report = {
        "asset": str(path.relative_to(ROOT)),
        "source": str((SOURCE / 'rei_magic_circle.blend').relative_to(ROOT)),
        "blender": bpy.app.version_string,
        "bytes": path.stat().st_size,
        "mesh_objects": len(meshes),
        "triangles": sum(len(obj.data.loop_triangles) for obj in meshes),
        "materials": [m['name'] for m in gltf['materials']],
        "animations": [a['name'] for a in gltf['animations']],
        "duration_seconds": 8,
        "loop": "Rotation by one repeated sector; endpoint silhouettes coincide",
        "front_axis": "+Z in GLB / -Y in Blender",
        "units": "meters; approximately 8.8 across, pivot at center",
        "external_dependencies": [],
        "collision": False,
        "lighting": "Emissive PBR; bloom is renderer-side, PNG has baked glow",
        "layers": [obj.name for obj in root.children],
    }
    (PREVIEW / "asset_report.json").write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('REI_MAGIC_CIRCLE_REPORT', json.dumps(report))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--animation', action='store_true')
    parser.add_argument('--frames', type=int, default=96)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    for path in (SOURCE, OUTPUT, PREVIEW):
        path.mkdir(parents=True, exist_ok=True)
    (SOURCE / '.gdignore').touch()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.context.preferences.filepaths.file_preview_type = 'NONE'
    root, meshes = build()
    front, hero = setup_render()
    bpy.data.orphans_purge(do_recursive=True)
    export(root, meshes)
    scene = bpy.context.scene
    scene.camera = hero
    for area in bpy.context.screen.areas:
        if area.type == 'VIEW_3D':
            area.spaces.active.region_3d.view_perspective = 'CAMERA'
            area.spaces.active.shading.type = 'MATERIAL'
    scene.render.resolution_x = scene.render.resolution_y = 1536
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'rei_magic_circle.blend'))
    scene.camera = front
    render(OUTPUT / 'rei_magic_circle.png', 2048)
    scene.camera = hero
    render(PREVIEW / 'rei_magic_circle_oblique.png')
    if args.animation:
        scene.render.engine = 'BLENDER_EEVEE'
        scene.eevee.taa_render_samples = 24
        frames = PREVIEW / 'frames'
        frames.mkdir(exist_ok=True)
        for index in range(args.frames):
            frame = 1 + index * 240 / args.frames
            scene.frame_set(int(frame), subframe=frame % 1)
            render(frames / f'{index:04d}.png', 720)
    print('REI_MAGIC_CIRCLE_DONE')


if __name__ == '__main__':
    main()
