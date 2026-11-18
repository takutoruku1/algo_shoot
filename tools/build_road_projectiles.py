import argparse
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'assets/road_projectiles'
SOURCE = ROOT / 'assets/source/road_projectiles'
QA = ROOT / 'build/qa_story/road3d_20261029'
FRAMES, CELL, COLUMNS = 48, 128, 8
NAMES = [
    'akari_docs', 'akari_sticky', 'akari_reply_plane', 'enemy_akari_deadline',
    'enemy_akari_unsent', 'enemy_akari_vacant', 'koharu_glow_arrow', 'koharu_ticket',
    'koharu_prayer_tag', 'enemy_koharu_cheer', 'enemy_koharu_comparison',
    'enemy_koharu_parcel', 'rei_subscriber', 'rei_comment', 'enemy_rei_anonymous',
    'enemy_rei_clipper', 'enemy_rei_metrics', 'mina_eraser', 'mina_folded_note', 'mina_unanswered',
]
MODELS = {}
CURRENT = None


def rgb(value):
    channels = [int(value[i:i+2], 16) / 255 for i in (0, 2, 4)]
    return tuple(c / 12.92 if c < .04045 else ((c + .055) / 1.055) ** 2.4 for c in channels)


def material(name, color, metallic=0, roughness=.32):
    result = bpy.data.materials.new(name)
    node = result.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value = (*rgb(color), 1)
    node.inputs['Metallic'].default_value = metallic
    node.inputs['Roughness'].default_value = roughness
    node.inputs['Coat Weight'].default_value = .18
    return result


def xyz(point):
    x, y, z = point
    return x, -z, y


def finish(obj, surface, rounding=0):
    obj.parent = CURRENT
    obj.data.materials.append(surface)
    if rounding:
        bevel = obj.modifiers.new('Rounded solid edges', 'BEVEL')
        bevel.width, bevel.segments = rounding, 3
        bevel.limit_method = 'ANGLE'
        obj.modifiers.new('Weighted normals', 'WEIGHTED_NORMAL')
    return obj


def mesh(name, vertices, faces, surface, rounding=0):
    data = bpy.data.meshes.new(name)
    data.from_pydata([xyz(p) for p in vertices], [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    return finish(obj, surface, rounding)


def prism(name, points, depth, surface, z=0, rounding=.025):
    n = len(points)
    vertices = [(x, y, h) for h in (z-depth/2, z+depth/2) for x, y in points]
    faces = [tuple(reversed(range(n))), tuple(range(n, n*2))]
    faces += [(i, (i+1) % n, (i+1) % n+n, i+n) for i in range(n)]
    return mesh(name, vertices, faces, surface, rounding)


def box(name, center, size, surface, rounding=.035):
    bpy.ops.mesh.primitive_cube_add(size=1, location=xyz(center))
    obj = bpy.context.object
    obj.name = name
    obj.scale = size[0], size[2], size[1]
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, surface, rounding)


def cylinder(name, center, radius, depth, surface, vertices=48, vertical=False):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=xyz(center))
    obj = bpy.context.object
    obj.name = name
    if not vertical:
        obj.rotation_euler.x = math.pi/2
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return finish(obj, surface, .015)


def tube(name, points, radius, surface):
    data = bpy.data.curves.new(name, 'CURVE')
    data.dimensions, data.bevel_depth, data.bevel_resolution = '3D', radius, 2
    spline = data.splines.new('POLY')
    spline.points.add(len(points)-1)
    for item, point in zip(spline.points, points):
        item.co = (*xyz(point), 1)
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    return finish(obj, surface)


def star(name, center, radius, surface, depth=.06):
    x, y, z = center
    points = [(x + math.cos(math.pi/2+i*math.pi/5)*radius*(1 if i % 2 == 0 else .43),
               y + math.sin(math.pi/2+i*math.pi/5)*radius*(1 if i % 2 == 0 else .43)) for i in range(10)]
    return prism(name, points, depth, surface, z, .01)


def paper(name, surface, z=0, width=1.35, height=1.65):
    points = [(-width/2, -height/2), (width/2, -height/2), (width/2, height/2-.3),
              (width/2-.3, height/2), (-width/2, height/2)]
    prism(name, points, .065, surface, z, .018)
    prism(name+' fold', [(width/2-.3, height/2), (width/2-.3, height/2-.3), (width/2, height/2-.3)],
          .05, PALETTE['lavender'], z+.04, .008)


def ring(name, center, width, height, surface, radius=.025):
    x, y, z = center
    points = [(x+math.cos(t*math.tau/32)*width, y+math.sin(t*math.tau/32)*height, z) for t in range(33)]
    return tube(name, points, radius, surface)


def build(name):
    global CURRENT
    CURRENT = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(CURRENT)
    p = PALETTE
    if name == 'enemy_akari_deadline':
        cylinder('Steel stopwatch', (0, -.1, 0), .76, .32, p['steel'])
        cylinder('Ivory dial', (0, -.1, .18), .65, .05, p['paper'])
        cylinder('Rear plate', (0, -.1, -.18), .65, .035, p['navy'])
        cylinder('Winder', (0, .78, 0), .18, .24, p['red'], vertical=True)
        for i in range(12):
            a = i*math.tau/12
            tube('Dial mark', [(math.sin(a)*r, -.1+math.cos(a)*r, .23) for r in (.52, .58)], .024, p['red'])
        tube('Short hand', [(0, -.1, .25), (-.27, .12, .25)], .04, p['red'])
        tube('Long hand', [(0, -.1, .26), (.40, .23, .26)], .032, p['red'])
        cylinder('Dial pin', (0, -.1, .27), .075, .05, p['red'])
    elif name == 'enemy_akari_unsent':
        box('Sealed envelope', (0, 0, 0), (1.5, 1.5, .22), p['paper'], .08)
        for a, b in [((-.7, .65, .13), (0, -.08, .15)), ((.7, .65, .13), (0, -.08, .15)),
                     ((-.7, -.7, .13), (-.12, -.04, .15)), ((.7, -.7, .13), (.12, -.04, .15))]:
            tube('Envelope fold', [a, b], .018, p['rose'])
        cylinder('Wax seal', (0, -.07, .17), .24, .12, p['rose'])
        ring('Wax emboss', (0, -.07, .24), .17, .18, p['pink'], .017)
        box('Rear stamp', (.48, .48, -.13), (.25, .30, .035), p['red'], .015)
    elif name == 'enemy_akari_vacant':
        box('Badge frame', (0, -.13, 0), (1.38, 1.57, .16), p['blue'], .09)
        box('Blank badge', (0, -.13, .1), (1.15, 1.32, .045), p['paper'], .025)
        box('Clip', (0, .78, .04), (.38, .55, .19), p['navy'], .035)
        cylinder('Clip rivet', (0, .78, .15), .095, .04, p['steel'])
        box('Empty name field', (0, .02, .14), (.88, .64, .025), p['ice'], .025)
        for y in (-.45, -.62):
            box('Badge rule', (-.14, y, .14), (.65, .07, .025), p['blue'], .01)
    elif name == 'akari_docs':
        for i in range(3):
            paper('Stacked document', p['paper'] if i == 2 else p['ice'], (i-1)*.09)
        box('Red tab', (.42, .79, .04), (.22, .28, .06), p['red'])
        for i in range(7):
            box('Printed rule', (-.06, .48-i*.17, .14), (.95 if i else .65, .045, .025), p['blue'], .008)
    elif name == 'akari_sticky':
        vertices = [(0, 0, .27), (-.68, -.27, 0), (-.28, .53, .32), (.12, .84, -.12),
                    (.72, .17, .12), (.91, .24, -.17), (.59, -.48, .02), (-.75, -.72, -.15)]
        faces = [(0, i, i+1) for i in range(1, 7)] + [(0, 7, 1)]
        obj = mesh('Crumpled red draft', vertices, faces, p['red'])
        modifier = obj.modifiers.new('Paper thickness', 'SOLIDIFY')
        modifier.thickness = .06
        mesh('Fold highlight', [(0, 0, .28), (-.28, .53, .33), (-.68, -.27, .01)], [(0, 1, 2)], p['pink'])
    elif name == 'akari_reply_plane':
        vertices = [(1, 0, 0), (-.86, .56, .03), (-.54, 0, .28), (-.86, -.56, .03), (-.32, -.26, -.19)]
        obj = mesh('Folded reply plane', vertices, [(0, 1, 2), (0, 2, 3), (0, 3, 4), (0, 4, 2)], p['paper'])
        obj.modifiers.new('Folded paper thickness', 'SOLIDIFY').thickness = .06
        tube('Centre fold', [(-.54, 0, .3), (1, 0, .02)], .018, p['blue'])
    elif name == 'koharu_glow_arrow':
        prism('Arrow sign', [(-.9, .13), (.06, .13), (.06, -.27), (.95, .3), (.06, .88), (.06, .48), (-.9, .48)],
              .22, p['orange'], rounding=.04)
        prism('Arrow inlay', [(-.72, .24), (.23, .24), (.23, -.04), (.70, .3), (.23, .62), (.23, .36), (-.72, .36)],
              .035, p['paper'], .13, .01)
        box('Sign grip', (0, -.39, -.04), (.23, 1.02, .23), p['rose'])
    elif name == 'koharu_ticket':
        points = [(-.8, -.63), (.75, -.63), (.68, -.37), (.8, -.17), (.68, .07), (.8, .30), (.70, .6), (-.8, .6)]
        prism('Ticket body', points, .12, p['paper'], rounding=.02)
        box('Ticket spine', (-.66, 0, .075), (.12, 1.19, .035), p['gold'], .015)
        star('Ticket star', (.38, .26, .09), .23, p['orange'])
        for i, w in enumerate((.68, .47, .30)):
            box('Ticket print', (-.13, -.1-i*.17, .09), (w, .07, .025), p['orange'], .008)
    elif name == 'koharu_prayer_tag':
        prism('Prayer tag', [(-.6, -.83), (.6, -.83), (.68, .45), (0, .82), (-.68, .45)], .2, p['paper'])
        star('Prayer star', (0, -.10, .13), .43, p['lavender'])
        for side in (-1, 1):
            tube('Ribbon loop', [(0, .72, 0), (.32*side, 1.02, .04), (.28*side, 1.14, 0), (0, .77, -.05)], .04, p['pink'])
    elif name == 'enemy_koharu_cheer':
        box('Penlight handle', (0, -.57, 0), (.47, .75, .35), p['navy'], .11)
        box('Penlight head', (0, .36, 0), (.68, 1.20, .42), p['lavender'], .19)
        box('Silver collar', (0, -.23, 0), (.70, .13, .44), p['steel'])
        star('Penlight star', (0, .39, .25), .25, p['paper'])
        cylinder('Switch', (0, -.53, .20), .08, .035, p['steel'])
        ring('Wrist strap', (.39, -.64, -.02), .23, .31, p['rose'], .055)
    elif name == 'enemy_koharu_comparison':
        paper('Checklist', p['paper'])
        for i in range(3):
            y = .45-i*.43
            tube('Check', [(-.52, y, .07), (-.40, y-.13, .07), (-.15, y+.13, .07)], .045, p['red'])
            box('List rule', (.27, y-.06, .06), (.57, .08, .035), p['blue'], .015)
    elif name == 'enemy_koharu_parcel':
        box('Gift parcel', (0, 0, 0), (1.36, 1.32, 1.06), p['paper'], .055)
        box('Ribbon around height', (0, 0, 0), (.31, 1.36, 1.10), p['pink'], .018)
        box('Ribbon around width', (0, 0, 0), (1.40, .27, 1.10), p['rose'], .018)
        star('Parcel seal', (.43, -.35, .56), .18, p['lavender'])
    elif name in ('rei_comment', 'enemy_rei_anonymous'):
        points = [(-.8, .65), (.72, .76), (.9, -.38), (-.20, -.52), (-.78, -.91), (-.63, -.36), (-.9, -.20)]
        prism('Speech bubble bevel', points, .36, p['lavender'], rounding=.045)
        inner = [(x*.81, y*.78+.02) for x, y in points]
        prism('Dark speech inset', inner, .04, p['navy'], .2, .02)
        if name == 'rei_comment':
            star('Comment star', (.02, .05, .25), .40, p['gold'], .10)
        else:
            prism('Anonymous slash', [(-.44, .05), (.19, .43), (.04, .12), (.52, .14), (-.22, -.42), (-.06, -.06)],
                  .08, p['red'], .25, .008)
    elif name == 'rei_subscriber':
        prism('Subscription gem', [(-.89, -.42), (-.68, .52), (.2, .80), (.86, .40), (.91, -.41), (-.2, -.71)],
              .48, p['violet'], rounding=.07)
        prism('Pearl gem face', [(-.63, -.30), (-.48, .38), (.15, .56), (.62, .27), (.66, -.29), (-.14, -.49)],
              .045, p['paper'], .27, .015)
        star('Subscriber star', (.04, .02, .32), .47, p['gold'], .10)
    elif name == 'enemy_rei_clipper':
        centers = [(math.sin(t*math.pi*2)*.46, .92-t*1.84, math.cos(t*math.pi*2)*.20) for t in np.linspace(0, 1, 25)]
        vertices = [(x+s*.43, y, z) for x, y, z in centers for s in (-1, 1)]
        obj = mesh('Twisted film ribbon', vertices, [(i*2, i*2+1, i*2+3, i*2+2) for i in range(24)], p['navy'])
        thickness = obj.modifiers.new('Film thickness', 'SOLIDIFY')
        thickness.thickness, thickness.offset = .035, 0
        for i in range(0, 24, 6):
            part = [(x+s*.29, y, z+.04) for x, y, z in centers[i+1:i+6] for s in (-1, 1)]
            mesh('Exposed film frame', part, [(j*2, j*2+1, j*2+3, j*2+2) for j in range(4)], p['pink'])
        for x, y, z in centers[1::2]:
            for side in (-1, 1):
                box('Film perforation', (x+side*.37, y, z+.035), (.07, .06, .012), p['paper'], .006)
    elif name == 'enemy_rei_metrics':
        prism('Metrics downward arrow', [(-.48, .28), (.48, .28), (.48, -.18), (.9, -.18), (0, -1), (-.9, -.18), (-.48, -.18)],
              .27, p['gold'], rounding=.025)
        prism('Metrics badge', [(-.56, .75), (0, 1), (.56, .75), (.45, -.05), (0, -.27), (-.45, -.05)],
              .34, p['gold'])
        prism('Metrics screen', [(-.43, .66), (0, .85), (.43, .66), (.34, .05), (0, -.12), (-.34, .05)],
              .04, p['navy'], .20, .01)
        for i, h in enumerate((.52, .37, .22)):
            box('Graph column', (-.24+i*.23, .05+h/2, .25), (.14, h, .09), p['ice'], .01)
    elif name == 'mina_eraser':
        box('Eraser rubber', (0, 0, 0), (1.8, .74, .66), p['paper'], .15)
        box('Paper sleeve', (.03, 0, 0), (.86, .78, .70), p['rose'], .04)
        prism('Sleeve folded corner', [(-.33, .30), (.39, .30), (.15, .06)], .02, p['pink'], .37, .008)
        for side in (-1, 1):
            for y, z in ((-.13, .12), (.12, .24), (.05, -.18)):
                cylinder('Rubber pores', (side*.91, y, z), .027, .012, p['lavender'], vertices=12).rotation_euler.z = math.pi/2
    elif name == 'mina_folded_note':
        vertices = [(-.89, .42, 0), (.05, .92, -.05), (.91, .34, .05), (.65, -.66, -.02),
                    (-.12, -.90, .08), (-.76, -.38, -.1), (-.05, .03, .34)]
        for i in range(6):
            obj = mesh('Folded memory facet', [vertices[i], vertices[(i+1) % 6], vertices[6]], [(0, 1, 2)],
                       p['paper'] if i % 3 else p['pink'])
            obj.modifiers.new('Memory paper thickness', 'SOLIDIFY').thickness = .05
        tube('Memory fold', [vertices[0], vertices[6], vertices[3]], .015, p['rose'])
    elif name == 'mina_unanswered':
        box('Receiver bridge', (0, 0, 0), (1.04, .26, .26), p['blue'], .06)
        for side in (-1, 1):
            cylinder('Octagonal receiver', (side*.67, 0, 0), .39, .42, p['navy'], vertices=8)
            cylinder('Receiver face', (side*.67, 0, .24), .30, .06, p['lavender'], vertices=8)
            for x, y in ((0, 0), (-.10, .07), (.09, .09), (.01, -.11)):
                cylinder('Receiver speaker hole', (side*.67+x, y, .28), .026, .008, p['navy'], vertices=12)
    else:
        raise ValueError(name)
    bpy.context.view_layer.update()
    vertices = []
    graph = bpy.context.evaluated_depsgraph_get()
    for child in CURRENT.children:
        evaluated = child.evaluated_get(graph)
        data = evaluated.to_mesh()
        vertices.extend(child.matrix_world @ v.co for v in data.vertices)
        evaluated.to_mesh_clear()
    minimum = Vector(tuple(min(v[i] for v in vertices) for i in range(3)))
    maximum = Vector(tuple(max(v[i] for v in vertices) for i in range(3)))
    center = (minimum+maximum)/2
    radius = max((v-center).length for v in vertices)
    for child in CURRENT.children:
        child.location = (child.location-center)/radius
        child.scale /= radius
    MODELS[name] = CURRENT
    return CURRENT


def pose(obj, name, phase):
    angle = phase*math.tau
    if name in ('akari_reply_plane', 'koharu_glow_arrow'):
        obj.rotation_euler = (angle, .16*math.sin(angle), .12*math.sin(angle*2))
    else:
        obj.rotation_euler = (.25+.3*math.sin(angle), .18*math.sin(angle*2), angle)


def outline(pixels):
    alpha = pixels[:, :, 3]
    inner = alpha > .6
    edge = inner.copy()
    for dx, dy in ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (-1, -1), (-1, 1), (1, -1)):
        edge |= np.roll(np.roll(inner, dy, axis=0), dx, axis=1)
    shadow = edge.astype(np.float32)*.9
    result = pixels.copy()
    a = alpha+shadow*(1-alpha)
    result[:, :, :3] = (pixels[:, :, :3]*alpha[:, :, None] + np.array((.025, .02, .04))*shadow[:, :, None]*(1-alpha[:, :, None])) / np.maximum(a[:, :, None], .00001)
    result[:, :, 3] = a
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--only', choices=NAMES)
    parser.add_argument('--preview', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    for directory in (OUTPUT, SOURCE, QA):
        directory.mkdir(parents=True, exist_ok=True)
    (SOURCE / '.gdignore').touch()
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    PALETTE = {name: material(name, color, metal) for name, color, metal in [
        ('paper', 'fff0d6', .0), ('ice', 'c8e8f7', .1), ('blue', '718eaf', .32),
        ('navy', '30324e', .25), ('steel', 'a9adcb', .72), ('red', 'cb3853', .1),
        ('rose', 'ae526e', .1), ('pink', 'f3acbe', .05), ('lavender', 'b8a0e4', .25),
        ('violet', '7553bb', .4), ('gold', 'edc66f', .65), ('orange', 'f0a15a', .12),
    ]}
    names = [args.only] if args.only else NAMES
    for name in NAMES:
        build(name)
        for child in MODELS[name].children:
            child.hide_render = True
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 12
    scene.cycles.use_denoising = True
    scene.render.resolution_x = scene.render.resolution_y = CELL
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'Medium High Contrast'
    scene.world.color = (.20, .20, .20)
    scene.render.threads_mode, scene.render.threads = 'FIXED', 4
    bpy.ops.object.camera_add(location=(0, -6, 0))
    camera = bpy.context.object
    camera.rotation_euler = (Vector((0, 0, 0))-camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type, camera.data.ortho_scale = 'ORTHO', 2.5
    scene.camera = camera
    for label, position, power, size in [('Key', (-3, -4, 5), 450, 4), ('Fill', (4, -2, 1), 260, 3), ('Rim', (2, 3, 4), 650, 3)]:
        bpy.ops.object.light_add(type='AREA', location=position)
        light = bpy.context.object
        light.name, light.data.energy, light.data.shape, light.data.size = label, power, 'DISK', size
        light.rotation_euler = (-light.location).to_track_quat('-Z', 'Y').to_euler()
    scene.frame_start, scene.frame_end = 1, FRAMES
    bpy.context.preferences.filepaths.save_version = 0
    bpy.context.preferences.filepaths.file_preview_type = 'NONE'
    for name in names:
        obj = MODELS[name]
        for child in obj.children:
            child.hide_render = False
        count = 8 if args.preview else FRAMES
        atlas = np.zeros((CELL*((count+COLUMNS-1)//COLUMNS), CELL*COLUMNS, 4), dtype=np.float32)
        for frame in range(count):
            pose(obj, name, frame/count)
            scene.render.filepath = str(QA / 'render.png')
            bpy.ops.render.render(write_still=True)
            rendered = bpy.data.images.load(scene.render.filepath, check_existing=False)
            pixels = np.array(rendered.pixels[:], dtype=np.float32).reshape(CELL, CELL, 4)
            bpy.data.images.remove(rendered)
            pixels = outline(pixels)
            x, y = frame % COLUMNS*CELL, (count//COLUMNS-1-frame//COLUMNS)*CELL
            atlas[y:y+CELL, x:x+CELL] = pixels
            print(f'ROAD_FRAME {name} {frame+1}/{count}', flush=True)
        texture = bpy.data.images.new(name+'_turntable', width=atlas.shape[1], height=atlas.shape[0], alpha=True)
        texture.pixels.foreach_set(atlas.ravel())
        texture.filepath_raw = str((QA if args.preview else OUTPUT) / (name+'.png'))
        texture.file_format = 'PNG'
        texture.save()
        bpy.data.images.remove(texture)
        for child in obj.children:
            child.hide_render = True
    if not args.preview:
        for index, (name, obj) in enumerate(MODELS.items()):
            for frame in range(FRAMES):
                pose(obj, name, frame / FRAMES)
                obj.keyframe_insert(data_path='rotation_euler', frame=frame+1)
            obj.location = (index % 5*2.7-5.4, 0, 4.1-index//5*2.7)
            for child in obj.children:
                child.hide_render = False
        scene.render.fps = 24
        scene.frame_set(1)
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'road_projectiles.blend'), relative_remap=False)
        (OUTPUT / 'layout.json').write_text(json.dumps({'frames': FRAMES, 'cell': CELL, 'columns': COLUMNS, 'assets': NAMES}, indent=2)+'\n')
