# SPDX-License-Identifier: MIT
# Original procedural model by Richard Cole / Earth Unscripted.
# See PROVENANCE.md for brand-mark and artwork licence scope.
import bpy, math, json, hashlib, zipfile
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent
OUTPUT=ROOT/"Generated"
OUTPUT.mkdir(exist_ok=True)
if not bpy.app.background: raise RuntimeError('Use a fresh Blender --background --factory-startup process')
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
materials=[]
specs=[('Seafoam',(.19,.59,.49),.25,.27),('Warm ivory',(.88,.83,.66),.12,.28),('Chrome',(.65,.69,.72),.92,.18),('Rubber',(.018,.022,.023),0,.72),('Blue glass',(.055,.15,.19),.45,.16),('Amber',(.95,.32,.035),.15,.25),('Ruby',(.55,.018,.015),.15,.22),('Canvas',(.64,.32,.13),0,.85),('Wood',(.35,.16,.055),0,.5),('Daisy',(.98,.75,.13),0,.45),('Lamp',(.9,.91,.77),.2,.15),('Shadow',(.035,.042,.039),0,.55)]
for name,color,metal,rough in specs:
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough;materials.append(m)
def point(p):return (p[0],-p[2],p[1])
def finish(o,name,mat,attachment='body'):
 o.name=name;o.data.materials.append(materials[mat]);o['attachment']=attachment;o['material']=mat
 return o
def box(name,mat,c,s,bevel=.025):
 bpy.ops.mesh.primitive_cube_add(size=1,location=point(c));o=bpy.context.object;o.dimensions=(s[0],s[2],s[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel:
  mod=o.modifiers.new('Rolled edges','BEVEL');mod.width=bevel;mod.segments=3;bpy.ops.object.modifier_apply(modifier=mod.name)
 return finish(o,name,mat)
def tube(name,mat,a,b,r,vertices=16):
 a,b=Vector(point(a)),Vector(point(b));d=b-a;bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=r,depth=d.length,location=(a+b)/2);o=bpy.context.object;o.rotation_euler=d.to_track_quat('Z','Y').to_euler();return finish(o,name,mat)
def sphere(name,mat,c,s):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,location=point(c));o=bpy.context.object;o.scale=(s[0],s[2],s[1]);return finish(o,name,mat)
def line(name,mat,pts,r=.008):
 for a,b in zip(pts,pts[1:]):tube(name,mat,a,b,r,8)
def ring(name,mat,c,r,thick,axis='z'):
 pts=[]
 for i in range(49):
  a=i*math.tau/48
  pts.append((c[0]+(0 if axis=='x' else r*math.sin(a)),c[1]+r*math.cos(a),c[2]+(r*math.sin(a) if axis=='x' else 0)))
 line(name,mat,pts,thick)
# Round-sided monocoque with genuine wheel openings, rather than wheels buried in a box.
body=box('Seafoam lower coachwork',0,(0,.82,0),(1.75,1.04,4.18),.20)
for side in [-1,1]:
 for z in [-1.2,1.2]:
  cutter=tube('cutter',3,(side*.57,.24,z),(side*1.18,.24,z),.405,48)
  bpy.context.view_layer.objects.active=body;mod=body.modifiers.new('Wheel opening','BOOLEAN');mod.object=cutter;mod.operation='DIFFERENCE';bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)
cabin=box('Ivory window cabin',1,(0,1.59,-.025),(1.70,.65,4.07),.18)
def aperture(name, centre, size):
 # Cut the coachwork rather than placing dark rectangles over a solid shell.
 cutter=box(name,3,centre,size,.025)
 bpy.context.view_layer.objects.active=cabin
 modifier=cabin.modifiers.new(name,'BOOLEAN');modifier.object=cutter;modifier.operation='DIFFERENCE'
 bpy.ops.object.modifier_apply(modifier=modifier.name);bpy.data.objects.remove(cutter,do_unlink=True)
def glazing(name, centre, size, outward):
 # Exterior tinted face only. From the cabin the open back face is culled,
 # retaining a clear road view without changing the exterior's fixed livery.
 x,y,z=centre;w,h,d=size
 if outward=='front': points=[(x-w/2,y-h/2,z),(x+w/2,y-h/2,z),(x+w/2,y+h/2,z),(x-w/2,y+h/2,z)]
 elif outward=='rear': points=[(x+w/2,y-h/2,z),(x-w/2,y-h/2,z),(x-w/2,y+h/2,z),(x+w/2,y+h/2,z)]
 else:
  side=1 if x>0 else -1
  points=[(x,y-h/2,z+side*d/2),(x,y-h/2,z-side*d/2),(x,y+h/2,z-side*d/2),(x,y+h/2,z+side*d/2)]
 mesh=bpy.data.meshes.new(name);mesh.from_pydata([point(p) for p in points],[],[(0,1,2,3)])
 obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);finish(obj,name,4)
def window_frame(name,centre,width,height,side=None):
 x,y,z=centre
 if side is None:
  points=[(x-width/2,y-height/2,z),(x+width/2,y-height/2,z),(x+width/2,y+height/2,z),(x-width/2,y+height/2,z),(x-width/2,y-height/2,z)]
 else:
  points=[(x,y-height/2,z-width/2),(x,y-height/2,z+width/2),(x,y+height/2,z+width/2),(x,y+height/2,z-width/2),(x,y-height/2,z-width/2)]
 line(name,3,points,.018)
for side in [-1,1]:
 aperture('Front window aperture',(side*.395,1.625,2.0),(.70,.45,.5))
 for z,width in [(1.18,.75),(.25,.70),(-.60,.70),(-1.43,.68)]:
  aperture('Side window aperture',(side*.85,1.61,z),(.4,.415,width-.05))
aperture('Rear window aperture',(0,1.63,-2.06),(1.065,.375,.5))
box('Rounded ivory roof',1,(0,1.96,-.025),(1.77,.18,4.15),.085)
# Split front glass follows the subtle V face and retains a strong central mullion.
for side in [-1,1]:
 window_frame('Split windshield rubber',(side*.395,1.625,2.031),.70,.45)
 glazing('Split windshield glass',(side*.395,1.625,2.034),(.688,.443,0),'front')
 line('Wiper arms',2,[(side*.18,1.415,2.065),(side*.47,1.53,2.065)],.009)
 line('Wiper blades',3,[(side*.33,1.51,2.071),(side*.61,1.55,2.071)],.012)
 # Iconic ivory V descending over the nose.
 pts=[(side*.84,1.31,2.025),(side*.72,1.22,2.09),(0,.91,2.105),(0,1.32,2.105)]
 verts=[point(p) for p in pts];me=bpy.data.meshes.new('V nose');me.from_pydata(verts,[],[(3,2,1,0) if side==1 else (0,1,2,3)]);o=bpy.data.objects.new('Ivory V nose',me);bpy.context.collection.objects.link(o);finish(o,'Ivory V nose',1)
 for z,width in [(1.18,.75),(.25,.70),(-.60,.70),(-1.43,.68)]:
  window_frame('Side window seals',(side*.869,1.61,z),width-.05,.415,side)
  glazing('Side glazing',(side*.875,1.61,z),(0,.405,width-.055),'side')
  if z<.8:
   for edge in [-1,1]:
    for pleat in range(4):
     box('Ochre pleated curtains',7,(side*.880,1.60,z+edge*(width*.5-.065)-edge*pleat*.021),(.014,.35,.018),.006)
 # Door shut lines, beltline, handles and lower sill.
 line('Chrome beltline',2,[(side*.879,1.29,-1.85),(side*.879,1.29,1.80)],.012)
 for z in [.75,1.65,-.96]:line('Door panel gaps',11,[(side*.879,.53,z),(side*.879,1.29,z)],.004)
 for z in [.85,-.84,-.65]:box('Chrome door handles',2,(side*.90,1.20,z),(.035,.025,.12),.01)
 tube('Sill trim',2,(side*.87,.44,-.75),(side*.87,.44,.74),.014)
 tube('Mirror stalk',2,(side*.81,1.45,1.75),(side*1.03,1.62,1.69),.012)
 sphere('Mirror ivory housing',1,(side*1.045,1.68,1.69),(.045,.10,.073))
 sphere('Mirror reflective face',2,(side*1.047,1.68,1.639),(.037,.082,.012))
 for y in [1.08,1.13,1.18,1.23]:
  for z in [-1.78,-1.67,-1.56,-1.45]:box('Rear cooling louvres',11,(side*.875,y,z),(.013,.018,.072),.007)
 # Round lamps with concentric bezels and lens flutes.
 tube('Headlamp chrome bezel',2,(side*.60,.88,2.015),(side*.60,.88,2.097),.153,48)
 sphere('Headlamp convex lens',10,(side*.60,.88,2.104),(.132,.132,.035))
 for dx in [-.075,-.038,0,.038,.075]:line('Headlamp lens flutes',1,[(side*.60+dx,.80,2.133),(side*.60+dx,.96,2.133)],.002)
 tube('Front indicators',5,(side*.70,1.22,2.01),(side*.70,1.22,2.05),.048,24)
 box('Rear lamp chrome',2,(side*.66,.73,-2.075),(.12,.25,.045),.04)
 box('Rear ruby lenses',6,(side*.66,.74,-2.105),(.095,.20,.025),.035)
# Driver-visible dashboard, single round speedometer and working steering rim.
box('Ivory dashboard',1,(0,1.28,1.84),(1.60,.12,.28),.035)
box('Dashboard dark top',11,(0,1.345,1.85),(1.60,.025,.28),.012)
tube('Speedometer chrome bezel',2,(.43,1.29,1.69),(.43,1.29,1.685),.080,32)
tube('Speedometer black face',11,(.43,1.29,1.684),(.43,1.29,1.68),.067,32)
line('Speedometer needle',1,[(.43,1.29,1.677),(.405,1.335,1.677)],.003)
for side in [-1,1]:
 box('Front seat cushion',11,(side*.43,.87,1.00),(.62,.16,.56),.07)
 box('Front seat back',7,(side*.43,1.13,.75),(.62,.50,.13),.06)
tube('Steering column',11,(.43,1.02,1.88),(.43,1.29,1.75),.022)
steering_start=set(bpy.data.objects)
ring('Ivory steering rim',1,(.43,1.29,1.75),.215,.017)
for angle in [0,math.tau/3,2*math.tau/3]:
 line('Steering spokes',2,[(.43,1.29,1.75),(.43+.20*math.sin(angle),1.29+.20*math.cos(angle),1.75)],.009)
sphere('Steering horn button',1,(.43,1.29,1.744),(.045,.045,.015))
for obj in set(bpy.data.objects)-steering_start:
 obj['attachment']='steering';obj['pivot']=(.43,1.29,1.75)
# VW monogram, made as geometry.
ring('VW emblem ring',2,(0,1.12,2.133),.148,.012)
line('VW emblem V',2,[(-.09,1.21,2.14),(0,1.10,2.14),(.09,1.21,2.14)],.011)
line('VW emblem W',2,[(-.11,1.13,2.14),(-.055,1.025,2.14),(0,1.085,2.14),(.055,1.025,2.14),(.11,1.13,2.14)],.011)
for z in [-2.15,2.15]:
 box('Ivory bumpers',1,(0,.47,z),(1.77,.14,.16),.065)
 for x in [-.60,.60]:box('Bumper overriders',2,(x,.52,z),(.065,.24,.19),.025)
 box('Black registration plate',11,(0,.58,z*1.035),(.49,.105,.018),.009)
window_frame('Rear window seal',(0,1.63,-2.078),1.065,.375)
glazing('Rear window',(0,1.63,-2.08),(1.05,.36,0),'rear')
box('Rear engine hatch seam',11,(0,.89,-2.087),(1.13,.43,.01),.06)
box('Rear engine hatch',0,(0,.89,-2.097),(1.11,.41,.012),.055)
box('Engine hatch handle',2,(0,1.05,-2.12),(.13,.025,.027),.009)
# Visible bent tailpipe with a dark bore, smoke emitter matches its mouth.
tube('Exhaust silencer',11,(-.42,.22,-1.77),(.42,.22,-1.77),.09,24)
tube('Tailpipe chrome',2,(-.49,.24,-1.79),(-.61,.23,-2.23),.025,24)
tube('Tailpipe dark bore',3,(-.61,.23,-2.231),(-.614,.23,-2.244),.019,24)
# Wheel meshes are exported relative to their suspension pivots.
for index,(side,z) in enumerate([(-1,-1.2),(-1,1.2),(1,-1.2),(1,1.2)]):
 start=set(bpy.data.objects);x=side*.72;y=.24
 # Rounded tyres and ivory steel wheels with polished domed hubcaps.
 bpy.ops.mesh.primitive_torus_add(major_segments=48,minor_segments=12,location=point((x,y,z)),major_radius=.254,minor_radius=.075,rotation=(0,math.pi/2,0));finish(bpy.context.object,'Rounded road tyre',3)
 tube('Ivory steel wheel',1,(x-side*.075,y,z),(x+side*.075,y,z),.215,48)
 sphere('Polished domed hubcap',2,(x+side*.092,y,z),(.045,.144,.144))
 ring('Tyre sidewall bead',3,(x+side*.071,y,z),.272,.006,'x')
 for tick in range(40):
  a=tick*math.tau/40
  tube('Fine tyre tread',11,(x-.055,y+.328*math.cos(a),z+.328*math.sin(a)),(x+.055,y+.328*math.cos(a+.025),z+.328*math.sin(a+.025)),.004,5)
 for o in set(bpy.data.objects)-start:o['attachment']='wheel'+str(index);o['pivot']=(x,y,z)
# Roof rack in chrome and warm timber, with strapped vintage longboard.
for z in [-1.35,.95]:
 for side in [-1,1]:tube('Roof rack feet',2,(side*.73,2.0,z),(side*.73,2.19,z),.018)
 tube('Roof rack crossbars',2,(-.79,2.19,z),(.79,2.19,z),.018)
for x in [-.65,-.43,-.21,0,.21,.43,.65]:box('Ash roof rack slats',8,(x,2.20,-.20),(.09,.025,2.65),.01)
for side in [-1,1]:line('Roof rack perimeter',2,[(side*.79,2.20,-1.50),(side*.79,2.33,-1.50),(side*.79,2.33,1.10),(side*.79,2.20,1.10)],.014)
sphere('Honey longboard',9,(.18,2.28,-.25),(.28,.055,1.62))
box('Longboard ivory stringer',1,(.18,2.335,-.25),(.038,.008,2.66),.004)
for z in [-1.05,.70]:line('Canvas board straps',7,[(-.16,2.23,z),(-.12,2.34,z),(.47,2.34,z),(.50,2.23,z)],.014)
box('Vintage luggage',7,(-.47,2.36,-.60),(.36,.27,.71),.05)
for z in [-.84,-.38]:box('Luggage leather belts',8,(-.47,2.36,z),(.365,.278,.025),.008)
# Small daisies and peace motifs give restrained hand-painted hippy character.
for side in [-1,1]:
 for z,y,r in [(-.35,.86,.10),(.12,.69,.075),(-.62,1.05,.065)]:
  for i in range(8):
   a=i*math.tau/8;sphere('Ivory daisy petals',1,(side*.883,y+math.sin(a)*r*.65,z+math.cos(a)*r*.65),(.007,r*.36,r*.36))
  sphere('Golden daisy centres',9,(side*.896,y,z),(.009,r*.32,r*.32))
 ring('Peace roundel',9,(side*.893,.91,.43),.12,.009,'x')
 line('Peace symbol',9,[(side*.895,1.03,.43),(side*.895,.79,.43)],.009)
 for sign in [-1,1]:line('Peace branches',9,[(side*.895,.91,.43),(side*.895,.825,.43+sign*.085)],.009)
# Export by material and attachment to keep draw calls bounded.
for obj in bpy.context.scene.objects:
 if obj.type=='MESH':
  for face in obj.data.polygons:face.use_smooth=True
  if obj.name.startswith('Seafoam lower'):
   bpy.context.view_layer.objects.active=obj
   modifier=obj.modifiers.new('Panel weighted normals','WEIGHTED_NORMAL');modifier.keep_sharp=True
   bpy.ops.object.modifier_apply(modifier=modifier.name)
bpy.context.view_layer.update();groups={}
for o in list(bpy.context.scene.objects):
 if o.type!='MESH' or 'attachment' not in o:continue
 evaluated=o.evaluated_get(bpy.context.evaluated_depsgraph_get());me=evaluated.to_mesh();me.calc_loop_triangles();key=(o['attachment'],o['material']);g=groups.setdefault(key,{'vertices':[],'triangles':[],'normals':[]});offset=len(g['vertices'])//3;pivot=o.get('pivot',(0,0,0))
 for v in me.vertices:
  p=o.matrix_world@v.co;g['vertices'].extend(round(float(c),5) for c in (p.x-pivot[0],p.z-pivot[1],-p.y-pivot[2]))
 for normal in me.vertex_normals:
  n=(o.matrix_world.to_3x3()@normal.vector).normalized();g['normals'].extend(round(float(c),6) for c in (n.x,n.z,-n.y))
 for t in me.loop_triangles:g['triangles'].extend(offset+i for i in t.vertices)
 evaluated.to_mesh_clear()
parts=[dict(name=specs[m][0]+' '+a,attachment=a,material=m,**g) for (a,m),g in groups.items()]
geometry=json.dumps(dict(materials=[dict(colour=c,metallic=m,smoothness=1-r,paint=False) for _,c,m,r in specs],parts=parts),separators=(',',':')).encode()
(OUTPUT/'candidate-mesh.json').write_bytes(geometry)
reference=json.loads((ROOT/'manifest.json').read_text())
actual=hashlib.sha256(geometry).hexdigest()
report=dict(blender=bpy.app.version_string,candidateMeshSha256=actual,reviewedMeshSha256=reference['meshSha256'],byteIdentical=actual==reference['meshSha256'],parts=len(parts),note='Editable generator output. For the reviewed release use mesh.json and pack_reviewed.py. Modified exports need a new pack version and revision.')
(OUTPUT/'generation.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
