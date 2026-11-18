import json
import math
from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/source/player_specials'
OUTPUT = ROOT / 'assets/fx/player_specials.glb'
QA = ROOT / 'build/qa_story/player_special_ultimate_1118/models'


def linear(value):
    channels = [int(value[i:i+2], 16) / 255 for i in (0, 2, 4)]
    return tuple(c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4 for c in channels)


def material(name, color, metal=0, glow=0, alpha=1):
    result = bpy.data.materials.new(name)
    shader = result.node_tree.nodes.get('Principled BSDF')
    color = linear(color)
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Metallic'].default_value = metal
    shader.inputs['Roughness'].default_value = .27
    shader.inputs['Coat Weight'].default_value = .35
    shader.inputs['Emission Color'].default_value = (*color, 1)
    shader.inputs['Emission Strength'].default_value = glow
    shader.inputs['Alpha'].default_value = alpha
    if alpha < 1:
        result.surface_render_method = 'DITHERED'
    return result


def root(name, character):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj['character_reference'] = f'char/player/{character}/{character}_idle_v2.png'
    obj['projectile_reference'] = f'assets/source/player_projectiles/player_projectiles.blend:{character}'
    obj['front_axis'] = 'GLB +Z; Blender -Y; screen right +X; screen up +Y'
    return obj


def mesh(name, vertices, faces, surface, parent, smooth=False):
    data = bpy.data.meshes.new(name)
    data.from_pydata([(x, -z, y) for x, y, z in vertices], [], faces)
    assert not data.validate(), name
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    data.materials.append(surface)
    for polygon in data.polygons:
        polygon.use_smooth = smooth
        assert polygon.area > 1e-10, name
    return obj


def crystal(name, outline, depth, surface, parent, z=0):
    cx = sum(p[0] for p in outline) / len(outline)
    cy = sum(p[1] for p in outline) / len(outline)
    n = len(outline)
    vertices = [(x, y, z) for x, y in outline] + [(cx, cy, z + depth), (cx, cy, z - depth * .45)]
    faces = [(i, (i+1) % n, n) for i in range(n)] + [((i+1) % n, i, n+1) for i in range(n)]
    return mesh(name, vertices, faces, surface, parent)


def tube(name, points, radius, surface, parent, sides=6):
    vertices, faces = [], []
    for i, point in enumerate(points):
        tangent = (Vector(points[min(i+1, len(points)-1)]) - Vector(points[max(0, i-1)])).normalized()
        normal = tangent.cross(Vector((0, 0, 1)))
        if normal.length < .001:
            normal = tangent.cross(Vector((0, 1, 0)))
        normal.normalize()
        other = tangent.cross(normal).normalized()
        for j in range(sides):
            a = j * math.tau / sides
            vertices.append(tuple(Vector(point) + radius * (math.cos(a) * normal + math.sin(a) * other)))
    for i in range(len(points)-1):
        for j in range(sides):
            faces.append((i*sides+j, i*sides+(j+1)%sides, (i+1)*sides+(j+1)%sides, (i+1)*sides+j))
    faces += [tuple(reversed(range(sides))), tuple(range((len(points)-1)*sides, len(points)*sides))]
    return mesh(name, vertices, faces, surface, parent, True)


def ring(name, radius, width, surface, parent, z=0, start=0, end=math.tau, squash=1):
    return tube(name, [(radius*math.cos(a), radius*math.sin(a)*squash, z)
                      for a in [start+(end-start)*i/96 for i in range(97)]], width, surface, parent)


def ribbon(name, points, widths, surface, parent):
    vertices = []
    for i, point in enumerate(points):
        direction = Vector(points[min(i+1, len(points)-1)]) - Vector(points[max(i-1, 0)])
        side = Vector((-direction.y, direction.x, 0)).normalized() * widths[i]
        vertices += [tuple(Vector(point)-side), tuple(Vector(point)+side)]
    obj = mesh(name, vertices, [(i*2, i*2+2, i*2+3, i*2+1) for i in range(len(points)-1)], surface, parent)
    obj.modifiers.new('Paper thickness', 'SOLIDIFY').thickness = .012
    return obj


def star_points(radius):
    return [(math.cos(math.pi/2+i*math.pi/5)*(radius if i%2 == 0 else radius*.43),
             math.sin(math.pi/2+i*math.pi/5)*(radius if i%2 == 0 else radius*.43)) for i in range(10)]


def heart_points(radius, center=(0, 0)):
    return [(center[0]+math.sin(a)**3*radius,
             center[1]+(13*math.cos(a)-5*math.cos(2*a)-2*math.cos(3*a)-math.cos(4*a))/17*radius)
            for a in [math.tau*i/40 for i in reversed(range(40))]]


def build():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    p = {
        'silver': material('Pearl silver', 'd4eafa', .72),
        'paper': material('Ivory folded paper', 'fff4db', .12),
        'navy': material('Mina deep blue', '203c69', .55),
        'cyan': material('Mina blue crystal', '5bd5f2', .48, .18),
        'ice': material('Mina luminous seam', 'b8f7ff', .3, 1.5),
        'glass': material('Mina translucent letter', '6ad4ee', .25, .12, .22),
        'beam': material('Mina luminous beam volume', '32c9f4', .08, .85, .38),
        'gold': material('Warm gold foil', 'efbd68', .72),
        'orange': material('Akari amber folds', 'f59938', .42, .2),
        'red': material('Akari ember underside', 'a62b2c', .32),
        'fire': material('Akari white hot edge', 'fff0a5', .2, 2),
        'mint': material('Koharu protective mint', '72e7b2', .48, .25),
        'jade': material('Koharu deep jade', '21665b', .58),
        'pink': material('Koharu heart crystal', 'f7b9dd', .38, .3),
        'greenlight': material('Koharu light seam', 'b7ffdd', .2, 1.3),
        'violet': material('Rei amethyst', 'aa72e4', .58, .2),
        'purple': material('Rei midnight facets', '44335f', .62),
        'starlight': material('Rei starlight', 'f1d5ff', .2, 1.6),
    }
    models = {}
    def model(name, character):
        models[name] = root(name, character)
        return models[name]

    shield = model('MinaShield', 'mina')
    shield['collision'] = 'Shield center + direction * 20; side extent +/-42; thickness 2.5 game units'
    for side in (-1, 1):
        outline = [(-.07, 0), (-.13, side*.98), (.10, side*.86), (.23, side*.34), (.14, 0)]
        crystal('Blue letter wing', outline, .075, p['glass'], shield)
        tube('Silver letter edge', [(x, y, .05) for x, y in outline[:4]], .020, p['silver'], shield)
        tube('Lit folded edge', [(-.10, side*.98, .08), (.10, side*.70, .12), (.18, side*.28, .13), (.03, 0, .15)], .012, p['ice'], shield)
        for i in range(3):
            y = side*(.37+i*.20)
            crystal('Overlapping paper fold', [(-.10,y),(-.22,y+side*.12),(.045,y+side*.16),(.12,y)], .05, p['paper'], shield, -.04)
    crystal('Navy envelope seal', [(-.18,0),(.0,-.21),(.19,0),(0,.21)], .13, p['navy'], shield, .08)
    crystal('Blue envelope seal', [(-.12,0),(0,-.15),(.14,0),(0,.15)], .14, p['cyan'], shield, .14)
    tube('Actual guard seam', [(0,-1,.10),(0,1,.10)], .014, p['ice'], shield)

    seal = model('MinaSeal', 'mina')
    ring('Silver iris', .77, .09, p['silver'], seal)
    crystal('Reply diamond', [(-.64,0),(0,-.64),(.64,0),(0,.64)], .35, p['cyan'], seal, .06)
    for i in range(4):
        a = i*math.pi/2
        points = [(math.cos(a)*r-math.sin(a)*y, math.sin(a)*r+math.cos(a)*y)
                  for r,y in ((.80,-.13),(1.21,0),(.80,.13))]
        crystal('Iris vanes', points, .12, p['ice'], seal)

    beam = model('MinaBeam', 'mina')
    beam['collision'] = 'Local X 0..1 maps to actual beam start/end; local Y +/-1 maps to BeamRadius'
    tube('Cyan reply volume', [(0,0,0),(1,0,0)], .68, p['beam'], beam, 16)
    tube('White reply core', [(0,0,0),(1,0,0)], .24, p['ice'], beam, 12)
    for side in (-1,1):
        tube('Beam boundary', [(0,side,0),(1,side,0)], .035, p['cyan'], beam)
        points = [(i/64, side*(.62+.16*math.sin(i/64*math.tau*3)), .16*math.cos(i/64*math.tau*3)) for i in range(65)]
        tube('Braided reply filament', points, .035, p['ice'], beam)
    for strand in range(4):
        phase = strand * math.pi / 2
        points = [(i/96,.85*math.sin(i/96*math.tau*4+phase),.85*math.cos(i/96*math.tau*4+phase)) for i in range(97)]
        tube('Spiraling cannon envelope', points, .024, p['cyan'] if strand%2 else p['ice'], beam)

    blade = model('AkariBlade', 'akari')
    blade['motion'] = 'Paper tip follows the current dash position; never previews an untraveled hit'
    for side in (-1,1):
        crystal('Scarlet folded underside', [(-.66,side*.72),(-.46,0),(1.04,0),(-.15,side*.24)], .12, p['red'], blade, -.08)
        crystal('Ivory folded blade', [(-.56,side*.66),(1.04,0),(-.12,side*.13)], .13, p['paper'], blade, .04)
        crystal('Amber cutting fold', [(-.54,side*.65),(.97,0),(-.13,side*.29)], .09, p['orange'], blade, .12)
        tube('Incandescent paper edge', [(-.56,side*.66,.13),(1.04,0,.13)], .023, p['fire'], blade)
    crystal('Letter spine', [(-.64,0),(-.12,-.10),(.98,0),(-.12,.10)], .19, p['gold'], blade, .10)
    for layer in range(3):
        radius = .76+layer*.11
        points = [(math.cos(a)*radius,math.sin(a)*radius,.04+layer*.05)
                  for a in [-1.28+2.56*i/40 for i in range(41)]]
        ribbon('Swept amber crescent', points, [.018+.055*math.sin(math.pi*i/40) for i in range(41)],
               p['fire'] if layer == 2 else p['orange'], blade)

    wake = model('AkariWake', 'akari')
    wake['motion'] = 'Local X 0..1 maps only to the distance already traveled'
    for side in (-1,1):
        points = [(i/32, side*(.5+.18*math.sin(i/32*math.pi)), .18*math.sin(i/32*math.tau)) for i in range(33)]
        ribbon('Torn foil wake', points, [.002+.06*i/32 for i in range(33)], p['orange'], wake)
        tube('Golden wake edge', points, .014, p['fire'], wake)

    shelter = model('KoharuRing', 'koharu')
    shelter['collision'] = 'Ring outside edge is radius 1, mapped to exactly 52 game units at fixed Origin'
    ring('Protective jade rim', .975, .025, p['jade'], shelter)
    ring('Mint boundary light', .987, .012, p['greenlight'], shelter, .03)
    ring('Inner silver braid', .91, .012, p['silver'], shelter, .035)
    for i in range(8):
        a = i*math.tau/8
        outline = [(math.cos(a)*x-math.sin(a)*y, math.sin(a)*x+math.cos(a)*y)
                   for x,y in ((.83,0),(.91,-.065),(.975,0),(.91,.065))]
        crystal('Protective folded petal', outline, .065, p['mint'], shelter, .07)
        points = []
        for j in range(25):
            b = a+j/24*math.tau/8
            r = .875-.075*math.sin(j/24*math.pi)
            points.append((math.cos(b)*r,math.sin(b)*r,.05+.06*math.sin(j/24*math.pi)))
        tube('Scalloped prayer ribbon', points, .012, p['gold'], shelter)

    heart = model('KoharuHeart', 'koharu')
    crystal('Heart silver setting', heart_points(.78), .22, p['silver'], heart)
    crystal('Pink heart', heart_points(.64), .30, p['pink'], heart, .15)
    crystal('Penlight grip', [(-.16,-.38),(.16,-.38),(.13,-1.63),(-.13,-1.63)], .10, p['paper'], heart, -.03)
    for side in (-1,1):
        ribbon('Prayer ribbon tail', [(side*.48,-.42,0),(side*.84,-.72,.06),(side*.68,-1.10,.1),(side*1.0,-1.44,0)],
               [.11,.10,.07,.005],p['mint'],heart)
    tube('Heart glint', [(-.42,.29,.36),(-.27,.38,.40),(-.12,.31,.39)], .028, p['greenlight'], heart)

    star = model('ReiStar', 'rei')
    crystal('Midnight star setting', star_points(1), .21, p['purple'], star)
    crystal('Gold star rim', star_points(.92), .25, p['gold'], star, .08)
    crystal('Amethyst star', star_points(.78), .38, p['violet'], star, .18)
    crystal('Star bright center', [(-.17,0),(0,-.23),(.17,0),(0,.23)], .16, p['starlight'], star, .51)
    ring('Open stellar orbit', 1.17, .025, p['starlight'], star, -.04, -.28, math.pi*1.52, .62)

    link = model('ReiLink', 'rei')
    link['motion'] = 'Local X 0..1 follows only the current traveling head or resolved target pair'
    tube('Thread of voice', [(0,0,0),(1,0,0)], .10, p['starlight'], link)
    for side in (-1,1):
        points = [(i/48,side*.62*math.sin(i/48*math.pi),.17*math.sin(i/48*math.tau*2)) for i in range(49)]
        tube('Constellation ribbon', points, .038, p['gold'] if side == 1 else p['violet'], link)

    halo = model('MinaHalo', 'mina')
    for i in range(6):
        a = i * math.tau / 6
        ring('Iris opening segment', .92, .045, p['silver'], halo, .05, a+.07, a+.83)
        points = [(math.cos(a)*x-math.sin(a)*y, math.sin(a)*x+math.cos(a)*y)
                  for x,y in ((.65,-.12),(.93,-.16),(1.17,0),(.93,.16),(.65,.12))]
        crystal('Reply aperture blade', points, .16, p['cyan'], halo, .09)
        ring('Blue aperture filament', .72, .022, p['ice'], halo, .14, a, a+.75)

    cut = model('AkariCut', 'akari')
    cut['collision'] = 'Local X 0..1 spans the actual slash; Y -1..1 is its ten-unit half-width'
    crystal('Long folded slash', [(0,0),(.22,-.38),(.60,-.86),(1,0),(.62,.42),(.24,.26)], .12, p['red'], cut)
    crystal('Golden cutting face', [(0,0),(.3,-.16),(.65,-.72),(1,0),(.54,.24)], .16, p['orange'], cut, .07)
    tube('Cutting light', [(0,0,.25),(.32,-.08,.25),(.65,-.42,.25),(1,0,.25)], .055, p['fire'], cut)
    for side in (-1,1):
        ribbon('Split paper flank', [(0,0,0),(.28,side*.55,.12),(.58,side*.94,.16),(1,0,0)],
               [.005,.025,.036,.002], p['gold'], cut)

    finale = model('AkariFinale', 'akari')
    finale['collision'] = 'Radius expands 64..88 and rotates -0.3..0.3 radians in 0.22 sec; arc angles -1.35..1.35; tapered blade inside eleven-unit half-width'
    angles = [-1.35+2.7*i/72 for i in range(73)]
    points = [(math.cos(a),math.sin(a),.02) for a in angles]
    widths = [.004+.116*math.sin(math.pi*i/72)**.7 for i in range(73)]
    ribbon('Scarlet crescent back', points, widths, p['red'], finale)
    ribbon('Folded amber great blade', [(x,y,z+.045) for x,y,z in points],
           [w*.82 for w in widths], p['orange'], finale)
    tube('Incandescent crescent cutting rim', [(math.cos(a)*(1+w),math.sin(a)*(1+w),.08) for a,w in zip(angles,widths)], .009, p['fire'], finale)
    tube('Golden crescent inner rim', [(math.cos(a)*(1-w),math.sin(a)*(1-w),.065) for a,w in zip(angles,widths)], .006, p['gold'], finale)
    for i in range(6):
        a = -.85+i*.34
        outline = [(math.cos(a)*x-math.sin(a)*y,math.sin(a)*x+math.cos(a)*y)
                   for x,y in ((.93,-.08),(1.06,0),(.95,.10))]
        crystal('Swept paper flame facet', outline, .025, p['gold'], finale, .08)

    bloom = model('KoharuBloom', 'koharu')
    bloom['collision'] = 'Expanding circular wavefront at radius 1, three-unit half-width, petals trail inward'
    ring('Bloom wavefront', 1, .014, p['greenlight'], bloom, .05)
    ring('Bloom gold inner edge', .96, .010, p['gold'], bloom)
    ring('Bloom inner garland', .72, .006, p['mint'], bloom)
    for i in range(24):
        a = i*math.tau/24
        points = [(math.cos(a)*x-math.sin(a)*y, math.sin(a)*x+math.cos(a)*y)
                  for x,y in ((.62,0),(.79,-.055),(.98,0),(.79,.055))]
        crystal('Unfolding prayer petal', points, .06, p['pink'] if i%2 else p['mint'], bloom, .025)
        curve = []
        for j in range(25):
            b = a + j/24*math.tau/24
            r = .94 - .16*math.sin(j/24*math.pi)
            curve.append((math.cos(b)*r,math.sin(b)*r,.07))
        tube('Bloom scalloped gold lace', curve, .005, p['gold'], bloom)

    nova = model('ReiNova', 'rei')
    nova['collision'] = 'Starburst radius 1 maps to the 28-unit constellation impact radius'
    ring('Constellation seal', .78, .024, p['gold'], nova)
    ring('Stellar outer orbit', .97, .012, p['violet'], nova, -.06)
    crystal('Supernova amethyst core', star_points(.52), .28, p['violet'], nova, .12)
    crystal('Supernova white star', star_points(.32), .32, p['starlight'], nova, .22)
    for i in range(12):
        a = i*math.tau/12
        tip = .99 if i%3 == 0 else .86
        points = [(math.cos(a)*x-math.sin(a)*y, math.sin(a)*x+math.cos(a)*y)
                  for x,y in ((.13,0),(.43,-.13),(tip,0),(.43,.13))]
        crystal('Stellar release ray', points, .17, p['starlight'] if i%3 == 0 else p['violet'], nova, .1)

    book = model('ReiBook', 'rei')
    for side in (-1,1):
        crystal('Amethyst book cover', [(0,-.58),(side*.95,-.76),(side*.95,.64),(0,.47)], .07, p['purple'], book)
        crystal('Open ivory page', [(0,-.50),(side*.82,-.61),(side*.82,.57),(0,.43)], .08, p['paper'], book, .09)
        for line in range(5):
            y=-.38+line*.16
            tube('Engraved starlight sentence', [(side*.13,y,.19),(side*.70,y-.08,.19)], .013, p['gold'], book)
    tube('Book glowing spine', [(0,-.57,.18),(0,.51,.18)], .027, p['starlight'], book)
    return models


def main():
    for path in (SOURCE, QA, OUTPUT.parent):
        path.mkdir(parents=True, exist_ok=True)
    models = build()
    bpy.context.view_layer.update()
    bpy.ops.export_scene.gltf(filepath=str(OUTPUT), export_format='GLB', export_yup=True,
                              export_cameras=False, export_lights=False, export_extras=True)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.render.threads_mode, scene.render.threads = 'FIXED', 8
    scene.render.resolution_x, scene.render.resolution_y = 1800, 1000
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.world.color = (.10,.13,.18)
    scene.view_settings.view_transform = 'AgX'
    positions = {'MinaShield':(-4.6,1.5), 'MinaSeal':(-3.4,1.6), 'MinaBeam':(-5.5,-.2),
                 'AkariBlade':(-1,1.5), 'AkariWake':(-2,-.2), 'KoharuRing':(2.0,1.3),
                 'KoharuHeart':(2,-.7), 'ReiStar':(5,1.5), 'ReiLink':(4,-.2),
                 'MinaHalo':(-4.6,-2), 'AkariCut':(-2,-2), 'KoharuBloom':(1.8,-2),
                 'ReiNova':(4.1,-2), 'ReiBook':(5.7,-2), 'AkariFinale':(-.3,-2.0)}
    for name, obj in models.items():
        x,y = positions[name]
        obj.location = (x,0,y)
        if name in ('MinaBeam','AkariWake','ReiLink','AkariCut'):
            obj.scale = (2,.22,.22)
        if name in ('MinaSeal','KoharuHeart'):
            obj.scale = (.55,.55,.55)
        if name == 'AkariFinale':
            obj.scale = (.68,.68,.68)
    bpy.ops.object.camera_add(location=(0,-18,6))
    camera = bpy.context.object
    camera.rotation_euler = (Vector((0,0,.5))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type, camera.data.ortho_scale = 'ORTHO', 13.8
    scene.camera = camera
    for at, energy, color, size in [((-5,-6,7),1300,(.85,.94,1),7),((5,-4,3),1000,(1,.81,.60),5),((1,3,5),1600,(.57,.60,1),5)]:
        bpy.ops.object.light_add(type='AREA', location=at)
        obj = bpy.context.object
        obj.data.energy, obj.data.color, obj.data.size = energy, color, size
        obj.rotation_euler = (-obj.location).to_track_quat('-Z','Y').to_euler()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.context.preferences.filepaths.file_preview_type = 'NONE'
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'player_specials.blend'), relative_remap=False)
    scene.render.filepath = str(QA/'models.png')
    bpy.ops.render.render(write_still=True)
    report = {name: {'meshes': len(obj.children), 'vertices': sum(len(c.data.vertices) for c in obj.children),
                     'reference': obj['character_reference']} for name,obj in models.items()}
    (QA/'geometry.json').write_text(json.dumps(report,indent=2)+'\n')
    print('PLAYER_SPECIALS_COMPLETE', json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
