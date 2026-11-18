import math


def _arch(api, name, center, radius, thickness, depth, stone, trim, segments=17):
    x, y, z = center
    vertices, faces = [], []
    for index in range(segments):
        a = math.pi * index / segments + .009
        b = math.pi * (index + 1) / segments - .009
        points = [(x + math.cos(t) * r, z + math.sin(t) * r)
                  for r, t in ((radius, a), (radius, b),
                               (radius + thickness, b), (radius + thickness, a))]
        start = len(vertices)
        vertices.extend((px, y + dy, pz) for dy in (-depth / 2, depth / 2) for px, pz in points)
        faces.extend(tuple(start + j for j in face) for face in
                     ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
                      (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)))
    api.mesh(name + ' voussoirs', vertices, faces, stone, bevel=.025)
    api.tube(name + ' inner molding',
             [(x + math.cos(t * math.pi / 32) * (radius + .09), y - depth / 2 - .035,
               z + math.sin(t * math.pi / 32) * (radius + .09)) for t in range(33)], .035, trim)


def _diamond(api, name, center, width, height, depth, surface):
    x, y, z = center
    api.mesh(name, [(x, y - depth, z), (x, y + depth, z),
                    (x - width, y, z), (x, y, z + height),
                    (x + width, y, z), (x, y, z - height)],
             [(0, 2, 3), (0, 3, 4), (0, 4, 5), (0, 5, 2),
              (1, 3, 2), (1, 4, 3), (1, 5, 4), (1, 2, 5)], surface, bevel=.015)


def _star_clasp(api, name, center, radius, surface):
    x, y, z = center
    points = []
    for i in range(8):
        a = math.pi / 2 + i * math.pi / 4
        r = radius if i % 2 == 0 else radius * .28
        points.append((x + math.cos(a) * r, y, z + math.sin(a) * r))
    vertices = [(x, y - radius * .26, z)] + points + [(x, y + radius * .14, z)]
    faces = [(0, i + 1, (i + 1) % 8 + 1) for i in range(8)]
    faces += [(9, (i + 1) % 8 + 1, i + 1) for i in range(8)]
    api.mesh(name, vertices, faces, surface, bevel=.015)


def _swag(api, name, start, end, sag, width, fabric, border, torn=False):
    vertices, faces = [], []
    lower, upper = [], []
    segments, rows = 32, 4
    for i in range(segments + 1):
        t = i / segments
        x = start[0] + (end[0] - start[0]) * t
        y = start[1] + (end[1] - start[1]) * t
        z = start[2] + (end[2] - start[2]) * t - sag * math.sin(math.pi * t)
        breadth = width * (.35 + .65 * math.sin(math.pi * t) ** .55)
        for j in range(rows + 1):
            across = j / rows - .5
            fray = .22 * math.sin(i * 1.8) if torn and j == 0 and .45 < t < .8 else 0
            vertices.append((x, y + math.sin(t * 4 * math.pi + across * 2) * .13,
                             z + across * breadth + fray))
        lower.append(vertices[-rows - 1])
        upper.append(vertices[-1])
    for i in range(segments):
        for j in range(rows):
            if torn and i in (19, 20) and j == 0:
                continue
            a = i * (rows + 1) + j
            b = a + rows + 1
            faces.append((a, b, b + 1, a + 1))
    cloth = api.mesh(name, vertices, faces, fabric)
    thickness = cloth.modifiers.new('Woven edge thickness', 'SOLIDIFY')
    thickness.thickness = .035
    for polygon in cloth.data.polygons:
        polygon.use_smooth = True
    api.tube(name + ' top piping', upper, .025, border)
    api.tube(name + ' bottom piping', lower, .025, border)


def _bridge(api, name, bays, bay_width, depth, stone, shadow, gold, broken=False, rail=True):
    length = bays * bay_width
    api.box(name + ' deck', (0, 0, 0), (length + .35, depth, .42), stone, bevel=.075)
    api.box(name + ' shadow cornice', (0, 0, -.34), (length + .5, depth + .13, .23), shadow, bevel=.045)
    api.box(name + ' upper molding', (0, -.04, .25), (length + .55, depth + .18, .12), gold, bevel=.025)
    for i in range(bays):
        x = -length / 2 + (i + .5) * bay_width
        radius = bay_width * .38
        _arch(api, name + f' arch {i}', (x, 0, -radius - .62), radius, .33, depth * .78, stone, gold)
        for side in (-1, 1):
            px = x + side * (radius + .2)
            api.box(name + f' pier {i} {side}', (px, 0, -radius - 1.2), (.5, depth * .92, 2.2), stone, bevel=.04)
            api.box(name + f' capital {i} {side}', (px, -.02, -radius - .32), (.7, depth + .05, .27), gold, bevel=.025)
    if rail:
        for side in (-1, 1):
            y = side * depth * .46
            api.box(name + f' balustrade {side}', (0, y, 1.04), (length + .3, .14, .16), stone, bevel=.03)
            api.box(name + f' rail glint {side}', (0, y - .015, 1.16), (length + .35, .17, .07), gold, bevel=.018)
            for i in range(bays * 5 + 1):
                if broken and side == -1 and i % 9 in (4, 5):
                    continue
                x = -length / 2 + i * length / (bays * 5)
                api.box(name + f' baluster {side} {i}', (x, y, .66), (.11, .16, .67), stone, bevel=.025)


def _canopy_arcade(api, name, left, right, y, z, stone, dark, gold, chapter):
    _orbit(api, name, left, right, y, z, 4.0, stone, dark, gold,
           curvature=5.2, phase=.5, thickness=.86)
    bays = 12
    step = (right - left) / bays
    for i in range(bays):
        t = (i + .5) / bays
        x = left + (right - left) * t
        dy = y + math.sin(t * math.pi + .5) * 5.2
        dz = z + .35 * math.sin(t * math.pi * 2 + .5)
        yaw = math.atan(math.cos(t * math.pi + .5) * 5.2 * math.pi / (right - left))
        api.parent = api.group(name + f' vaulted bay {i}', (x, dy, dz),
                               (0, chapter * .006 * (1 if i % 2 else -1), yaw))
        _arch(api, name + f' deep arch {i}', (0, -.15, -3.18), step * .40, .43, 3.15, stone, gold)
        for side in (-1, 1):
            px = side * (step * .4 + .24)
            api.cylinder(name + ' tapered hanging column', (px, -.15, -3.57), .29, 1.25, stone)
            api.cylinder(name + ' column collar', (px, -.15, -3.00), .42, .22, gold)
            api.cylinder(name + ' shaped column foot', (px, -.15, -4.18), .38, .18, gold)
        api.parent = None


def _banner(api, name, x, y, z, length, cloth, gold, split=False):
    width = .7
    points = [(x - width, y, z), (x + width, y, z),
              (x + width, y - .08, z - length + .5), (x, y - .16, z - length),
              (x - width, y - .08, z - length + .5)]
    if split:
        points[3] = (x + .28, y - .18, z - length + .45)
    api.mesh(name, points, [(0, 1, 2, 3, 4)], cloth)
    api.tube(name + ' border', points + [points[0]], .035, gold)
    _star_clasp(api, name + ' emblem', (x, y - .06, z - length * .42), .36, gold)


def build_koharu(api, chapter):
    stone = api.mat('Koharu carved lavender limestone', '5E5D86', metallic=.08, roughness=.43)
    dark = api.mat('Koharu deep violet masonry joints', '2C2D4A', roughness=.6)
    gold = api.mat('Koharu aged pale gold', 'A48A53', metallic=.68, roughness=.28)
    lilac = api.mat('Koharu lilac silk', '746A9B', metallic=.1, roughness=.36)
    teal = api.mat('Koharu muted mint silk', '5C8F97', metallic=.08, roughness=.4)
    cream = api.mat('Koharu ivory banner reverse', 'B9B0A4', roughness=.55)
    api.layer('mid')
    layouts = [(-25.3, 3.0, -8.7, 3), (-5.0, 13.0, -9.3, 3), (16.8, 5.5, -8.4, 3)]
    for i, (x, y, z, bays) in enumerate(layouts):
        offset = chapter * (.12 if i % 2 else -.16)
        group = api.group(f'Koharu colonnade {i}', (x + chapter * .4, y, z + offset),
                          (.065, chapter * .008 * (-1 if i % 2 else 1), (-.34, .20, -.22)[i]))
        api.parent = group
        _bridge(api, f'Koharu {i}', bays, 5.1, 3.5, stone, dark, gold, broken=chapter >= 2)
        for post in range(bays + 1):
            px = (post - bays / 2) * 5.1
            api.box('Koharu ornamental post', (px, -1.66, 1.13), (.28, .36, 1.8), stone, bevel=.04)
            _star_clasp(api, 'Koharu star mount', (px, -1.91, 2.12), .36, gold)
        _swag(api, 'Koharu lower terrace cloth', (-bays * 2.5, -1.94, .84),
              (bays * 2.5, -1.94, .84), .7, .45, teal if i % 2 else lilac, gold, chapter == 3)
        api.parent = None
    _canopy_arcade(api, 'Koharu continuous vaulted canopy', -38, 38, 7.5, 10.45,
                   stone, dark, gold, chapter)
    for i, (left, right, z) in enumerate([(-30, -12, 9.85), (-9, 9, 10.05), (12, 30, 10.2)]):
        _swag(api, f'Koharu vaulted silk {i}', (left, -.3, z), (right, 1.4, z + .4),
              1.1 + chapter * .08, .7, lilac, gold, chapter == 3)
        _swag(api, f'Koharu mint lining {i}', (left, -.55, z + .15), (right, 1.15, z + .1),
              .8 + chapter * .07, .38, teal, cream, chapter == 3)
    api.layer('near')
    near_layout = [(-27, -12, -8.3, -.27), (-4, -9, -8.5, .34),
                   (17, -14, -8.05, -.20), (36, -11, -8.65, .23)]
    for i, (x, y, z, yaw) in enumerate(near_layout):
        group = api.group(f'Koharu foreground balcony {i}', (x + chapter * .6, y, z),
                          (.07, .012 * (i % 2 * 2 - 1) * chapter, yaw))
        api.parent = group
        _bridge(api, f'Koharu foreground {i}', 3, 5.5, 4.6, stone, dark, gold, broken=chapter >= 2)
        for px in (-7.7, 0, 7.7):
            api.box('Koharu close square pedestal', (px, -2.15, .78), (.65, .65, 1.1), dark, bevel=.06)
            api.box('Koharu close pedestal lip', (px, -2.15, 1.38), (.83, .83, .2), gold, bevel=.035)
            _star_clasp(api, 'Koharu close metal flower', (px, -2.30, 1.9), .5, gold)
        api.parent = None
    for i, (a, b) in enumerate([(-31, -14), (-10, 7), (11, 30)]):
        _swag(api, f'Koharu near canopy {i}', (a, -13.0, 8.4), (b, -11.8, 8.6),
              .65, 1.1, lilac if i % 2 else teal, gold, chapter >= 2)
        if chapter >= 2:
            _banner(api, f'Koharu detached canopy tail {i}', b - 1.2, -12.0, 8.5,
                    1.4, cream, gold, True)
    api.parent = None


def _spire(api, name, x, y, z, height, stone, dark, gold):
    width = height * .16
    api.box(name + ' foot', (x, y, z + .15), (width * 1.8, width * 1.5, .3), dark, bevel=.04)
    api.box(name + ' square shaft', (x, y, z + height * .39), (width, width, height * .65), stone, bevel=.045)
    for side in (-1, 1):
        api.box(name + ' recessed panel', (x + side * width * .28, y - width * .505, z + height * .38),
                (width * .14, .04, height * .45), dark, bevel=.015)
    api.box(name + ' capital', (x, y, z + height * .73), (width * 1.45, width * 1.35, .22), gold, bevel=.03)
    api.cylinder(name + ' needle', (x, y, z + height * .84), width * .12, height * .3, gold)
    _diamond(api, name + ' lance', (x, y, z + height), width * .3, height * .16, width * .23, gold)


def _orbit(api, name, left, right, y, z, depth, stone, dark, gold, curvature=.0, phase=0, thickness=.62):
    vertices, faces = [], []
    n = 60
    front, back = [], []
    for i in range(n + 1):
        t = i / n
        x = left + (right - left) * t
        dy = y + math.sin(t * math.pi + phase) * curvature
        dz = z + .35 * math.sin(t * math.pi * 2 + phase)
        vertices.extend([(x, dy - depth / 2, dz), (x, dy + depth / 2, dz),
                         (x, dy - depth / 2, dz - thickness), (x, dy + depth / 2, dz - thickness)])
        front.append((x, dy - depth / 2 - .02, dz + .03))
        back.append((x, dy + depth / 2 + .02, dz + .03))
        if i:
            a, b = (i - 1) * 4, i * 4
            faces.extend([(a, b, b + 1, a + 1), (a + 2, a + 3, b + 3, b + 2),
                          (a, a + 2, b + 2, b), (a + 1, b + 1, b + 3, a + 3)])
    api.mesh(name + ' curved viaduct', vertices, faces, stone, bevel=.025)
    api.tube(name + ' near bronze edge', front, .065, gold)
    api.tube(name + ' far bronze edge', back, .05, gold)
    api.tube(name + ' stepped lower molding', [(x, py + .04, pz - thickness + .09) for x, py, pz in front], .11, dark)
    api.tube(name + ' underside reveal', [(x, py - .03, pz - thickness + .2) for x, py, pz in front], .027, gold)
    for i in range(7):
        t = i / 6
        x = left + (right - left) * t
        dy = y + math.sin(t * math.pi + phase) * curvature
        dz = z + .35 * math.sin(t * math.pi * 2 + phase)
        _spire(api, name + f' finial {i}', x, dy - depth / 2, dz, 1.15, stone, dark, gold)
    return front


def _page(api, name, center, width, height, ivory, gold, bend=0):
    x, y, z = center
    vertices, faces = [], []
    for i in range(11):
        t = i / 10
        curve = math.sin(t * math.pi) * bend
        vertices.extend([(x - width / 2, y - curve, z - t * height),
                         (x + width / 2, y - curve - .08, z - t * height + .10 * math.sin(t * math.pi))])
        if i:
            a = (i - 1) * 2
            faces.append((a, a + 2, a + 3, a + 1))
    page = api.mesh(name, vertices, faces, ivory)
    solid = page.modifiers.new('Paper thickness', 'SOLIDIFY')
    solid.thickness = .045
    api.tube(name + ' gilt edge', [vertices[i * 2 + 1] for i in range(11)], .025, gold)


def build_rei(api, chapter):
    stone = api.mat('Rei deep blue violet marble', '424465', metallic=.23, roughness=.32)
    dark = api.mat('Rei near black marble inlay', '191E35', metallic=.18, roughness=.4)
    gold = api.mat('Rei restrained antique brass', 'AC8C54', metallic=.76, roughness=.24)
    velvet = api.mat('Rei purple stage velvet', '352A58', roughness=.68)
    ivory = api.mat('Rei ivory page', 'B6AC9F', roughness=.48)
    api.layer('mid')
    for i, (a, b, y, z) in enumerate([(-37, -9, 6, -9.0), (-6, 33, 13, -9.2),
                                     (-35, -4, 12, 10.9), (0, 35, 5, 10.8)]):
        group = api.group(f'Rei arc gallery {i}', (chapter * .2 * (i % 2 * 2 - 1), 0, 0),
                          (.015, chapter * .006 * (i % 2 * 2 - 1), 0))
        api.parent = group
        _orbit(api, f'Rei gallery {i}', a, b, y, z, 3.3, stone, dark, gold,
               curvature=5.2, phase=i * .7)
        for k in range(5):
            x = a + (b - a) * (k + .5) / 5
            dy = y + math.sin((k + .5) * math.pi / 5 + i * .7) * 5.2
            _arch(api, f'Rei gallery arch {i} {k}', (x, dy, z - 3.0), 2.25, .22, 1.1, stone, gold)
            _spire(api, f'Rei hanging buttress {i} {k}', x - 2.45, dy, z - 4.4, 3.2, stone, dark, gold)
        api.parent = None
    for i, x in enumerate((-27, -15, 3, 19, 31)):
        height = 3.5 + (i % 3) * .35
        root = api.group(f'Rei isolated tower {i}', (x + chapter * .2, 10 + i % 2 * 3, -10.8),
                         (.04, chapter * .023 * (1 if i % 2 else -1), .16))
        api.parent = root
        _spire(api, f'Rei tower {i}', 0, 0, 0, height, stone, dark, gold)
        api.parent = None
    _orbit(api, 'Rei upper distant orbit', -38, 38, 18, 12.4, .75,
           stone, dark, gold, curvature=6.0, phase=1.0)
    for i, (a, b) in enumerate([(-33, -13), (-9, 10), (14, 35)]):
        _swag(api, f'Rei celestial velvet {i}', (a, 2.2, 10.3), (b, 3.0, 10.5),
              .85 + chapter * .1, .95, velvet, gold, chapter >= 2)
        if chapter > 0:
            _page(api, f'Rei unlettered archive leaf {i}', (b - 2, 1.0, 9.9), 1.25, 1.5,
                  ivory, gold, bend=.4)
    api.layer('near')
    for i, (a, b) in enumerate([(-32, -12), (-8, 13), (17, 35)]):
        y, z = [(-12, -8.15), (-8, -8.7), (-14, -8.0)][i]
        _orbit(api, f'Rei foreground orbit {i}', a, b, y, z - .10 * chapter,
               4.8, stone, dark, gold, curvature=3.2, phase=i * .8, thickness=.78)
        for x in (a + 3.8, b - 3.5):
            _arch(api, 'Rei near vaulted foundation', (x, y, z - 2.5), 2.2, .35, 3.5, stone, gold)
    for i, (a, b) in enumerate([(-34, -14), (-10, 10), (14, 34)]):
        _swag(api, f'Rei close velvet canopy {i}', (a, -13.0, 8.4), (b, -12.0, 8.5),
              .55, 1.3, velvet, gold, chapter >= 2)
        _diamond(api, 'Rei attached canopy pendant', (a + .8, -13.2, 7.8), .17, .62, .12, gold)
        if chapter >= 2:
            _page(api, f'Rei folding page {i}', (b - 3, -12.3, 8.1), 1.4, 1.5,
                  ivory, gold, bend=.35 + chapter * .08)
    api.parent = None
