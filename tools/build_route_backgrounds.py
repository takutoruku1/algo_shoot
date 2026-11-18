import argparse
import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/source/route_backgrounds'
OUTPUT = ROOT / 'char/bg2/route/blender'
QA = ROOT / 'build/qa_story/route_blender_20261029'
CHAPTERS = ['', '_passage', '_late', '_climax']


def rgb(value):
    c = [int(value[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in c)


class SceneArt:
    def __init__(self):
        self.collections = {}
        self.parent = None
        for name in ('far', 'mid', 'near', 'lighting'):
            collection = bpy.data.collections.new(name)
            bpy.context.scene.collection.children.link(collection)
            self.collections[name] = collection
        self.layer('mid')

    def layer(self, name):
        self.collection = self.collections[name]
        self.parent = None

    def mat(self, name, color, metallic=0, roughness=.4, emission=0):
        material = bpy.data.materials.new(name)
        shader = material.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Base Color'].default_value = (*rgb(color), 1)
        shader.inputs['Metallic'].default_value = metallic
        shader.inputs['Roughness'].default_value = roughness
        shader.inputs['Coat Weight'].default_value = .18
        shader.inputs['Emission Color'].default_value = (*rgb(color), 1)
        shader.inputs['Emission Strength'].default_value = emission
        if not emission:
            noise = material.node_tree.nodes.new('ShaderNodeTexNoise')
            noise.inputs['Scale'].default_value = 5.0
            noise.inputs['Detail'].default_value = 3.0
            bump = material.node_tree.nodes.new('ShaderNodeBump')
            bump.inputs['Strength'].default_value = .12
            bump.inputs['Distance'].default_value = .035
            material.node_tree.links.new(noise.outputs['Fac'], bump.inputs['Height'])
            material.node_tree.links.new(bump.outputs['Normal'], shader.inputs['Normal'])
        return material

    def finish(self, obj, material=None, bevel=0):
        for collection in list(obj.users_collection):
            collection.objects.unlink(obj)
        self.collection.objects.link(obj)
        obj.parent = self.parent
        if material:
            obj.data.materials.append(material)
        if bevel:
            modifier = obj.modifiers.new('Light-catching solid edges', 'BEVEL')
            modifier.width, modifier.segments = bevel, 2
            modifier.limit_method = 'ANGLE'
            obj.modifiers.new('Architectural face normals', 'WEIGHTED_NORMAL')
        return obj

    def box(self, name, loc, size, material, bevel=.04, rotation=(0, 0, 0)):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        obj = bpy.context.object
        obj.name, obj.scale = name, size
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        obj.rotation_euler = rotation
        return self.finish(obj, material, bevel)

    def mesh(self, name, verts, faces, material, bevel=0):
        data = bpy.data.meshes.new(name)
        data.from_pydata(verts, [], faces)
        data.update()
        obj = bpy.data.objects.new(name, data)
        self.collection.objects.link(obj)
        return self.finish(obj, material, bevel)

    def tube(self, name, points, radius, material):
        data = bpy.data.curves.new(name, 'CURVE')
        data.dimensions, data.bevel_depth, data.bevel_resolution = '3D', radius, 2
        spline = data.splines.new('POLY')
        spline.points.add(len(points) - 1)
        for item, point in zip(spline.points, points):
            item.co = (*point, 1)
        obj = bpy.data.objects.new(name, data)
        self.collection.objects.link(obj)
        return self.finish(obj, material)

    def sphere(self, name, loc, scale, material):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, location=loc)
        obj = bpy.context.object
        obj.name, obj.scale = name, scale
        for face in obj.data.polygons:
            face.use_smooth = True
        return self.finish(obj, material)

    def cylinder(self, name, loc, radius, depth, material, rotation=(0, 0, 0)):
        bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=radius, depth=depth, location=loc)
        obj = bpy.context.object
        obj.name, obj.rotation_euler = name, rotation
        return self.finish(obj, material, .025)

    def group(self, name, loc=(0, 0, 0), rotation=(0, 0, 0)):
        obj = bpy.data.objects.new(name, None)
        self.collection.objects.link(obj)
        obj.location, obj.rotation_euler, obj.parent = loc, rotation, self.parent
        return obj


def room(api, name, position, size, rotation, palettes):
    wall, trim, inside, glow, paper = palettes
    width, depth, height = size
    api.parent = None
    api.parent = api.group(name, position, rotation)
    api.box('Floating foundation', (0, 0, -height / 2), (width + .4, depth + .4, .5), wall, .1)
    api.box('Raised cornice', (0, 0, height / 2), (width + .55, depth + .5, .3), trim, .07)
    api.box('Roof slab', (0, 0, height / 2 + .25), (width + .25, depth + .25, .24), wall)
    api.box('Back wall', (0, depth / 2, 0), (width, .3, height), inside)
    api.box('Side wall', (width / 2, 0, 0), (.3, depth, height), wall)
    for x in (-width * .5, width * .5):
        api.box('Corner pilaster', (x, 0, 0), (.26, depth + .18, height + .1), trim, .04)
    for x in (-width * .37, -.07, width * .36):
        api.box('Facade lintel inset', (x, -depth / 2 - .04, height / 2 - .27),
                (width * .19, .11, .12), paper, .015)
    for z in (-height * .39, height * .39):
        api.box('Side facade seam', (width / 2 + .16, 0, z), (.04, depth - .22, .07), paper, .008)
    for x in (-width / 2, 0, width / 2):
        api.box('Window mullion', (x, -depth / 2, 0), (.18, .22, height), trim)
    for z in (-height / 2 + .25, height / 2 - .3):
        api.box('Window horizontal frame', (0, -depth / 2, z), (width, .24, .14), trim)
    for x in (-width * .25, width * .25):
        api.box('Warm recessed pane', (x, depth / 2 - .18, .2), (width * .35, .08, height * .61), glow, .01)
        api.box('Pane division', (x, depth / 2 - .25, .2), (.08, .1, height * .65), trim, .01)
        api.box('Window sill', (x, depth / 2 - .35, -height * .23), (width * .39, .35, .12), paper)
        api.box('Writing desk', (x, -.05, -height * .2), (width * .35, depth * .48, .13), paper)
        for dx in (-width * .13, width * .13):
            api.box('Desk leg', (x + dx, .12, -height * .37), (.09, .1, height * .34), trim, .01)
        api.box('Chair back', (x, .6, -height * .19), (width * .14, .15, height * .22), trim)
        api.box('Chair seat', (x, .4, -height * .33), (width * .16, .4, .12), trim)
        api.box('Desk writing sheet', (x + .14, -.22, -height * .2 + .09),
                (.46, .34, .035), paper, .008, rotation=(0, 0, .15))
        api.box('Desk letter tray', (x - .35, .15, -height * .2 + .14),
                (.4, .4, .13), trim, .025)
    for x in (-width * .27, width * .27):
        api.box('Ceiling recessed light', (x, -.1, height * .47), (.75, 1.1, .06), glow, .025)
    for x in (-width * .3, width * .3):
        api.box('Suspended structural rib', (x, 0, -height * .65), (.24, depth * .6, height * .35), trim)
    api.parent = None


def paper_bridge(api, name, x, y, z, length, material, underside, bend=0):
    vertices = []
    sections = 8
    for i in range(sections + 1):
        t = i / sections
        height = z + math.sin(t * math.pi) * bend + (.12 if i % 2 else -.12)
        vertices += [(x + length * t, y - .9, height - .55),
                     (x + length * t, y, height + .28), (x + length * t, y + .9, height + .55)]
    faces = []
    for i in range(sections):
        a = i * 3
        faces += [(a, a + 3, a + 4, a + 1), (a + 1, a + 4, a + 5, a + 2)]
    obj = api.mesh(name, vertices, faces, material)
    obj.data.materials.append(underside)
    solid = obj.modifiers.new('Folded paper thickness', 'SOLIDIFY')
    solid.thickness, solid.material_offset = .09, 1
    bevel = obj.modifiers.new('Paper edge', 'BEVEL')
    bevel.width, bevel.segments = .025, 2


def build_akari(api, chapter):
    wall = api.mat('Blue slate room shells', '647086', .28, .33)
    trim = api.mat('Dark structural steel', '394B62', .62, .3)
    inside = api.mat('Warm interior wood', '6D6155', .1, .5)
    glow = api.mat('Quiet amber windows', 'DCA96D', .1, .34, .35)
    paper = api.mat('Folded ivory correspondence', '969BA9', 0, .72)
    paper.node_tree.nodes.get('Principled BSDF').inputs['Coat Weight'].default_value = 0
    pal = wall, trim, inside, glow, paper
    rng = random.Random(77 + chapter)
    api.layer('mid')
    positions = [(-27, 7.8), (-14, -8.4), (-1, 9), (13, -8.3), (26, 8.1)]
    for i, (x, z) in enumerate(positions):
        turn = (-.31 if i % 2 else .34) * (1 + chapter * .15)
        room(api, 'Floating room ' + str(i), (x, 3 + i % 2 * 4, z),
             (6 + i % 2, 4.5, 4.3), (.09, (i % 2 * 2 - 1) * chapter * .035, turn), pal)
        paper_bridge(api, 'Folded room connection', x + 3, 1.5, z - 1.8, 8,
                     paper, trim, bend=(.3 + chapter * .35) * (-1 if i % 2 else 1))
    api.layer('near')
    for i, x in enumerate((-25, -8, 10, 27)):
        top = i % 2 == 0
        z = 9.1 if top else -9.2
        room(api, 'Near cutaway room ' + str(i), (x, -12, z), (9, 5.5, 5),
             (.16 if top else -.12, chapter * .03, -.33 if top else .29), pal)
        paper_bridge(api, 'Near folded overhang', x - 6, -13, 7.5 if top else -7.6,
                     10, paper, trim, bend=.2 + chapter * .15)
    if chapter >= 2:
        for i in range(7):
            api.box('Displaced room fragment', (-26 + i * 8, -8, (8.6 if i % 2 else -9.2)),
                    (1.9, .6, .35), wall, .03, rotation=(.2, .1, rng.uniform(-.4, .4)))


def ribbon(api, name, points, width, surface):
    verts = []
    for i, (x, y, z) in enumerate(points):
        tilt = math.sin(i * .7) * .32
        verts.extend([(x, y - tilt, z - width / 2), (x, y + tilt, z + width / 2)])
    obj = api.mesh(name, verts, [(i * 2, i * 2 + 1, i * 2 + 3, i * 2 + 2) for i in range(len(points) - 1)], surface)
    modifier = obj.modifiers.new('Woven ribbon thickness', 'SOLIDIFY')
    modifier.thickness = .045
    for face in obj.data.polygons:
        face.use_smooth = True


def mirror_panel(api, position, width, height, rotation, frame, glass, edge):
    api.parent = None
    api.parent = api.group('Tilted return mirror', position, rotation)
    api.box('Thick mirrored slab', (0, 0, 0), (width, .45, height), frame, .09)
    api.box('Inset mirror face', (0, -.26, 0), (width - .15, .065, height - .2), glass, .025)
    for x in (-width / 2 + .045, width / 2 - .045):
        api.box('Reflected bevel edge', (x, -.27, 0), (.055, .06, height - .1), edge, .015)
    for z in (-height / 2, height / 2):
        api.box('Mirror rim', (0, 0, z), (width, .52, .1), edge, .025)
    api.box('Floating panel foot', (0, .3, -height / 2 - .24), (width + .5, 2.1, .38), frame, .08)
    api.parent = None


def build_mina(api, chapter):
    frame = api.mat('Water worn silver blue', '3F6474', .72, .24)
    glass = api.mat('Deep mirror faces', '638C9C', .36, .5)
    shader = glass.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Coat Weight'].default_value = .02
    shader.inputs['Specular IOR Level'].default_value = .15
    reflection = glass.node_tree.nodes.new('ShaderNodeTexImage')
    reflection.image = bpy.data.images.load(str(ROOT / 'char/bg2/route' / f'mina{CHAPTERS[chapter]}_far.png'))
    coordinates = glass.node_tree.nodes.new('ShaderNodeTexCoord')
    separate = glass.node_tree.nodes.new('ShaderNodeSeparateXYZ')
    combine = glass.node_tree.nodes.new('ShaderNodeCombineXYZ')
    glass.node_tree.links.new(coordinates.outputs['Generated'], separate.inputs[0])
    glass.node_tree.links.new(separate.outputs['X'], combine.inputs['X'])
    glass.node_tree.links.new(separate.outputs['Z'], combine.inputs['Y'])
    glass.node_tree.links.new(combine.outputs[0], reflection.inputs['Vector'])
    glass.node_tree.links.new(reflection.outputs['Color'], shader.inputs['Base Color'])
    glass.node_tree.links.new(reflection.outputs['Color'], shader.inputs['Emission Color'])
    shader.inputs['Emission Strength'].default_value = .12
    edge = api.mat('Cold reflected edges', '9BB2BC', .72, .28)
    fabric = api.mat('White return ribbons', '839BAB', 0, .95)
    fabric.node_tree.nodes.get('Principled BSDF').inputs['Coat Weight'].default_value = 0
    dark = api.mat('Deep water platforms', '203F50', .58, .25)
    api.layer('mid')
    for i in range(13):
        x = -32 + i * 5.2
        height = 5.2 + (i % 3) * .7
        z = -8.4 - i % 2 * .5
        mirror_panel(api, (x, 3 + i % 3 * 2, z), 2.4 + (i % 2) * .5, height,
                     (.08, (.03 + chapter * .055) * (-1 if i % 2 else 1), .18 * (-1 if i % 3 else 1)), frame, glass, edge)
        if i % 3 == 0:
            mirror_panel(api, (x - 2.4, 7, 10.5), 2.2, 5.1, (.1, -.06 * chapter, -.18), frame, glass, edge)
    for j in range(2):
        points = [(-36 + t * 3, 3 + j, 7.8 + j * .35 + math.sin(t * .38 + j) * (.55 + chapter * .2)) for t in range(25)]
        ribbon(api, 'Suspended white return path', points, .65 + j * .12, fabric)
    api.layer('near')
    for i, x in enumerate((-27, -12, 6, 23)):
        mirror_panel(api, (x, -12, -10.1), 3.5, 7.5 + i % 2,
                     (.13, (.06 + chapter * .045) * (-1 if i % 2 else 1), .28 * (-1 if i % 2 else 1)), frame, glass, edge)
        api.box('Mirror water terrace', (x, -10, -9), (11, 5, .55), dark, .1, rotation=(.04, .04 * chapter, -.08))
    points = [(-32 + i * 2.8, -13, 8.3 + math.sin(i * .48) * .6) for i in range(24)]
    ribbon(api, 'Close return ribbon', points, 1.2, fabric)


def background_plate(api, path):
    api.layer('far')
    material = bpy.data.materials.new('Original world palette and distant atmosphere')
    nodes = material.node_tree.nodes
    nodes.clear()
    texture = nodes.new('ShaderNodeTexImage')
    texture.image = bpy.data.images.load(str(path))
    emission = nodes.new('ShaderNodeEmission')
    emission.inputs['Strength'].default_value = .82
    out = nodes.new('ShaderNodeOutputMaterial')
    material.node_tree.links.new(texture.outputs['Color'], emission.inputs['Color'])
    material.node_tree.links.new(emission.outputs[0], out.inputs['Surface'])
    width = 2 * 115 * 18 / 42 * 1.005
    height = width / 3
    obj = api.mesh('Preserved distant world artwork', [(-width / 2, 45, -height / 2),
        (width / 2, 45, -height / 2), (width / 2, 45, height / 2), (-width / 2, 45, height / 2)], [(0, 1, 2, 3)], material)
    uv = obj.data.uv_layers.new(name='Original panorama coordinates')
    for item, coord in zip(uv.data, ((0, 0), (1, 0), (1, 1), (0, 1))):
        item.uv = coord


def lighting(api, theme):
    api.layer('lighting')
    warm = {'akari': (1, .69, .4), 'koharu': (1, .66, .53), 'rei': (.53, .44, 1), 'mina': (.52, .85, 1)}[theme]
    for name, position, energy, size, color in [
        ('Warm architectural key', (-15, -20, 28), 5000, 24, warm),
        ('Cool broad fill', (28, -30, 8), 3800, 35, (.42, .6, 1)),
        ('Upper silhouette rim', (3, 16, 25), 6500, 18, (1, .74, .48) if theme == 'akari' else (.65, .73, 1)),
        ('Lower reflected light', (-10, -12, -20), 1800, 28, (.38, .58, .8)),
    ]:
        data = bpy.data.lights.new(name, 'AREA')
        data.energy, data.shape, data.size, data.color = energy * 3.2, 'DISK', size, color
        obj = bpy.data.objects.new(name, data)
        api.collection.objects.link(obj)
        obj.location = position
        obj.rotation_euler = (-Vector(position)).to_track_quat('-Z', 'Y').to_euler()
    scene = bpy.context.scene
    world = bpy.data.worlds.new('Soft atmospheric fill')
    scene.world = world
    world.node_tree.nodes.get('Background').inputs['Color'].default_value = (.07, .09, .15, 1)
    world.node_tree.nodes.get('Background').inputs['Strength'].default_value = .7


def build(theme, chapter, preview):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    api = SceneArt()
    path = ROOT / 'char/bg2/route' / f'{theme}{CHAPTERS[chapter]}_far.png'
    background_plate(api, path)
    if theme == 'akari':
        build_akari(api, chapter)
    elif theme == 'mina':
        build_mina(api, chapter)
    else:
        from route_blender_architecture import build_koharu, build_rei
        (build_koharu if theme == 'koharu' else build_rei)(api, chapter)
    lighting(api, theme)
    scene = bpy.context.scene
    camera = bpy.data.cameras.new('Side scrolling panorama camera')
    obj = bpy.data.objects.new('Camera', camera)
    api.collections['lighting'].objects.link(obj)
    obj.location, obj.rotation_euler = (0, -70, 0), (math.pi / 2, 0, 0)
    camera.lens, camera.sensor_width, camera.clip_end = 42, 36, 250
    scene.camera = obj
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y = 2172, 724
    scene.render.resolution_percentage = 50 if preview else 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.image_settings.color_depth = '8'
    scene.view_settings.view_transform = 'Standard'
    scene.render.image_settings.compression = 20
    scene['Source artwork'] = str(path.relative_to(ROOT))
    scene['Purpose'] = '3D middle/foreground architecture; original distant atmosphere; three aligned parallax passes'
    name = f'{theme}{CHAPTERS[chapter]}'
    report = {'id': name, 'blender': bpy.app.version_string, 'reference': str(path.relative_to(ROOT)),
              'objects': {k: len(v.objects) for k, v in api.collections.items()}, 'size': [2172, 724]}
    if not preview:
        bpy.ops.file.pack_all()
        bpy.context.preferences.filepaths.save_version = 0
        bpy.context.preferences.filepaths.file_preview_type = 'NONE'
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / f'{name}.blend'))
        for layer in ('far', 'mid', 'near'):
            for key in ('far', 'mid', 'near'):
                api.collections[key].hide_render = key != layer
            scene.render.filepath = str(OUTPUT / f'{name}_{layer}.png')
            bpy.ops.render.render(write_still=True)
        for key in ('far', 'mid', 'near'):
            api.collections[key].hide_render = False
    if preview or chapter == 0:
        scene.render.filepath = str(QA / f'{name}_blender.png')
        bpy.ops.render.render(write_still=True)
    (QA / f'{name}_geometry.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('ROUTE_BACKGROUND_COMPLETE ' + json.dumps(report), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--stage', nargs='+', default=['akari', 'koharu', 'rei', 'mina'])
    parser.add_argument('--chapters', nargs='+', type=int, default=[0, 1, 2, 3])
    parser.add_argument('--preview', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    for directory in (SOURCE, OUTPUT, QA):
        directory.mkdir(parents=True, exist_ok=True)
    (SOURCE / '.gdignore').touch()
    sys.path.insert(0, str(ROOT / 'tools'))
    for stage in args.stage:
        for chapter in args.chapters:
            build(stage, chapter, args.preview)
