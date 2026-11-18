import json
import math
import random
import struct
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from build_route_backgrounds import SceneArt, rgb, build_akari
from route_blender_architecture import build_koharu, build_rei

OUT = ROOT / 'assets/opening_3d'
SOURCE = ROOT / 'assets/source/opening_3d'
random.seed(17)


def material(name, color, emission=0, metallic=0, roughness=.45):
    mat = bpy.data.materials.new(name)
    node = mat.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value = (*rgb(color), 1)
    node.inputs['Roughness'].default_value = roughness
    node.inputs['Metallic'].default_value = metallic
    node.inputs['Emission Color'].default_value = (*rgb(color), 1)
    node.inputs['Emission Strength'].default_value = emission
    return mat


def palette():
    return {name: material(name, color, emission, metal) for name, color, emission, metal in [
        ('ink', '111C30', 0, .25), ('edge', '2C405B', 0, .5), ('steel', '788B9C', 0, .65),
        ('wood', 'A18469', 0, 0), ('wall', 'AFBAC6', 0, 0), ('paper', 'DFE6E9', 0, 0),
        ('night', '081126', 0, .15), ('blue', '5A7292', 0, 0), ('cloth', '666F90', 0, 0),
        ('cyan', '73D9E9', 2.2, .1), ('amber', 'F2C77F', 1.2, .1),
        ('pink', 'D9A2D9', 1.5, .1), ('white', 'BEDCE9', .8, 0),
        ('screen', '142B41', .35, .1), ('lavender', '9B89C6', .1, 0),
        ('mint', '79C0AA', .1, 0), ('rose', 'BC7698', .1, 0),
    ]}


def keyboard(a, p, x=0, y=-.1, z=1.075, width=.95):
    a.box('Keyboard body', (x, y, z), (width, .32, .045), p['ink'], .025)
    for row in range(4):
        for col in range(13):
            a.box('Key', (x + (col - 6) * width / 14, y + (row - 1.5) * .068, z + .028),
                  (width / 16, .05, .018), p['edge'], .006)
    a.box('Space key', (x, y - .15, z + .03), (.32, .04, .018), p['steel'], .006)


def desk(a, p, x=0, y=.2, width=2.8):
    a.box('Desk top', (x, y, 1), (width, 1.25, .09), p['wood'], .035)
    a.box('Desk edge', (x, y - .61, .995), (width, .035, .055), p['paper'], .008)
    for side in (-1, 1):
        a.box('Desk leg', (x + side * (width / 2 - .12), y, .48), (.075, 1, .96), p['steel'], .015)
    for drawer in range(3):
        a.box('Drawer', (x + width / 2 - .32, y + .18, .8 - drawer * .22), (.53, .85, .2), p['edge'])
        a.box('Drawer handle', (x + width / 2 - .32, y - .26, .8 - drawer * .22), (.18, .03, .025), p['steel'], .008)


def monitor(a, p, x=0, y=.65, z=1.7, width=1.55, height=.9, avatar=False):
    a.box('Monitor stand foot', (x, y + .05, 1.08), (.48, .35, .055), p['ink'], .035)
    a.box('Monitor stand', (x, y + .1, 1.31), (.1, .1, .45), p['edge'], .025)
    a.box('Monitor bezel', (x, y, z), (width, .09, height), p['ink'], .035)
    a.box('Monitor display', (x, y - .051, z), (width - .07, .009, height - .07), p['screen'], .008)
    a.box('Display header', (x, y - .06, z + height * .4), (width - .1, .008, .055), p['edge'], .004)
    for row in range(7):
        a.box('Screen text line', (x + width * .28, y - .07, z + height * .27 - row * .085),
              (width * (.19 + .02 * (row % 3)), .008, .012), p['white'], .003)
        a.sphere('Chat avatar dot', (x + width * .14, y - .075, z + height * .27 - row * .085), (.017, .008, .017), p['pink' if row % 2 else 'cyan'])
    if avatar:
        picture(a, 'Avatar preview', ROOT / 'char/bg2/opening/op_rei_cutin_v1.png',
                (x - width * .17, y - .075, z - .01), (width * .55, height * .75), p)
    else:
        for row in range(5):
            a.box('Unsent message line', (x - width * .2, y - .065, z + .17 - row * .075),
                  (width * (.38 - row * .035), .008, .012), p['white'], .004)
        a.box('Unsent message cursor', (x - width * .29, y - .067, z - .21), (.012, .009, .055), p['cyan'], .002)


def picture(a, name, path, loc, size, p):
    mat = material(name, 'FFFFFF', .08)
    nodes = mat.node_tree.nodes
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(str(path), check_existing=True)
    shader = nodes.get('Principled BSDF')
    mat.node_tree.links.new(tex.outputs['Color'], shader.inputs['Base Color'])
    mat.node_tree.links.new(tex.outputs['Color'], shader.inputs['Emission Color'])
    mat.node_tree.links.new(tex.outputs['Alpha'], shader.inputs['Alpha'])
    mat.surface_render_method = 'DITHERED'
    x, y, z = loc
    w, h = size
    ratio = tex.image.size[0] / tex.image.size[1]
    w = min(w, h * ratio)
    h = w / ratio
    obj = a.mesh(name, [(x-w/2,y,z-h/2),(x+w/2,y,z-h/2),(x+w/2,y,z+h/2),(x-w/2,y,z+h/2)], [(0,1,2,3)], mat)
    uv = obj.data.uv_layers.new()
    for loop, coord in zip(uv.data, [(0,0),(1,0),(1,1),(0,1)]):
        loop.uv = coord


def mug(a, p, x, y, color):
    a.cylinder('Ceramic mug', (x, y, 1.17), .095, .22, p[color])
    a.cylinder('Coffee surface', (x, y, 1.285), .081, .005, p['night'])
    a.tube('Mug handle', [(x + .095 + math.sin(t*math.pi/16)*.085, y, 1.17 + math.cos(t*math.pi/16)*.075) for t in range(17)], .019, p[color])


def paper(a, p, x, y, z, angle=0):
    a.parent = a.group('Paper sheet', (x,y,z), (0,0,angle))
    a.box('Sheet', (0,0,0), (.38,.52,.006), p['paper'], .003)
    for row in range(7):
        a.box('Printed line', (-.015, .18-row*.049, .005), (.29-(row%3)*.04,.004,.002), p['blue'], 0)
    a.parent = None


def chair(a,p,x,y,color='ink'):
    a.box('Upholstered seat',(x,y,.58),(.68,.64,.14),p[color],.09)
    a.box('Chair backrest',(x,y-.32,1.04),(.65,.13,.8),p[color],.09,rotation=(.09,0,0))
    a.cylinder('Chair gas lift',(x,y,.31),.045,.45,p['steel'])
    for i in range(5):
        angle=i*math.tau/5
        dx,dy=math.cos(angle)*.42,math.sin(angle)*.42
        a.tube('Chair caster spoke',[(x,y,.15),(x+dx,y+dy,.1)],.025,p['steel'])
        a.sphere('Chair caster',(x+dx,y+dy,.07),(.055,.04,.055),p['ink'])


def room_shell(a, p, width=7, depth=6, warm=False):
    a.box('Floor', (0, 0, -.1), (width, depth, .2), p['wood' if warm else 'blue'], .025)
    for i in range(20):
        x = -width/2 + i*width/20
        a.box('Floor seam', (x, 0, .006), (.012, depth, .003), p['edge'], 0)
    a.box('Back wall', (0, depth/2, 1.6), (width,.18,3.2), p['wall'], .02)
    a.box('Right wall', (width/2, 0, 1.6), (.15,depth,3.2), p['wall'], .02)
    a.box('Left wall', (-width/2, 0, 1.6), (.15,depth,3.2), p['wall'], .02)
    a.box('Ceiling', (0,0,3.3), (width,depth,.14), p['wall'], .02)
    a.box('Skirting', (0, depth/2-.11, .1), (width,.04,.16), p['edge'], .01)


def window(a, p, x, y, width=4, height=1.7):
    z = 2.05
    a.box('Night window', (x,y,z), (width,.06,height), p['night'], .008)
    for side in (-1,1):
        a.box('Window upright', (x+side*width/2,y-.05,z), (.055,.1,height+.1), p['steel'], .008)
        a.box('Window sill', (x,y-.06,z+side*height/2), (width+.1,.14,.06), p['steel'], .008)
    for i in range(1,4):
        a.box('Window mullion', (x-width/2+i*width/4,y-.07,z), (.04,.07,height), p['edge'], .008)
    for i in range(25):
        bx=x-width*.47+i*width/25
        h=.2+random.random()*.6
        a.box('Distant building', (bx,y-.04,z-height/2+h/2), (width/28,.015,h), p['edge'], 0)
        for row in range(int(h/.08)):
            if random.random()<.7:
                a.box('Distant window light', (bx,y-.054,z-height/2+.045+row*.08), (.02,.007,.023), p['amber'], 0)


def office(a,p):
    room_shell(a,p,9,8)
    window(a,p,0,3.86,7,1.8)
    for row in range(10):
        a.box('Venetian blind', (0,3.77,2.85-row*.065), (7,.12,.023), p['blue'], .003)
    desk(a,p)
    chair(a,p,0,-.75)
    monitor(a,p)
    keyboard(a,p)
    mug(a,p,.99,.37,'paper')
    a.sphere('Mouse', (.76,-.06,1.1), (.08,.12,.035), p['ink'])
    for i in range(5): paper(a,p,-.95,.4+i*.01,1.06+i*.009,-.13+i*.025)
    for x in (-3.15,3.15):
        desk(a,p,x,1.8,2.2)
        monitor(a,p,x,2.2,1.7,1.3,.8)
        keyboard(a,p,x,1.6)
        chair(a,p,x,.85)
    for x in (-2,2):
        a.box('Ceiling panel frame',(x,.8,3.05),(1.6,.75,.08),p['edge'])
        a.box('Ceiling fluorescent light',(x,.8,3),(1.5,.65,.015),p['white'])
    for x in (-3.7,-3.05):
        a.box('Storage cabinet',(x,3.4,.7),(.6,.65,1.4),p['wall'])
        for z in (.35,.95): a.box('Cabinet handle',(x,3.06,z),(.2,.035,.025),p['steel'],.006)


def study(a,p):
    room_shell(a,p,6,6,True)
    window(a,p,-1.15,2.86,2.8,1.8)
    for side in (-1,1):
        for i in range(8):
            a.box('Curtain fold',(-1.15+side*1.43+(i-3.5)*.045,2.76,2.02),(.065,.12,1.95),p['cloth'],.028)
    desk(a,p,0,1.0,2.5)
    chair(a,p,0,.05,'cloth')
    a.box('Laptop base',(.25,1.0,1.07),(1.06,.68,.045),p['lavender'],.025)
    monitor(a,p,.25,1.3,1.5,1.06,.64,True)
    keyboard(a,p,.25,.88,1.1,.92)
    paper(a,p,-.71,.76,1.07,-.09)
    a.cylinder('Pencil',(-.95,.7,1.09),.012,.38,p['mint'],(math.pi/2,0,.12))
    a.parent=a.group('Penlight',(.88,.61,1.1),(0,math.pi/2,.23))
    a.cylinder('Penlight grip',(0,0,0),.035,.19,p['paper'])
    a.cylinder('Penlight lens',(0,0,.23),.036,.28,p['pink'])
    a.cylinder('Penlight cap',(0,0,.38),.041,.025,p['lavender'])
    a.parent=None
    a.box('Bed frame',(-2,-.1,.25),(1.5,2.5,.5),p['wood'])
    a.box('Mattress',(-2,-.1,.57),(1.43,2.4,.18),p['paper'],.085)
    a.box('Lavender duvet',(-2,-.45,.72),(1.45,1.65,.15),p['lavender'],.08)
    a.box('Pillow',(-2,.76,.75),(1.1,.5,.16),p['cloth'],.12)
    a.box('Bookcase',(2.2,1.7,1.3),(.8,.42,2.6),p['wood'])
    for level in range(4):
        z=.25+level*.58
        a.box('Bookcase shelf',(2.2,1.45,z),(.8,.55,.04),p['paper'],.008)
        for i in range(7):
            a.box('Book spine',(1.9+i*.085,1.36,z+.19),(.065,.22,.32+random.random()*.09),p[['mint','blue','rose','paper'][i%4]],.008)


def studio(a,p):
    room_shell(a,p,6,6,True)
    desk(a,p,0,1.3,2.7)
    chair(a,p,0,.2)
    monitor(a,p,0,1.75,1.72,1.75,1.02,True)
    keyboard(a,p,0,1.05)
    a.sphere('Mouse',(.72,1.02,1.1),(.075,.12,.035),p['ink'])
    mug(a,p,.98,1.58,'blue')
    a.box('Paperback',(.94,.89,1.08),(.34,.48,.07),p['paper'],.008)
    a.box('Paperback cover',(.94,.89,1.12),(.35,.49,.01),p['blue'],.004)
    a.box('Mint bookmark',(.87,.64,1.12),(.045,.1,.008),p['mint'],.002)
    points=[(-1.24,1.27,1.05),(-1.24,1.27,1.68),(-.7,.98,1.98),(-.22,.66,1.78)]
    a.tube('Microphone boom',points,.025,p['ink'])
    for v in points: a.sphere('Boom joint',v,(.044,.044,.044),p['steel'])
    a.cylinder('Microphone body',(-.22,.66,1.66),.068,.25,p['ink'])
    a.sphere('Microphone capsule',(-.22,.66,1.8),(.071,.071,.065),p['steel'])
    for i in range(12):
        z=1.59+i*.018
        a.tube('Microphone grille', [(-.22+math.cos(t*math.tau/24)*.07,.66+math.sin(t*math.tau/24)*.07,z) for t in range(25)],.004,p['edge'])
    a.tube('Microphone cable',[(-.2,.66,1.52),(-.25,.68,1.3),(-.72,.88,1.1),(-1.24,1.27,1.04),(-1.37,1.34,.3)],.009,p['ink'])
    a.box('Bed frame',(-2.1,.4,.25),(1.45,2.5,.5),p['wood'])
    a.box('Checked duvet',(-2.1,.1,.59),(1.43,1.7,.18),p['blue'],.085)
    for i in range(8): a.box('Duvet stripe',(-2.72+i*.18,.1,.688),(.025,1.64,.002),p['paper'],0)
    for i in range(10): a.box('Duvet cross stripe',(-2.1,-.65+i*.17,.691),(1.35,.02,.002),p['cloth'],0)
    a.box('Pillow',(-2.1,1.24,.62),(1.06,.5,.2),p['cloth'],.1)
    window(a,p,-1.8,2.86,1.8,1.25)
    a.box('Closet',(2.45,2,1.5),(.7,1.8,3),p['wood'])
    for y in (1.65,2.2): a.box('Closet handle',(2.07,y,1.4),(.035,.025,.2),p['steel'],.008)


def phone(a,p):
    a.box('Phone machined frame',(0,0,0),(1.38,.16,2.8),p['steel'],.11)
    a.box('Phone ceramic back',(0,.043,0),(1.33,.1,2.74),p['ink'],.1)
    a.box('Phone display glass',(0,-.088,0),(1.29,.018,2.67),p['night'],.085)
    a.box('Earpiece',(0,-.104,1.16),(.25,.009,.028),p['edge'],.012)
    a.sphere('Front lens',(.22,-.105,1.16),(.023,.005,.023),p['blue'])
    a.box('Home gesture',(0,-.105,-1.2),(.37,.008,.014),p['white'],.006)
    for row in range(4):
        for col in range(5):
            a.box('Kana key',((col-2)*.22,-.106,-.64-row*.12),(.19,.008,.095),p['edge'],.014)
    for i in range(4): a.box('Status signal',(.37+i*.035,-.106,1.24),(.018,.008,.025+i*.014),p['white'],.002)
    a.box('Header rule',(0,-.106,.83),(1.13,.006,.006),p['edge'],0)
    a.box('Composer rule',(0,-.106,-.43),(1.13,.006,.006),p['edge'],0)
    a.box('Side key',(.698,0,.57),(.025,.07,.3),p['edge'],.01)
    a.box('Volume rocker',(-.698,0,.52),(.025,.07,.4),p['edge'],.01)


def signal(a,p):
    for side in (-1,1):
        for j in range(9):
            y= j*3.6
            x=side*(4.2+math.sin(j*.8)*.8)
            a.box('Timeline plinth',(x,y,-.15),(2.9,2.8,.2),p['ink'],.03)
            a.box('Timeline pane',(x,y,2.3),(2.8,.1,4.4),p['screen'],.065)
            for dx in (-1.45,1.45):
                a.box('Pane vertical edge',(x+dx,y-.07,2.3),(.025,.02,4.5),p['cyan'],.006)
            for z in (.05,4.55): a.box('Pane top edge',(x,y-.07,z),(2.9,.02,.025),p['cyan'],.006)
            a.sphere('Profile circle',(x-.92,y-.09,3.92),(.19,.024,.19),p[['amber','pink','cyan'][j%3]])
            for row in range(6):
                a.box('Post text relief',(x+.02,y-.09,3.25-row*.36),(1.95-(row%3)*.27,.035,.055),p['white'],.015)
            for k in range(3):
                a.sphere('Post reaction',(x-.8+k*.65,y-.09,.65),(.09,.028,.07),p[['rose','mint','lavender'][k]])
            a.box('Depth pillar',(x+side*1.8,y,2.7),(.2,.35,5.8),p['edge'],.025)
    a.box('Reflective channel',(0,13,-.36),(13,44,.15),p['ink'],.025)
    for x in (-3,-2.8,2.8,3): a.box('Signal rail',(x,13,-.25),(.025,44,.015),p['cyan'],.003)
    for j in range(18): a.box('Channel crosspiece',(0,j*2,-.23),(5.6,.018,.014),p['edge'],.001)
    a.box('Unopened threshold',(0,34,2.4),(3.6,.18,5.2),p['night'],.08)
    for x in (-1.9,1.9): a.box('Threshold edge',(x,33.88,2.4),(.035,.04,5.4),p['cyan'],.006)
    a.box('Threshold lintel',(0,33.88,5.1),(3.8,.04,.035),p['cyan'],.006)
    a.box('Threshold seam',(0,33.88,2.4),(.014,.03,5.2),p['white'],.003)


def envelope(a,p):
    a.box('Envelope body',(0,0,0),(1.5,.06,.93),p['paper'],.025)
    a.tube('Envelope folded edge',[(-.72,-.038,.43),(0,-.04,-.08),(.72,-.038,.43)],.011,p['blue'])
    a.tube('Envelope lower fold',[(-.72,-.039,-.43),(-.1,-.04,.03),(.1,-.04,.03),(.72,-.039,-.43)],.007,p['steel'])
    a.box('Unbroken seal',(0,-.046,-.08),(.16,.018,.16),p['amber'],.025,rotation=(0,math.pi/4,0))


def star(a,p):
    points=[]
    for depth in (-.09,.09):
        for i in range(10):
            angle=math.pi/2+i*math.pi/5
            radius=.7 if i%2==0 else .3
            points.append((math.cos(angle)*radius,depth,math.sin(angle)*radius))
    faces=[tuple(range(9,-1,-1)),tuple(range(10,20))]
    faces += [(i,(i+1)%10,(i+1)%10+10,i+10) for i in range(10)]
    a.mesh('Star',points,faces,p['amber'],.025)


def penlight(a,p):
    a.cylinder('Penlight grip',(0,0,-.3),.095,.6,p['paper'])
    a.cylinder('Penlight lens',(0,0,.4),.1,.8,p['pink'])
    a.cylinder('Penlight cap',(0,0,.82),.11,.05,p['lavender'])
    a.cylinder('Penlight collar',(0,0,.02),.11,.1,p['lavender'])


def akari_realm(a,p):
    build_akari(a,0)


def koharu_realm(a,p):
    build_koharu(a,0)


def rei_realm(a,p):
    build_rei(a,0)


def export(name, build):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    a=SceneArt()
    p=palette()
    build(a,p)
    curves=[o for o in bpy.context.scene.objects if o.type in {'CURVE','FONT'}]
    bpy.ops.object.select_all(action='DESELECT')
    for obj in curves: obj.select_set(True)
    if curves:
        bpy.context.view_layer.objects.active=curves[0]
        bpy.ops.object.convert(target='MESH')
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / f'{name}.blend'))
    bpy.ops.export_scene.gltf(filepath=str(OUT / f'{name}.glb'),export_format='GLB',
        export_apply=True,export_cameras=False,export_lights=False,export_yup=True,export_animations=False)
    return {'name':name,'objects':len(bpy.context.scene.objects),'glb_bytes':(OUT/f'{name}.glb').stat().st_size}


if __name__=='__main__':
    OUT.mkdir(parents=True,exist_ok=True)
    SOURCE.mkdir(parents=True,exist_ok=True)
    (SOURCE/'.gdignore').touch()
    choices=[('phone',phone),('office',office),('study',study),('studio',studio),('signal',signal),('envelope',envelope),('star',star),('penlight',penlight),('akari_realm',akari_realm),('koharu_realm',koharu_realm),('rei_realm',rei_realm)]
    selected=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    reports=[export(name,build) for name,build in choices if not selected or name in selected]
    assets=[]
    for path in sorted(OUT.glob('*.glb')):
        blob=path.read_bytes()
        length=struct.unpack_from('<I',blob,12)[0]
        document=json.loads(blob[20:20+length])
        assets.append({'name':path.stem,'nodes':len(document.get('nodes',[])),'bytes':len(blob)})
    (SOURCE/'manifest.json').write_text(json.dumps({'references':['char/bg2/opening/op_akari_v1.png','char/bg2/opening/op_koharu_v1.png','char/bg2/opening/op_rei_v1.png','char/bg2/prologue/timeline_depth_v2.png','tools/build_route_backgrounds.py','tools/route_blender_architecture.py'],'assets':assets},indent=2),encoding='utf-8')
    print('OPENING_ASSETS_COMPLETE '+json.dumps(reports),flush=True)
