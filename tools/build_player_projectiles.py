import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT/'assets/player_projectiles'
SOURCE = ROOT/'assets/source/player_projectiles'
MODELS = SOURCE/'meshy'
QA = ROOT/'build/qa_story/player_projectile_meshy'
FRAMES, CELL, COLUMNS = 48, 128, 8


def import_model(name, settings):
    path = MODELS/settings['file']
    if path.suffix == '.blend':
        with bpy.data.libraries.load(str(path), link=False) as (source, target):
            target.objects = ['Mesh_0', 'Meshy_Original_Model']
        imported = list(target.objects)
        for obj in imported: bpy.context.collection.objects.link(obj)
    else:
        bpy.ops.import_scene.gltf(filepath=str(path))
        imported = list(bpy.context.selected_objects)
    bpy.context.view_layer.update()
    meshes = [o for o in imported if o.type == 'MESH']
    points = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    anchor = Vector(settings['anchor'])
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)
    basis = bpy.data.objects.new(name+'_source', None)
    bpy.context.collection.objects.link(basis)
    basis.parent = root
    basis.rotation_euler = tuple(math.radians(a) for a in settings['rotation'])
    rotated = [basis.rotation_euler.to_matrix() @ (p-anchor) for p in points]
    scale = min(3.8/max(high-low), 2.10/max(abs(p[i]) for p in rotated for i in (0,2)))
    basis.scale = (scale,)*3
    basis.location = -(basis.rotation_euler.to_matrix() @ anchor)*scale
    for obj in imported:
        if obj.parent is None:
            matrix = obj.matrix_world.copy()
            obj.parent = basis
            obj.matrix_parent_inverse.identity()
            obj.matrix_basis = matrix
    root['source'] = str(path.relative_to(ROOT))
    root['source_sha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
    root['reference'] = settings['reference']
    root['source_anchor'] = list(anchor)
    root['hit_center'] = 'Gemstone or seal center; sprite pivot (64,64)'
    for obj in meshes: obj.hide_render = True
    return root


def pose(root, phase):
    angle = phase*math.tau
    if root.name.endswith('_plane'):
        root.rotation_euler = (.14+.30*math.sin(angle), .035*math.sin(angle*2), .06*math.sin(angle))
    else:
        root.rotation_euler = (.13+.34*math.sin(angle), .05*math.sin(angle*2), .085*math.cos(angle))


def studio(scene, resolution):
    scene.render.engine = 'CYCLES'
    devices = bpy.context.preferences.addons['cycles'].preferences
    devices.compute_device_type = 'OPTIX'
    devices.get_devices()
    for device in devices.devices: device.use = device.type == 'OPTIX'
    scene.cycles.device = 'GPU'
    scene.cycles.samples = 64
    scene.cycles.max_bounces = 16
    scene.cycles.transmission_bounces = 12
    scene.cycles.transparent_max_bounces = 12
    scene.cycles.use_denoising = True
    scene.render.use_persistent_data = True
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = resolution
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium High Contrast'
    world = bpy.data.worlds.new('Projectile studio')
    scene.world = world
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (.22,.25,.32,1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = .45
    for name, at, energy, size, color in [
        ('Key',(-3,-4,5),700,4,(1,.96,.90)),
        ('Fill',(4,-3,0),350,3,(.78,.88,1)),
        ('Rim',(0,3,4),900,3,(.78,.88,1)),
    ]:
        bpy.ops.object.light_add(type='AREA', location=at)
        light = bpy.context.object
        light.name, light.data.energy, light.data.size = name, energy, size
        light.data.color = color
        light.rotation_euler = (-light.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=12)
    environment = bpy.context.object
    environment.name = 'Reflection studio'
    environment.visible_camera = False
    backdrop = bpy.data.materials.new('Reflection gradient')
    nodes, links = backdrop.node_tree.nodes, backdrop.node_tree.links
    nodes.clear()
    coord = nodes.new('ShaderNodeTexCoord')
    split = nodes.new('ShaderNodeSeparateXYZ')
    ramp = nodes.new('ShaderNodeValToRGB')
    stops = [(0,(.03,.04,.06,1)),(.38,(.09,.12,.16,1)),(.46,(.32,.40,.50,1)),
             (.50,(.10,.14,.19,1)),(.58,(.018,.03,.055,1)),(1,(.15,.19,.25,1))]
    for element,(position,color) in zip(ramp.color_ramp.elements, (stops[0],stops[-1])):
        element.position, element.color = position, color
    for position,color in stops[1:-1]: ramp.color_ramp.elements.new(position).color = color
    emission = nodes.new('ShaderNodeEmission')
    emission.inputs['Strength'].default_value = .65
    output = nodes.new('ShaderNodeOutputMaterial')
    links.new(coord.outputs['Generated'], split.inputs['Vector'])
    links.new(split.outputs['Z'], ramp.inputs['Fac'])
    links.new(ramp.outputs['Color'], emission.inputs['Color'])
    links.new(emission.outputs['Emission'], output.inputs['Surface'])
    environment.data.materials.append(backdrop)
    bpy.ops.object.camera_add(location=(0,-7,0))
    camera = bpy.context.object
    camera.rotation_euler = (-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type, camera.data.ortho_scale = 'ORTHO', 5.2
    scene.camera = camera


def save_image(name, pixels, path):
    image = bpy.data.images.new(name, width=pixels.shape[1], height=pixels.shape[0], alpha=True)
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw, image.file_format = str(path), 'PNG'
    image.save()
    bpy.data.images.remove(image)


def render_frame(scene, name, frame, size, oversample):
    scene.render.filepath = str(QA/f'{name}_render.png')
    bpy.ops.render.render(write_still=True)
    image = bpy.data.images.load(scene.render.filepath, check_existing=False)
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(size*oversample,size*oversample,4)
    bpy.data.images.remove(image)
    pixels[:,:,:3] *= pixels[:,:,3:4]
    pixels = pixels.reshape(size,oversample,size,oversample,4).mean(axis=(1,3))
    pixels[:,:,:3] /= np.maximum(pixels[:,:,3:4], .00001)
    if size == CELL:
        alpha = pixels[:,:,3]
        assert alpha[CELL//2,CELL//2] > .9, f'{name}/{frame}: uncovered hit center'
        assert np.max(np.concatenate((alpha[0],alpha[-1],alpha[:,0],alpha[:,-1]))) == 0, f'{name}/{frame}: clipped'
    return pixels


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--preview', action='store_true')
    parser.add_argument('--preview-size', type=int, default=512)
    parser.add_argument('--only')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    for directory in (OUTPUT,SOURCE,QA): directory.mkdir(parents=True,exist_ok=True)
    (SOURCE/'.gdignore').touch()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    settings = json.loads((MODELS/'models.json').read_text())
    names = [args.only] if args.only else list(settings)
    models = {name:import_model(name,settings[name]) for name in names}
    size, oversample = (args.preview_size,1) if args.preview else (CELL,2)
    scene = bpy.context.scene
    studio(scene,size*oversample)
    frames = 1 if args.preview else FRAMES
    for name,root in models.items():
        meshes = [o for o in root.children_recursive if o.type == 'MESH']
        for obj in meshes: obj.hide_render = False
        atlas = np.zeros((size*(FRAMES//COLUMNS),size*COLUMNS,4),dtype=np.float32)
        for frame in range(frames):
            pose(root,frame/FRAMES)
            pixels = render_frame(scene,name,frame,size,oversample)
            if frame == 0: save_image(name+'_front',pixels,(QA if args.preview else OUTPUT)/(name+'_front.png'))
            x,y = frame%COLUMNS*size,(FRAMES//COLUMNS-1-frame//COLUMNS)*size
            atlas[y:y+size,x:x+size] = pixels
            print(f'PLAYER_FRAME {name} {frame+1}/{frames}',flush=True)
        if not args.preview: save_image(name,atlas,OUTPUT/(name+'.png'))
        for obj in meshes: obj.hide_render = True
    if not args.preview and not args.only:
        scene.frame_start,scene.frame_end,scene.render.fps = 1,FRAMES,24
        for index,(name,root) in enumerate(models.items()):
            for frame in range(FRAMES+1):
                pose(root,frame/FRAMES)
                root.keyframe_insert(data_path='rotation_euler',frame=frame+1)
            root.location = (index%2*5.4-2.7,0,7.8-index//2*5.2)
            for obj in root.children_recursive: obj.hide_render = False
        scene.camera.data.ortho_scale = 22
        scene.frame_set(1)
        bpy.context.preferences.filepaths.save_version = 0
        bpy.context.preferences.filepaths.file_preview_type = 'NONE'
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'player_projectiles.blend'),relative_remap=False)
        (OUTPUT/'layout.json').write_text(json.dumps({'frames':FRAMES,'cell':CELL,'columns':COLUMNS,'pivot':[64,64],
            'models':list(models),'provider':'Meshy','sources':{name:root['source_sha256'] for name,root in models.items()}},indent=2)+'\n')
    print('PLAYER_RENDER_COMPLETE',flush=True)


if __name__ == '__main__': main()
