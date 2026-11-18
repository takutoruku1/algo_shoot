import argparse
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_road_projectiles as g

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'assets/boss_projectiles'
SOURCE = ROOT / 'assets/source/boss_projectiles'
QA = ROOT / 'build/qa_story/boss3d_20261029'
NAMES = [
    'akari_docs', 'akari_sticky', 'akari_reply_plane', 'akari_envelope', 'akari_clip', 'akari_stamp',
    'akari_rain_drop', 'akari_splash', 'akari_parcel', 'akari_outbox', 'akari_approval_seal', 'akari_address_tag', 'akari_crystal',
    'koharu_penlight', 'koharu_acrylic', 'koharu_badge', 'koharu_star_pin', 'koharu_ticket', 'koharu_uchiwa',
    'koharu_petal', 'koharu_prayer_tag', 'koharu_clap', 'koharu_ribbon', 'koharu_heart_charm', 'koharu_crystal',
    'rei_subscriber', 'rei_comment', 'rei_film', 'rei_microphone', 'rei_bookmark', 'rei_clapper', 'rei_open_book', 'rei_crystal',
    'mina_eraser', 'mina_memory', 'mina_unanswered', 'mina_butterfly', 'mina_folded_note', 'mina_photo',
    'mina_memory_drawer', 'mina_snapshot_pair', 'mina_reply_star', 'mina_pendulum', 'mina_feather', 'mina_crystal',
    'akari_unsent_card', 'akari_read_receipt', 'akari_memo_strip', 'akari_curled_memo', 'koharu_gaze_eye', 'rei_quote_pin',
]
REUSE = {'akari_docs': 'akari_docs', 'akari_sticky': 'akari_sticky', 'akari_reply_plane': 'akari_reply_plane',
         'akari_envelope': 'enemy_akari_unsent',
         'koharu_penlight': 'enemy_koharu_cheer', 'koharu_ticket': 'koharu_ticket', 'koharu_prayer_tag': 'koharu_prayer_tag',
         'rei_film': 'enemy_rei_clipper', 'mina_eraser': 'mina_eraser', 'mina_unanswered': 'mina_unanswered',
         'mina_folded_note': 'mina_folded_note'}


def heart(name, center, size, surface, depth=.15):
    x, y, z = center
    points = []
    for angle in np.linspace(0, math.tau, 48, endpoint=False):
        points.append((x + math.sin(angle)**3*size, y + (13*math.cos(angle)-5*math.cos(2*angle)-2*math.cos(3*angle)-math.cos(4*angle))/17*size))
    return g.prism(name, points, depth, surface, z, .035)


def gem(name, points, surface, z=0, depth=.3):
    middle = (sum(x for x, y in points)/len(points), sum(y for x, y in points)/len(points))
    n = len(points)
    vertices = [(x, y, z) for x, y in points] + [(middle[0], middle[1], z+depth), (middle[0], middle[1], z-depth*.55)]
    return g.mesh(name, vertices, [(i, (i+1) % n, n) for i in range(n)] + [((i+1) % n, i, n+1) for i in range(n)], surface)


def photo(center, scale=1, angle=0):
    x, y, z = center
    points = [(-.65, -.8), (.65, -.8), (.65, .8), (-.65, .8)]
    def moved(vertices):
        return [(x + scale*(a*math.cos(angle)-b*math.sin(angle)), y+scale*(a*math.sin(angle)+b*math.cos(angle))) for a, b in vertices]
    g.prism('Photo paper border', moved(points), .11, p['paper'], z)
    g.prism('Photo violet sky', moved([(-.52, -.48), (.52, -.48), (.52, .66), (-.52, .66)]), .025, p['violet'], z+.065)
    g.prism('Remembered mountains', moved([(-.52, -.48), (-.52, -.12), (-.1, .44), (.12, .08), (.32, .35), (.52, -.1), (.52, -.48)]), .03, p['lavender'], z+.09)
    g.cylinder('Photo sun', (x+.28*scale, y+.40*scale, z+.12), .12*scale, .03, p['gold'], 24)


def picture_material():
    material = bpy.data.materials.new('Existing Koharu acrylic print')
    material.use_nodes = True
    nodes = material.node_tree.nodes
    shader = nodes.get('Principled BSDF')
    texture = nodes.new('ShaderNodeTexImage')
    texture.image = bpy.data.images.load(str(ROOT / 'char/v3/bullets/koharu_acrylic.png'))
    texture.image.pack()
    transparent = nodes.new('ShaderNodeBsdfTransparent')
    mix = nodes.new('ShaderNodeMixShader')
    links = material.node_tree.links
    links.new(texture.outputs['Color'], shader.inputs['Base Color'])
    links.new(texture.outputs['Alpha'], mix.inputs[0])
    links.new(transparent.outputs[0], mix.inputs[1])
    links.new(shader.outputs[0], mix.inputs[2])
    links.new(mix.outputs[0], nodes.get('Material Output').inputs['Surface'])
    return material


def build(name):
    if name in REUSE:
        root = g.build(REUSE[name])
        root.name = name
        return root
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)
    g.CURRENT = root
    if name == 'akari_clip':
        points = [(-.48,-.48,0),(-.48,.54,0),(0,.94,0),(.48,.54,0),(.48,-.55,0),(.12,-.90,0),(-.24,-.55,0),(-.24,.36,0),(0,.57,0),(.24,.36,0),(.24,-.40,0)]
        clip = g.tube('Bent steel paperclip', points, .09, p['steel'])
        clip.data.splines.clear()
        spline = clip.data.splines.new('BEZIER')
        spline.bezier_points.add(len(points)-1)
        for point, position in zip(spline.bezier_points, points):
            point.co = g.xyz(position)
            point.handle_left_type = point.handle_right_type = 'AUTO'
    elif name == 'akari_stamp':
        g.paper('Stamped document', p['paper'])
        g.ring('Red stamp circle', (0,0,.1), .52,.52,p['red'],.05)
        for i in range(4):
            a = i*math.pi/2
            g.prism('Stamp petal', [(0,0),(.22*math.cos(a-.4),.22*math.sin(a-.4)),(.40*math.cos(a),.40*math.sin(a)),(.22*math.cos(a+.4),.22*math.sin(a+.4))], .045,p['red'],.115)
    elif name == 'akari_unsent_card':
        g.prism('Unsent message', [(-.94,.58),(.94,.5),(.89,-.42),(.73,-.43),(.83,-.77),
                                  (.45,-.46),(-.92,-.46)], .22, p['ice'])
        for y, width in ((.28,1.32),(.02,1.53),(-.23,.80)):
            g.box('Embossed draft line',(-.7+width/2,y,.12),(width,.08,.065),p['blue'])
    elif name == 'akari_read_receipt':
        g.prism('Receipt with torn edge',[(-.85,.77),(.49,.77),(.86,.40),(.86,-.61),(.60,-.78),
            (.36,-.61),(.12,-.78),(-.12,-.61),(-.36,-.78),(-.60,-.61),(-.85,-.75)],.18,p['pink'])
        gem('Folded corner',[(.49,.77),(.49,.40),(.86,.40)],p['rose'],.11,.08)
        for x in (-.36,.10):
            g.tube('Raised read check',[(x-.23,-.02,.15),(x-.07,-.19,.15),(x+.25,.23,.15)],.068,p['red'])
    elif name == 'akari_memo_strip':
        g.prism('Directional memo strip',[(-1,.42),(.56,.42),(1,0),(.56,-.42),(-1,-.42),(-.76,0)],.22,p['gold'])
        g.prism('Folded tab',[(-.71,.31),(-.43,.31),(-.57,0),(-.43,-.31),(-.71,-.31),(-.54,0)],.06,p['orange'],.13)
        for y, width in ((.11,.69),(-.11,.43)):
            g.box('Memo writing',(.08+width/2,y,.14),(width,.06,.04),p['rose'])
    elif name == 'akari_curled_memo':
        points=[(-.88,-.78),(-.57,-.62),(-.38,-.45),(-.28,-.17)]
        for t in np.linspace(0,1,36):
            angle=math.pi-t*math.tau*1.30
            radius=.66-.49*t
            points.append((.31+radius*math.cos(angle),.20+radius*math.sin(angle)))
        vertices=[]
        for i,(x,y) in enumerate(points):
            dx,dy=np.subtract(points[min(i+1,len(points)-1)],points[max(i-1,0)])
            scale=.10/math.hypot(dx,dy)
            for a,z in ((1,-.18),(-1,-.18),(-1,.18),(1,.18)):
                vertices.append((x-a*dy*scale,y+a*dx*scale,z))
        faces=[(0,1,2,3),tuple(range(len(vertices)-1,len(vertices)-5,-1))]
        faces.extend((4*i+j,4*i+(j+1)%4,4*i+4+(j+1)%4,4*i+4+j) for i in range(len(points)-1) for j in range(4))
        g.mesh('Coiled memo with cut paper edges',vertices,faces,p['mint'],.035)
    elif name == 'koharu_gaze_eye':
        g.cylinder('Gaze medallion',(0,0,0),.98,.32,p['violet'])
        g.ring('Metal rim',(0,0,.17),.88,.88,p['lavender'],.07)
        gem('Open eyelids',[(-.80,0),(-.40,.29),(0,.38),(.40,.29),(.80,0),(.40,-.25),(0,-.32),(-.40,-.25)],p['paper'],.19,.14)
        g.cylinder('Iris',(0,0,.35),.27,.08,p['violet'])
        g.cylinder('Pupil',(0,0,.405),.12,.04,p['navy'])
        g.cylinder('Eye glint',(-.08,.09,.44),.055,.025,p['paper'])
    elif name == 'rei_quote_pin':
        g.prism('Pinned quote',[(-.90,.48),(.66,.48),(.89,.27),(.89,-.54),(.08,-.54),(-.43,-.91),(-.43,-.54),(-.90,-.54)],.20,p['lavender'])
        g.box('Quote face',(0,-.04,.13),(1.58,.80,.08),p['paper'])
        for x in (-.38,.16):
            g.prism('Quotation mark',[(x-.13,.16),(x+.12,.16),(x+.12,-.08),(x-.08,-.25),(x-.17,-.17),(x-.04,-.07),(x-.13,-.07)],.06,p['violet'],.21)
        g.cylinder('Pin head',(.40,.58,.31),.19,.23,p['rose'])
        g.tube('Steel pin',[(.40,.55,.23),(.32,.21,.20)],.045,p['steel'])
    elif name == 'akari_rain_drop':
        rings = [(-1.15,.015),(-.55,.14),(.05,.34),(.48,.38),(.74,.25),(.85,.015)]
        vertices = [(x, math.cos(a)*r, math.sin(a)*r) for x,r in rings for a in np.linspace(0,math.tau,20,endpoint=False)]
        faces = [(i*20+j,i*20+(j+1)%20,(i+1)*20+(j+1)%20,(i+1)*20+j) for i in range(len(rings)-1) for j in range(20)]
        g.mesh('Polished teardrop',vertices,faces,p['ice'])
        g.tube('Rain bright ridge',[(-.65,0,.10),(-.15,.08,.3),(.43,.1,.37),(.68,.08,.2)],.025,p['paper'])
    elif name == 'akari_splash':
        g.cylinder('Splash crown pool',(0,-.55,0),.78,.12,p['violet'],48)
        for i in range(5):
            x = -.72 + i*.36
            gem('Rebound splash peak',[(x-.2,-.55),(x+.2,-.55),(x+.04,.6+(i%2)*.32)],p['pink'],.10,.22)
    elif name == 'akari_parcel':
        g.box('Office parcel',(0,0,0),(1.3,1.3,1.3),p['rose'],.06)
        g.box('Paper lid',(0,.64,0),(1.35,.08,1.35),p['paper'],.03)
        g.box('Packing tape',(0,.69,0),(.22,.035,1.35),p['gold'],.01)
        g.box('Front tape',(0,.43,.67),(.22,.43,.035),p['gold'],.01)
        g.box('White address label',(-.20,-.16,.68),(.72,.41,.04),p['paper'],.025)
        for y in (-.10,-.22): g.box('Parcel address line',(-.20,y,.71),(.48,.035,.014),p['rose'],.005)
    elif name == 'akari_outbox':
        g.box('Outbox bottom',(0,-.62,0),(1.65,.23,.85),p['violet'])
        for side in (-1,1): g.box('Outbox side',(side*.75,-.4,0),(.18,.55,.85),p['lavender'])
        g.box('Outbox back',(0,-.35,-.36),(1.6,.66,.15),p['violet'])
        g.paper('Queued letter',p['paper'],z=.02,width=1.05,height=1.45)
        for y in (.10,.38): g.box('Letter line',(0,y,.09),(.74,.055,.025),p['rose'])
        for x in (-.16,.16): g.box('Tray clasp',(x,-.58,.48),(.07,.28,.08),p['gold'])
    elif name == 'akari_approval_seal':
        points=[(math.cos(i*math.pi/12)*(.94 if i%2==0 else .78),math.sin(i*math.pi/12)*(.94 if i%2==0 else .78)) for i in range(24)]
        g.prism('Approval medal rim',points,.16,p['gold'])
        g.cylinder('Approval enamel',(0,0,.12),.64,.08,p['paper'])
        g.tube('Embossed approval check',[(-.32,0,.22),(-.08,-.23,.22),(.36,.29,.22)],.075,p['steel'])
        for side in (-1,1): g.prism('Seal ribbon',[(side*.18,-.56),(side*.58,-.51),(side*.62,-1.05),(side*.34,-.90),(side*.21,-1.1)],.07,p['rose'],-.1)
    elif name == 'akari_address_tag':
        g.prism('Address label',[(-.8,.30),(-.42,.83),(.7,.63),(.87,-.60),(-.28,-.8)],.18,p['pink'])
        g.cylinder('Tag eyelet',(-.41,.44,.13),.12,.08,p['gold'])
        g.cylinder('Eyelet hole',(-.41,.44,.19),.065,.01,p['navy'])
        for y in (-.02,-.32): g.box('Address line',(.15,y,.15),(.68,.08,.04),p['rose'])
        g.tube('Label loop',[(-.42,.44,0),(-.83,.86,0),(-.56,1.10,0),(-.29,.86,0),(-.42,.44,0)],.07,p['steel'])
    elif name in ('koharu_badge','koharu_star_pin'):
        if name == 'koharu_badge':
            g.cylinder('Round gold badge',(0,0,0),.85,.22,p['gold'])
            g.cylinder('Orange enamel',(0,0,.16),.70,.12,p['orange'])
        else:
            g.star('Star pin setting',(0,0,0),1,p['gold'],.23)
            g.star('Star enamel',(0,0,.16),.8,p['orange'],.10)
        heart('Pink enamel heart',(0,.05,.26),.46,p['pink'],.10)
        g.box('Pin back',(0,0,-.19),(.65,.10,.1),p['steel'])
    elif name == 'koharu_acrylic':
        g.box('Clear acrylic foot',(0,-.88,-.02),(1.05,.16,.66),p['ice'],.06)
        g.box('Acrylic upright',(0,-.02,-.05),(.82,1.70,.10),p['ice'],.09)
        obj=g.mesh('Original character print',[(-.59,-.82,.035),(.59,-.82,.035),(.59,.94,.035),(-.59,.94,.035)],[(0,1,2,3)],picture_material())
        uv=obj.data.uv_layers.new()
        for item,coordinate in zip(uv.data,[(0,0),(1,0),(1,1),(0,1)]): item.uv=coordinate
    elif name == 'koharu_uchiwa':
        g.box('Fan handle',(0,-.70,0),(.16,.9,.12),p['rose'])
        g.cylinder('Uchiwa gold edge',(0,.18,0),.72,.12,p['gold'])
        g.cylinder('Uchiwa red face',(0,.18,.08),.64,.04,p['red'])
        heart('White heart',(0,.24,.13),.45,p['paper'],.025)
        for x in (-.35,-.18,0,.18,.35): g.tube('Fan ribs',[(0,-.38,-.10),(x,.35,-.10)],.018,p['gold'])
    elif name == 'koharu_petal':
        gem('Outer folded petal',[(0,-1),(-.68,-.1),(-.56,.72),(0,.36),(.52,.91),(.62,-.12)],p['pink'],0,.28)
        gem('Inner petal',[(0,-.9),(-.27,.43),(0,.75),(.22,.4)],p['paper'],.14,.16)
    elif name == 'koharu_clap':
        outline=[(-.55,-.72),(-.73,-.11),(-.88,.14),(-.76,.27),(-.51,-.03),(-.52,.76),(-.35,.83),(-.26,.14),(-.08,.98),(.10,.95),(.08,.16),(.33,.86),(.51,.79),(.34,.02),(.68,.54),(.81,.42),(.56,-.43),(.32,-.73)]
        g.prism('Applause hand',outline,.19,p['paper'],rounding=.045)
        g.box('Pink cuff',(-.08,-.78,0),(.97,.32,.27),p['rose'])
        g.tube('Palm fold',[(-.38,-.15,.12),(-.16,-.28,.14),(.16,-.22,.13)],.022,p['orange'])
    elif name == 'koharu_ribbon':
        for side in (-1,1):
            gem('Folded ribbon bow',[(side*.10,.10),(side*.85,.65),(side*.98,-.21),(side*.24,-.29)],p['mint'],0,.28)
            g.prism('Ribbon tail',[(side*.15,-.16),(side*.43,-.10),(side*.70,-.93),(side*.40,-.72),(side*.23,-1)],.07,p['mint'],-.08)
        g.box('Ribbon clasp',(0,0,.15),(.37,.55,.28),p['gold'],.10)
    elif name == 'koharu_heart_charm':
        heart('Pendant gold mount',(0,-.10,0),.81,p['gold'],.24)
        heart('Enamel heart jewel',(0,-.08,.18),.66,p['pink'],.14)
        g.ring('Charm eyelet',(0,.88,0),.13,.18,p['gold'],.045)
    elif name in ('rei_subscriber','rei_comment'):
        points=([(-.95,-.4),(-.8,.36),(-.15,.74),(.87,.47),(.62,.03),(.98,-.35),(.02,-.67)] if name=='rei_subscriber'
                else [(-.83,-.46),(-.46,-.1),(-.86,.47),(0,.82),(.86,.46),(.45,-.05),(.83,-.45),(0,-.63)])
        g.prism('Violet faceted quotation',points,.26,p['violet'])
        gem('Raised quotation face',[(x*.78,y*.78) for x,y in points],p['lavender'],.16,.20)
        g.star('Quoted gold star',(0,.02,.39),.46,p['gold'],.10)
    elif name == 'rei_microphone':
        g.prism('Microphone grip',[(-.21,-1),(.21,-1),(.28,.1),(-.28,.1)],.30,p['violet'])
        g.cylinder('Microphone collar',(0,.05,.04),.35,.15,p['gold'])
        gem('Microphone head',[(-.47,.29),(-.28,.91),(.28,.91),(.47,.29),(0,.05)],p['violet'],.04,.38)
        for y in (.3,.46,.62,.78): g.tube('Microphone grille',[(-.32,y,.28),(.32,y,.28)],.013,p['lavender'])
        g.star('Mic badge',(0,-.32,.2),.22,p['gold'],.045)
    elif name == 'rei_bookmark':
        g.prism('Bookmark ribbon',[(-.52,.95),(.52,.95),(.52,-.95),(0,-.60),(-.52,-.95)],.13,p['violet'])
        g.prism('Bookmark stripe',[(-.11,.95),(.11,.95),(.11,-.73),(0,-.60),(-.11,-.73)],.04,p['lavender'],.09)
        g.star('Bookmark star',(0,.16,.16),.43,p['gold'],.10)
    elif name == 'rei_clapper':
        g.box('Slate body',(0,-.19,0),(1.7,1.05,.21),p['violet'])
        g.box('Clapper rail',(0,.53,0),(1.8,.3,.24),p['lavender'])
        for i in range(4):
            x=-.9+i*.46
            g.prism('Slate stripe',[(x,.39),(x+.20,.39),(x+.38,.68),(x+.18,.68)],.025,p['paper'],.14)
        for y in (-.11,-.38): g.box('Slate note',(-.13,y,.14),(1.1,.07,.03),p['lavender'])
        g.cylinder('Slate hinge',(-.72,.30,.18),.09,.055,p['gold'],24)
    elif name == 'rei_open_book':
        for side in (-1,1):
            g.prism('Book cover',[(0,-.78),(side*.95,-.91),(side*.95,.77),(0,.91)],.17,p['violet'])
            gem('Open book pages',[(side*.06,-.66),(side*.85,-.76),(side*.85,.66),(side*.06,.78)],p['paper'],.13,.14)
            for y in (-.25,.03,.31): g.tube('Printed prose',[(side*.20,y+.05,.31),(side*.68,y-.05,.30)],.022,p['rose'])
        g.tube('Book binding',[(0,-.79,.12),(0,.80,.12)],.055,p['gold'])
    elif name == 'mina_memory':
        g.prism('Unsent folded letter',[(-.88,-.54),(.83,-.62),(.79,.64),(-.81,.72)],.10,p['rose'])
        for points in [[(-.8,-.5),(0,.03),(.77,-.57)],[(-.78,.67),(.76,.59),(0,-.06)]]:
            gem('Folded unsent paper',points,p['paper'],.1,.10)
        g.tube('Letter clasp',[(-.12,.17,.22),(.1,-.19,.22)],.045,p['gold'])
    elif name == 'mina_butterfly':
        for side in (-1,1):
            gem('Upper memory wing',[(side*.05,.03),(side*.98,.84),(side*.82,.13),(side*.30,-.16)],p['paper'],0,.25)
            gem('Lower memory wing',[(side*.09,-.08),(side*.71,-.31),(side*.58,-.93),(side*.17,-.41)],p['lavender'],0,.18)
        g.prism('Butterfly spine',[(-.09,-.64),(.09,-.64),(.05,.53),(-.05,.53)],.16,p['rose'],.15)
    elif name == 'mina_photo':
        photo((0,0,0))
    elif name == 'mina_snapshot_pair':
        photo((-.25,.16,-.1),.78,.13)
        photo((.25,-.20,.13),.78,-.10)
    elif name == 'mina_memory_drawer':
        g.box('Memory cabinet',(0,0,0),(1.5,1.55,.68),p['rose'])
        g.box('Drawer face',(0,-.26,.40),(1.32,.70,.19),p['pink'])
        g.box('Drawer label',(0,-.25,.52),(.62,.26,.04),p['navy'])
        g.box('Drawer handle',(0,-.25,.59),(.45,.09,.1),p['steel'])
        g.prism('Memory note',[(-.62,.48),(-.56,.92),(.59,.98),(.65,.48)],.06,p['paper'],.08)
        g.tube('Note graph',[(-.36,.68,.16),(-.15,.84,.16),(.02,.64,.16),(.25,.87,.16)],.025,p['lavender'])
    elif name == 'mina_reply_star':
        g.star('Reply star frame',(0,0,0),1,p['gold'],.18)
        points=[(math.cos(math.pi/2+i*math.pi/5)*(.83 if i%2==0 else .37),math.sin(math.pi/2+i*math.pi/5)*(.83 if i%2==0 else .37)) for i in range(10)]
        gem('Reply star facets',points,p['paper'],.12,.30)
        g.tube('Star reply mark',[(-.26,.03,.42),(0,-.15,.43),(.25,.07,.42)],.032,p['rose'])
    elif name == 'mina_pendulum':
        g.ring('Pendulum bail',(0,.85,0),.17,.22,p['gold'],.055)
        gem('Memory pendulum',[(-.43,.48),(.43,.48),(.67,-.35),(0,-1),(-.67,-.35)],p['lavender'],0,.38)
        gem('Pendulum inset',[(-.22,.18),(.22,.18),(.30,-.29),(0,-.62),(-.30,-.29)],p['pink'],.39,.1)
        g.cylinder('Pendulum gold core',(0,-.18,.52),.17,.05,p['gold'],8)
    elif name == 'mina_feather':
        points=[(-.58,-.92),(-.48,-.27),(-.77,.01),(-.38,.15),(-.48,.57),(-.09,.43),(.28,.98),(.62,.37),(.43,.18),(.65,-.04),(.18,-.36),(.12,-.70)]
        gem('Folded dragon feather',points,p['lavender'],0,.22)
        g.tube('Feather spine',[(-.66,-1,.15),(-.18,-.14,.25),(.31,.88,.1)],.045,p['paper'])
    elif name.endswith('_crystal'):
        character=name.split('_')[0]
        surface=p[{'akari':'gold','koharu':'mint','rei':'violet','mina':'ice'}[character]]
        if character=='akari':
            gem('Hot amber letter spear',[(-.5,-.4),(.95,0),(-.5,.4),(-.76,0)],surface,0,.27)
            for side in (-1,1): g.tube('Folded flame ribbon',[(-.6,side*.22,0),(-1.0,side*.50,.05),(-1.4,side*.35,0)],.10,p['orange'])
        elif character=='koharu':
            heart('Support heart setting',(.12,0,0),.69,p['mint'],.22)
            gem('Heart central jewel',[(-.36,.32),(.16,.52),(.54,.26),(.13,-.52)],p['ice'],.18,.22)
            for side in (-1,1): g.prism('Heart ribbon',[(-.42,side*.08),(-1.06,side*.32),(-.83,side*.05),(-1.02,-side*.17)],.08,p['mint'])
        elif character=='rei':
            g.star('Streaming star',(.12,0,0),.72,p['violet'],.2)
            g.star('Live gold star',(.12,0,.16),.49,p['gold'],.12)
            for y in (-.23,.21): g.tube('Split stream tail',[(-.18,y,0),(-.72,y*1.7,.06),(-1.34,y*1.4,0)],.075,p['lavender'])
        else:
            gem('Blue memory lancet',[(-1.18,0),(-.15,.31),(1.05,0),(-.15,-.31)],surface,0,.19)
            g.ring('Memory iris',(-.07,0,.18),.29,.29,p['steel'],.05)
            gem('Memory crystal core',[(-.31,0),(-.07,.23),(.18,0),(-.07,-.23)],p['ice'],.22,.2)
    else:
        raise ValueError(name)
    return root


def normalize(root):
    bpy.context.view_layer.update()
    graph=bpy.context.evaluated_depsgraph_get()
    vertices=[]
    for child in root.children:
        evaluated=child.evaluated_get(graph)
        mesh=evaluated.to_mesh()
        vertices.extend(child.matrix_world @ v.co for v in mesh.vertices)
        evaluated.to_mesh_clear()
    low=Vector(tuple(min(v[i] for v in vertices) for i in range(3)))
    high=Vector(tuple(max(v[i] for v in vertices) for i in range(3)))
    center=(low+high)/2
    radius=max((v-center).length for v in vertices)
    for child in root.children:
        child.location=(child.location-center)/radius
        child.scale/=radius


def pose(root, name, phase):
    angle=phase*math.tau
    if name.endswith('_crystal') or name in ('akari_reply_plane','akari_rain_drop'):
        root.rotation_euler=(angle,.12*math.sin(angle),.12*math.sin(angle*2))
    else:
        root.rotation_euler=(.22+.32*math.sin(angle),.16*math.sin(angle*2),angle)


parser=argparse.ArgumentParser()
parser.add_argument('--only', choices=NAMES, nargs='+')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
for folder in (OUTPUT,SOURCE,QA): folder.mkdir(parents=True,exist_ok=True)
(SOURCE/'.gdignore').touch()
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
p={name:g.material(name,color,metal) for name,color,metal in [
    ('paper','fff0da',.03),('ice','b5e9fa',.45),('blue','6c91b1',.45),('navy','252a43',.35),
    ('steel','bccde3',.8),('red','d44961',.2),('rose','bd6c8e',.25),('pink','f7b8d2',.3),
    ('lavender','bca9f0',.35),('violet','7350b5',.6),('gold','f4d17d',.75),('orange','f5ac55',.35),('mint','a7efd8',.4),
]}
g.PALETTE=p
models={}
for name in NAMES:
    models[name]=build(name)
    normalize(models[name])
    for child in models[name].children: child.hide_render=True
scene=bpy.context.scene
scene.render.engine='CYCLES'
scene.cycles.samples=16
scene.cycles.use_denoising=True
scene.render.resolution_x=scene.render.resolution_y=g.CELL
scene.render.resolution_percentage=100
scene.render.film_transparent=True
scene.render.image_settings.file_format='PNG'
scene.render.image_settings.color_mode='RGBA'
scene.render.threads_mode,scene.render.threads='FIXED',4
scene.view_settings.view_transform='Standard'
scene.view_settings.look='Medium High Contrast'
scene.world.color=(.22,.22,.22)
bpy.ops.object.camera_add(location=(0,-6,0))
camera=bpy.context.object
camera.rotation_euler=(-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type,camera.data.ortho_scale='ORTHO',2.5
scene.camera=camera
for label,position,power,size in [('Key',(-3,-4,5),550,4),('Fill',(4,-3,1),320,3),('Rim',(2,3,4),750,3)]:
    bpy.ops.object.light_add(type='AREA',location=position)
    light=bpy.context.object
    light.name,light.data.energy,light.data.shape,light.data.size=label,power,'DISK',size
    light.rotation_euler=(-light.location).to_track_quat('-Z','Y').to_euler()
for name in (args.only or NAMES):
    root=models[name]
    for child in root.children: child.hide_render=False
    atlas=np.zeros((g.CELL*6,g.CELL*8,4),dtype=np.float32)
    for frame in range(g.FRAMES):
        pose(root,name,frame/g.FRAMES)
        scene.render.filepath=str(QA/'render.png')
        bpy.ops.render.render(write_still=True)
        rendered=bpy.data.images.load(scene.render.filepath,check_existing=False)
        pixels=np.array(rendered.pixels[:],dtype=np.float32).reshape(g.CELL,g.CELL,4)
        bpy.data.images.remove(rendered)
        x,y=frame%8*g.CELL,(5-frame//8)*g.CELL
        atlas[y:y+g.CELL,x:x+g.CELL]=g.outline(pixels)
        print(f'BOSS_FRAME {name} {frame+1}/{g.FRAMES}',flush=True)
    texture=bpy.data.images.new(name+'_boss_turntable',width=atlas.shape[1],height=atlas.shape[0],alpha=True)
    texture.pixels.foreach_set(atlas.ravel())
    texture.filepath_raw=str(OUTPUT/(name+'.png'))
    texture.file_format='PNG'
    texture.save()
    bpy.data.images.remove(texture)
    for child in root.children: child.hide_render=True
for index,(name,root) in enumerate(models.items()):
    for frame in range(g.FRAMES):
        pose(root,name,frame/g.FRAMES)
        root.keyframe_insert(data_path='rotation_euler',frame=frame+1)
    root.location=(index%7*2.6-7.8,0,7.8-index//7*2.6)
    for child in root.children: child.hide_render=False
scene.frame_start,scene.frame_end,scene.render.fps=1,g.FRAMES,24
scene.frame_set(1)
camera.data.ortho_scale=20
camera.location=(0,-24,0)
scene.render.resolution_x=scene.render.resolution_y=1400
bpy.context.preferences.filepaths.save_version=0
bpy.context.preferences.filepaths.file_preview_type='NONE'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'boss_projectiles.blend'),relative_remap=False)
(OUTPUT/'layout.json').write_text(json.dumps({'frames':g.FRAMES,'cell':g.CELL,'columns':8,'assets':NAMES},indent=2)+'\n')
