import argparse
import math
import random
import shutil
import subprocess
import sys
from pathlib import Path

import bpy


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets/sns"
QA = ROOT / "build/qa_story/sns_media/motion_v3"
SOURCE = OUTPUT / "story_clips_v2.png"
TAU = math.tau
LEFT, RIGHT = 1 / 18, 17 / 18


def smooth(value):
    value = max(0, min(1, value))
    return value * value * (3 - 2 * value)


def band(value, low, high, feather):
    return smooth((value - low) / feather) * smooth((high - value) / feather)


def position(u, v, depth=0):
    return ((u - (LEFT + RIGHT) / 2) / (RIGHT - LEFT) * 16, (0.5 - v) * 9, depth)


def uv(u, v, row):
    return (u, 1 - (row + v) / 2)


def mesh(name, vertices, faces, surface):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    result = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(result)
    result.data.materials.append(surface)
    return result


def emissive(name, color, alpha=False):
    result = bpy.data.materials.new(name)
    nodes = result.node_tree.nodes
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (*color, 1)
    if alpha:
        result.surface_render_method = "BLENDED"
        transparent = nodes.new("ShaderNodeBsdfTransparent")
        attribute = nodes.new("ShaderNodeVertexColor")
        attribute.layer_name = "Fade"
        mix = nodes.new("ShaderNodeMixShader")
        result.node_tree.links.new(attribute.outputs["Alpha"], mix.inputs[0])
        result.node_tree.links.new(transparent.outputs[0], mix.inputs[1])
        result.node_tree.links.new(emission.outputs[0], mix.inputs[2])
        result.node_tree.links.new(mix.outputs[0], out.inputs[0])
    else:
        result.node_tree.links.new(emission.outputs[0], out.inputs[0])
    return result, emission


def setup(kind):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = 32
    scene.render.resolution_x = 640
    scene.render.resolution_y = 360
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.fps = 24
    scene.world.color = (0, 0, 0)
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0
    scene.view_settings.gamma = 1
    bpy.ops.object.camera_add(location=(0, 0, 10))
    camera = bpy.context.object
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 16
    scene.camera = camera
    (QA / kind).mkdir(parents=True, exist_ok=True)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    return scene


def background(row):
    surface, emission = emissive("Original illustration", (1, 1, 1))
    texture = surface.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(str(SOURCE), check_existing=True)
    texture.interpolation = "Linear"
    surface.node_tree.links.new(texture.outputs["Color"], emission.inputs["Color"])
    nx, ny = 112, 64
    coords = [(LEFT + (RIGHT - LEFT) * x / nx, y / ny) for y in range(ny + 1) for x in range(nx + 1)]
    faces = []
    for y in range(ny):
        for x in range(nx):
            i = y * (nx + 1) + x
            faces.append((i, i + nx + 1, i + nx + 2, i + 1))
    obj = mesh("Illustration with localized movement", [position(u, v) for u, v in coords], faces, surface)
    layer = obj.data.uv_layers.new(name="Atlas cell")
    for loop in obj.data.loops:
        layer.data[loop.index].uv = uv(*coords[loop.vertex_index], row)
    return obj, coords, layer


def raindrop(surface, index):
    count = 16
    vertices = [(0, 0, 0)] * (count * 2 + 1)
    faces = []
    for i in range(count):
        j = (i + 1) % count
        faces.append((0, 1 + i, 1 + j))
        faces.append((1 + i, 1 + count + i, 1 + count + j, 1 + j))
    obj = mesh(f"Sliding drop {index}", vertices, faces, surface)
    colors = obj.data.color_attributes.new(name="Fade", type="FLOAT_COLOR", domain="POINT")
    return obj, colors


def rain():
    scene = setup("rain")
    obj, coords, layer = background(0)
    surface, _ = emissive("Rain edge light", (.44, .58, .77), True)
    random.seed(71)
    drops = []
    for index in range(18):
        x = random.uniform(.2, .93)
        end = min(.78, .64 + (x - .15) * .3)
        distance = random.uniform(.32, .46)
        start = random.uniform(.04, end - distance)
        trail = mesh(f"Water trail {index}", [(0, 0, 0)] * 12,
                     [(j * 2, j * 2 + 1, j * 2 + 3, j * 2 + 2) for j in range(5)], surface)
        trail_colors = trail.data.color_attributes.new(name="Fade", type="FLOAT_COLOR", domain="POINT")
        drops.append((*raindrop(surface, index), trail, trail_colors, x, start, distance,
                      random.uniform(.0025, .0033), random.uniform(.009, .015), random.random()))

    def animate(t):
        for loop in obj.data.loops:
            u, v = coords[loop.vertex_index]
            mask = band(u, .43, .94, .04) * band(v, .55, .79, .05)
            shift = .0016 * math.sin(TAU * t + v * 26 + u * 7) * mask
            layer.data[loop.index].uv = uv(u + shift, v, 0)
        for drop, colors, trail, trail_colors, x, start, distance, width, height, offset in drops:
            phase = (t + offset) % 1
            fade = smooth(phase / .14) * smooth((1 - phase) / .18)
            cx = x + .0025 * math.sin(phase * 4 + offset * TAU)
            cy = start + phase * distance
            drop.data.vertices[0].co = position(cx, cy, .03)
            colors.data[0].color = (1, 1, 1, .15 * fade)
            for ring in range(2):
                for i in range(16):
                    angle = TAU * i / 16
                    radius = .72 if ring == 0 else 1
                    j = 1 + ring * 16 + i
                    drop.data.vertices[j].co = position(cx + math.cos(angle) * width * radius,
                                                        cy + math.sin(angle) * height * radius, .03)
                    colors.data[j].color = (1, 1, 1, (.70 if ring == 0 else 0) * fade)
            drop.data.update()
            trail_length = min(.10, phase * distance)
            for j in range(6):
                along = j / 5
                trail_y = cy - trail_length * along
                trail_x = x + .0025 * math.sin((phase - trail_length * along / distance) * 4 + offset * TAU)
                half_width = width * .28 * (1 - along * .7)
                for side in range(2):
                    k = j * 2 + side
                    trail.data.vertices[k].co = position(trail_x + (-1 if side == 0 else 1) * half_width, trail_y, .025)
                    trail_colors.data[k].color = (1, 1, 1, .38 * fade * (1 - along))
            trail.data.update()
    return scene, animate


def studio():
    scene = setup("studio")
    obj, coords, layer = background(1)
    indicator, _ = emissive("Monitor playback levels", (.27, .40, .59), True)
    bars = []
    for i in range(14):
        bar = mesh("Audio playback level", [(0, 0, 0)] * 4, [(0, 1, 2, 3)], indicator)
        colors = bar.data.color_attributes.new(name="Fade", type="FLOAT_COLOR", domain="POINT")
        for color in colors.data:
            color.color = (1, 1, 1, .80)
        bars.append(bar)

    def animate(t):
        for loop in obj.data.loops:
            u, v = coords[loop.vertex_index]
            curtain = band(u, .245, .378, .025) * band(v, -.02, .465, .04)
            curtain += band(u, -.02, .091, .025) * band(v, -.02, .535, .05)
            sway = .018 * math.sin(TAU * t + v * 3) * smooth(v / .35) * curtain
            layer.data[loop.index].uv = uv(u + sway, v, 1)
        for i, bar in enumerate(bars):
            x, y = .542 + i * .0105, .305
            height = .010 + .054 * (.5 + .5 * math.sin(TAU * t * 2 + i * .65))
            for vertex, (u, v) in zip(bar.data.vertices,
                                     [(x, y), (x + .006, y), (x + .006, y - height), (x, y - height)]):
                vertex.co = position(u, v, .02)
            bar.data.update()
    return scene, animate


args = argparse.ArgumentParser()
args.add_argument("--kind", choices=("rain", "studio"), required=True)
args.add_argument("--preview", action="store_true")
options = args.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
scene, animate = rain() if options.kind == "rain" else studio()
frames = [0, 32, 64] if options.preview else range(96)
for frame in frames:
    scene.frame_set(frame + 1)
    animate(frame / 96)
    scene.render.filepath = str(QA / options.kind / f"{frame:04d}.png")
    bpy.ops.render.render(write_still=True)
if not options.preview:
    shutil.copyfile(QA / options.kind / "0000.png", OUTPUT / f"{options.kind}_poster.png")
    # This installed Theora encoder produces invalid interframes; intra frames decode consistently.
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-framerate", "24",
                    "-i", str(QA / options.kind / "%04d.png"), "-c:v", "libtheora", "-q:v", "9",
                    "-g", "1", "-pix_fmt", "yuv420p", "-an", str(OUTPUT / f"{options.kind}_loop.ogv")], check=True)
    print(f"SNS_CLIP_COMPLETE {options.kind} 640x360 24fps 96frames")
