"""Studio previews of the exact released pack meshes; run with Blender --background."""
# SPDX-License-Identifier: MIT
import bpy, json, zipfile, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
PACKS=ROOT.parent/'Unity/Assets/StreamingAssets/VehiclePacks'
for slug,filename in [('vw-splittie','VW-Splittie-2.0.0.eucar'),('hekla','Hekla-Formula-Offroad-2.0.0.eucar')]:
 bpy.ops.wm.read_factory_settings(use_empty=True)
 with zipfile.ZipFile(PACKS/filename) as archive:
  spec=json.loads(archive.read('manifest.json'))['physics']; model=json.loads(archive.read('mesh.json'))
 root_height=spec['suspensionRayLength']-spec['suspensionAnchorY']-spec['massKg']*9.81/(4*spec['springNewtonsPerMetre'])
 mats=[]
 for index,data in enumerate(model['materials']):
  mat=bpy.data.materials.new('Pack material '+str(index));mat.use_nodes=True
  shader=mat.node_tree.nodes.get('Principled BSDF');shader.inputs['Base Color'].default_value=(*data['colour'],1)
  shader.inputs['Metallic'].default_value=data['metallic'];shader.inputs['Roughness'].default_value=max(.12,1-data['smoothness'])
  mats.append(mat)
 for part in model['parts']:
  offset=[0,root_height,0]; attachment=part['attachment']
  if attachment.startswith('wheel'):
   i=int(attachment[-1]);offset=[(-1 if i<2 else 1)*spec['track']/2,spec['wheelRadius'],(-1 if i%2==0 else 1)*spec['wheelbase']/2]
  elif attachment=='steering':offset=[spec['cockpitX'],root_height+spec['cockpitY']-.35,spec['cockpitZ']+.45]
  raw=part['vertices']; vertices=[(raw[i]+offset[0],-raw[i+2]-offset[2],raw[i+1]+offset[1]) for i in range(0,len(raw),3)]
  triangles=part['triangles']; faces=[triangles[i:i+3] for i in range(0,len(triangles),3)]
  mesh=bpy.data.meshes.new(part['name']);mesh.from_pydata(vertices,[],faces);mesh.update()
  if 'normals' in part:
   ns=part['normals'];mesh.normals_split_custom_set_from_vertices([(ns[i],-ns[i+2],ns[i+1]) for i in range(0,len(ns),3)])
  else:
   for face in mesh.polygons:face.use_smooth=True
  obj=bpy.data.objects.new(part['name'],mesh);bpy.context.collection.objects.link(obj);obj.data.materials.append(mats[part['material']])
 bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.015));floor=bpy.context.object
 mat=bpy.data.materials.new('Warm studio floor');mat.diffuse_color=(.58,.61,.56,1);floor.data.materials.append(mat)
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=24;scene.cycles.use_denoising=True
 scene.render.threads_mode='FIXED';scene.render.threads=6;scene.render.resolution_x=1200;scene.render.resolution_y=800;scene.render.resolution_percentage=100
 scene.render.image_settings.file_format='PNG';scene.world=bpy.data.worlds.new('Studio');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.55,.62,.67,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.45
 scene.view_settings.view_transform='AgX'
 def area(name,position,power,size):
  data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
  light=bpy.data.objects.new(name,data);scene.collection.objects.link(light);light.location=position;light.rotation_euler=(Vector((0,0,1))-light.location).to_track_quat('-Z','Y').to_euler()
 area('Key',(-4,-4,7),1500,5);area('Fill',(5,-1,4),1000,4);area('Rim',(1,5,6),1800,4)
 camera_data=bpy.data.cameras.new('Camera');camera=bpy.data.objects.new('Camera',camera_data);scene.collection.objects.link(camera);scene.camera=camera;camera_data.type='ORTHO';camera_data.ortho_scale=6.7 if slug=='vw-splittie' else 5.9
 for view,position in [('front',(6,-8,4.7)),('rear',(-6,8,4.4)),('side',(8,-.5,3.3))]:
  camera.location=position;camera.rotation_euler=(Vector((0,0,1.25))-camera.location).to_track_quat('-Z','Y').to_euler()
  scene.render.filepath=str(ROOT/'public/assets/cars'/f'{slug}-{view}.png');bpy.ops.render.render(write_still=True)
 print('Rendered',slug,flush=True)
