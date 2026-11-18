import math


def _ccw(points):
    area = sum(a[0] * b[1] - b[0] * a[1]
               for a, b in zip(points, points[1:] + points[:1]))
    return points if area > 0 else list(reversed(points))


def _scale(points, x, y=None):
    return [(px * x, py * (x if y is None else y)) for px, py in points]


def _inset(points, factor):
    cx = sum(p[0] for p in points) / len(points)
    cy = sum(p[1] for p in points) / len(points)
    return [(cx + (x - cx) * factor, cy + (y - cy) * factor) for x, y in points]


def _star(radius, inset, tips=5, angle=math.pi / 2):
    return [(math.cos(angle + i * math.pi / tips) * (radius if i % 2 == 0 else inset),
             math.sin(angle + i * math.pi / tips) * (radius if i % 2 == 0 else inset))
            for i in range(tips * 2)]


def _lozenge(x, y, width, height):
    return [(x, y + height), (x - width, y), (x, y - height), (x + width, y)]


def build_koharu(api):
    silver = api.mat('Koharu silver frame', 'C2CEDA', metal=.82, rough=.23)
    edge = api.mat('Koharu dark bevel edges', '465064', metal=.8, rough=.3)
    pearl = api.mat('Koharu pearl frame ridges', 'ECF0F3', metal=.46, rough=.19)
    black = api.mat('Koharu obsidian body', '191523', metal=.32, rough=.27)
    facet_dark = api.mat('Koharu midnight facets', '27213B', metal=.37, rough=.29)
    facet_light = api.mat('Koharu violet facets', '51436E', metal=.32, rough=.25)
    ribbon = api.mat('Koharu violet ribbons', '9761D8', metal=.16, rough=.37)
    ribbon_dark = api.mat('Koharu ribbon folds', '623B96', metal=.12, rough=.43)
    ribbon_edge = api.mat('Koharu pale ribbon piping', 'E1CDF1', metal=.18, rough=.32)
    mint = api.mat('Koharu mint inlay', '92CEAC', metal=.28, rough=.23, emit=.10)
    iris = api.mat('Koharu emerald vertical iris', '246C44', metal=.16, rough=.25, emit=.10)
    eye = api.mat('Koharu luminous white eye', 'F1F5DC', metal=.05, rough=.24, emit=.48)

    upper = [(.67, .95), (1.35, 1.15), (1.43, .88), (1.75, .66),
             (1.71, .48), (1.58, .52), (1.61, .35), (1.31, .49), (.61, .79)]
    lower = [(.92, .08), (1.21, -.30), (1.69, -.83), (1.57, -.90),
             (1.58, -1.09), (1.40, -.99), (1.29, -1.18), (1.04, -.56), (.82, -.18)]
    for side in (-1, 1):
        for index, shape in enumerate((upper, lower)):
            points = _ccw([(x * side, y) for x, y in shape])
            name = f'Koharu ribbon {side} {index}'
            api.prism(name + ' thick silhouette', points, -.19, .15, ribbon_dark, bevel=.025)
            api.ring(name + ' pale sewn border', points, _inset(points, .86), -.075, .055, ribbon_edge, bevel=.015)
            api.facet(name + ' folded purple face', _inset(points, .88), -.06, .075, ribbon)
            fold = [(side * (.98 if index == 0 else .96), .82 if index == 0 else -.05, -.005),
                    (side * (1.22 if index == 0 else 1.24), .73 if index == 0 else -.47, .045),
                    (side * (1.57 if index == 0 else 1.48), .55 if index == 0 else -.87, -.01)]
            api.tube(name + ' fold seam', fold, .016, ribbon_edge)

    outer = [(0, 1.46), (-.64, 1.04), (-.93, .48), (-1.00, -.08),
             (-.70, -.85), (0, -1.50), (.70, -.85), (1.00, -.08), (.93, .48), (.64, 1.04)]
    inner = _scale(outer, .76, .81)
    api.prism('Koharu solid dark backing', _scale(outer, 1.035), -.04, .30, edge, bevel=.07)
    api.prism('Koharu opaque obsidian shield', _scale(outer, .96), .10, .17, black, bevel=.035)
    for index in range(len(inner)):
        a, b = inner[index], inner[(index + 1) % len(inner)]
        api.facet(f'Koharu inset crystal plane {index}', _ccw([(0, .03), a, b]),
                  .17, .035 if index % 2 else .075, facet_dark if index % 3 else facet_light)
    api.ring('Koharu broad silver frame', outer, inner, .20, .28, silver, bevel=.045)
    api.ring('Koharu polished outer rim', outer, _scale(outer, .955), .35, .055, pearl, bevel=.012)
    api.ring('Koharu inner shadow reveal', _scale(inner, 1.035), _scale(inner, .97),
             .33, .055, edge, bevel=.012)
    for side in (-1, 1):
        api.tube(f'Koharu mint upper inlay {side}',
                 [(side * .13, 1.26, .37), (side * .56, .96, .37), (side * .75, .60, .37)],
                 .027, mint)
        api.tube(f'Koharu mint lower inlay {side}',
                 [(side * .83, -.18, .37), (side * .57, -.76, .37), (side * .15, -1.22, .37)],
                 .027, mint)
    for name, x, y, width, height in [('upper crown', 0, 1.37, .22, .36),
                                     ('lower crown', 0, -1.38, .19, .34),
                                     ('left shoulder', -.88, -.04, .17, .28),
                                     ('right shoulder', .88, -.04, .17, .28)]:
        points = _lozenge(x, y, width, height)
        api.facet('Koharu silver ' + name, points, .36, .14, pearl)
        api.facet('Koharu mint ' + name, _inset(points, .48), .505, .035, mint)

    almond = [(-.66, .03), (-.39, .24), (0, .31), (.39, .24), (.66, .03),
              (.39, -.17), (0, -.24), (-.39, -.17)]
    api.prism('Koharu dark eye socket', _ccw(_scale(almond, 1.12)), .30, .16, black, bevel=.035)
    api.facet('Koharu luminous almond lens', _ccw(almond), .40, .07, eye)
    api.prism('Koharu green vertical iris', [(-.095, -.15), (.095, -.15), (.095, .22), (-.095, .22)],
              .49, .055, iris, bevel=.018)
    api.prism('Koharu pale vertical slit', [(-.033, -.105), (.033, -.105), (.033, .175), (-.033, .175)],
              .525, .024, eye, bevel=.008)


def build_rei(api):
    gold = api.mat('Rei warm gold frame', 'C49437', metal=.83, rough=.22)
    bright_gold = api.mat('Rei polished gold bevel', 'F0D890', metal=.63, rough=.19)
    shadow_gold = api.mat('Rei bronze side walls', '6F4A19', metal=.82, rough=.31)
    purple = api.mat('Rei violet crystal', '6B2EAC', metal=.42, rough=.24)
    bright_purple = api.mat('Rei amethyst facets', 'A56BE0', metal=.36, rough=.21)
    deep_purple = api.mat('Rei dark crystal core', '26133E', metal=.33, rough=.3)
    white = api.mat('Rei four point pearl light', 'F2F0D8', metal=.16, rough=.2, emit=.55)

    outer = _star(1.76, .77)
    inner = _scale(outer, .78)
    shards = _star(1.42, .82, angle=math.pi / 2 + math.pi / 5)
    api.prism('Rei solid bronze star backing', _scale(outer, 1.025), -.07, .28, shadow_gold, bevel=.045)
    api.prism('Rei violet star foundation', outer, .06, .23, deep_purple, bevel=.035)
    api.prism('Rei rear amethyst crown', shards, -.06, .18, purple, bevel=.018)
    for index in range(len(shards)):
        api.facet(f'Rei rear gem facet {index}', _ccw([(0, 0), shards[index], shards[(index + 1) % len(shards)]]),
                  .04, .10 if index % 2 else .15, purple if index % 3 else bright_purple)
    for index in range(len(outer)):
        points = _ccw([(0, 0), outer[index], outer[(index + 1) % len(outer)]])
        api.facet(f'Rei large inset crystal facet {index}', points, .15, .09,
                  bright_purple if index % 3 == 0 else purple if index % 2 else deep_purple)
    api.ring('Rei broad five point gold frame', outer, inner, .22, .26, gold, bevel=.04)
    api.ring('Rei illuminated outer gold chamfer', outer, _scale(outer, .945), .36, .055,
             bright_gold, bevel=.012)
    api.ring('Rei inner gold molding', _scale(inner, 1.045), _scale(inner, .99), .36, .045,
             bright_gold, bevel=.01)
    api.ring('Rei nested amethyst star', _star(1.13, .49), _star(.99, .38), .29, .09,
             bright_purple, bevel=.018)

    for index in range(5):
        angle = math.pi / 2 + index * math.tau / 5
        ux, uy = math.cos(angle), math.sin(angle)
        tx, ty = -uy, ux
        cx, cy = ux * 1.41, uy * 1.41
        points = _ccw([(cx + ux * .28, cy + uy * .28),
                       (cx + tx * .115, cy + ty * .115),
                       (cx - ux * .20, cy - uy * .20),
                       (cx - tx * .115, cy - ty * .115)])
        api.facet(f'Rei raised tip mount {index}', points, .36, .13, bright_gold)
        api.facet(f'Rei inset tip amethyst {index}', _inset(points, .47), .49, .045, purple)

    light = [(0, 1.05), (-.15, .20), (-.70, .02), (-.15, -.16),
             (0, -1.01), (.15, -.16), (.70, .02), (.15, .20)]
    api.prism('Rei central star gold setting', _scale(light, 1.12), .38, .17, gold, bevel=.035)
    api.facet('Rei bright four point star', light, .49, .14, white)
    for side in (-1, 1):
        api.tube(f'Rei pendant chain {side}', [(side * 1.47, .43, .12), (side * 1.43, -.08, .12)],
                 .022, gold)
        pendant = _lozenge(side * 1.43, -.20, .065, .13)
        api.facet(f'Rei suspended gold jewel {side}', pendant, .14, .065, bright_gold)
        api.facet(f'Rei suspended purple inset {side}', _inset(pendant, .5), .21, .018, purple)
