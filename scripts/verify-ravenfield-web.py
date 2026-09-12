import bpy, sys, json, math, argparse
from pathlib import Path
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
def armature_rest(rig):
    return {b.name: rig.matrix_world @ b.matrix_local for b in rig.data.bones}
def armature_pose(rig):
    return {b.name: rig.matrix_world @ b.matrix for b in rig.pose.bones}
def palm_frame(wrist,index,pinky):
    y=((index+pinky)*.5-wrist).normalized(); x=index-pinky
    x=(x-y*x.dot(y)).normalized(); z=x.cross(y).normalized()
    return Matrix((x,y,z)).transposed().to_quaternion()
def evaluated_mesh(obj,transform,triangles=False):
    evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh()
    try:
        coords=np.empty(len(mesh.vertices)*3,dtype=np.float64);mesh.vertices.foreach_get('co',coords)
        world=np.asarray(transform@evaluated.matrix_world)
        points=coords.reshape(-1,3)@world[:3,:3].T+world[:3,3]
        if not triangles:return points
        mesh.calc_loop_triangles()
        return points,[tuple(t.vertices) for t in mesh.loop_triangles],None
    finally:evaluated.to_mesh_clear()
parser=argparse.ArgumentParser(description='Independent first thumb-index web-space verification; requires a two-rig inspection scene in aligned world metres.')
parser.add_argument('--output',required=True,type=Path)
parser.add_argument('--require-pass',action='store_true')
parser.add_argument('--anchors',type=Path,help='Track previously captured triangles/barycentrics without recapturing after shape edits')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
OUT=args.output.resolve()
frozen=json.loads(args.anchors.read_text(encoding='utf-8')) if args.anchors else None
OUT.mkdir(parents=True,exist_ok=True)
scene=bpy.context.scene
all_rigs=[o for o in scene.objects if o.type=='ARMATURE']
source_rigs=[o for o in all_rigs if 'j_wrist_le' in o.data.bones]
rf_rigs=[o for o in all_rigs if 'Hand.L' in o.data.bones]
if len(source_rigs)!=1 or len(rf_rigs)!=1 or source_rigs[0]==rf_rigs[0]:
    raise RuntimeError('Requires separate COD source and RF armatures in an aligned inspection scene; final combined exports are unsupported')
rigs={'source':source_rigs[0],'rf':rf_rigs[0]}
meshes={kind:[o for o in scene.objects if o.type=='MESH' and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers) and (kind=='rf' or o.name.startswith('COD_ReferenceHands'))] for kind,rig in rigs.items()}
if not all(meshes.values()):raise RuntimeError('Missing source reference-hand or RF hand meshes')
weapons=[o for o in scene.objects if o.name.startswith('COD_Weapon')]
original={o:o.hide_render for o in scene.objects}
for o in meshes['source']+meshes['rf']+weapons: o.hide_set(False)
def names(kind, side):
    if kind=='source': return [f'j_{n}_{side}_{i}' for n,i in [('index',1),('index',2),('thumb',2),('thumb',3)]]+[f'j_wrist_{side}',f'j_pinky_{side}_1']
    s='L' if side=='le' else 'R'
    return [f'Index.{s}',f'Index.{s}.002',f'Thumb.{s}.002',f'Thumb.{s}.001',f'Hand.{s}',f'Pinky.{s}']
def marker(p, color, radius=.0013):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,radius=radius,location=p)
    o=bpy.context.object; o.color=(*color,1);o.show_in_front=True; return o
def line(a,b,color,radius=.00035):
    bpy.ops.mesh.primitive_cylinder_add(vertices=8,radius=radius,depth=(b-a).length,location=(a+b)*.5)
    o=bpy.context.object; o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();o.color=(*color,1);o.show_in_front=True;return o
def render(kind,side,stage,points,bones):
    for o in scene.objects: o.hide_render=o.type=='MESH'
    for o in meshes[kind]: o.hide_render=False;o.color=(.66,.71,.78,1) if kind=='rf' else (.55,.69,.65,1)
    i1,i2,t2,t3,w,p=bones
    center=(i1+t2)*.5
    normal=(i1-w).cross(t2-w).normalized()
    created=[]
    colors=[(1,.12,.04),(1,.8,.02),(.15,.5,1)]
    for k,point in enumerate(points): created.append(marker(Vector(point),colors[k]))
    for a,b in [(i1,i2),(t2,t3)]:
        created += [line(a,b,(.9,.1,.8)),marker(a,(.9,.1,.8),.00085)]
    # Distal-open first web space, oblique enough to see its saddle surface.
    direction=(normal*.8+(i2+t3-i1-t2).normalized()*.55).normalized()
    camera=bpy.data.objects.get('RF Inspection Camera')
    if camera is None:
        camera=bpy.data.objects.new('Web Verification Camera',bpy.data.cameras.new('Web Verification Camera'));scene.collection.objects.link(camera)
    camera.location=center+direction*.24
    camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO';camera.data.ortho_scale=.14;scene.camera=camera
    scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO'
    scene.display.shading.color_type='OBJECT';scene.display.shading.show_shadows=True
    scene.display.shading.show_cavity=True;scene.display.shading.cavity_type='BOTH'
    scene.display.shading.background_type='WORLD';scene.world.color=(.04,.04,.04)
    scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
    scene.render.filepath=str(OUT/f'{side}-{kind}-{stage}.png');bpy.ops.render.render(write_still=True)
    for o in created:bpy.data.objects.remove(o,do_unlink=True)
report={'rule':'In bind/rest pose cast from midpoint(index PIP, thumb IP) toward midpoint(index MCP, thumb MCP), at three offsets along normal=cross(indexMCP-wrist,thumbMCP-wrist). Capture first surface hit, then fixed triangle barycentrics. Magenta bones mark index proximal segment and thumb distal segment. Red/yellow/blue samples correspond to depth offsets -0.08/0/+0.08 palm width. Markers/bones use x-ray display; mesh shape is not modified.',
        'validationScope':'Visually checked on this COD/RF mesh pair: first thumb-index web saddle. This is not an automatic anatomical guarantee for different gloves, open finger poses or topology. Reject rays landing on a finger, a detached accessory, or outside the inter-root span; do not silently fall back to a palm surface.',
        'coordinateDefinitions':{'canonicalBind':'Metres, wrist origin, palm_frame(wrist,indexMCP,pinkyMCP) axes; includes bind finger-root placement differences.', 'webLocalBind':'Metres, origin midpoint(indexMCP,thumbMCP), x from thumbMCP to indexMCP, z wrist/indexMCP/thumbMCP normal, y=z cross x; isolates saddle relative to its two roots.', 'world':'Already aligned idle-inspection scene world metres; source and RF both include their current poses.', 'weaponDistance':'Unsigned nearest surface distance, not penetration depth or collision clearance.'}, 'landmarks':{}}
for kind,rig in rigs.items():
    rig.data.pose_position='REST';bpy.context.view_layer.update()
    geometry={o:evaluated_mesh(o,Matrix.Identity(4),True) for o in meshes[kind]}
    rests=armature_rest(rig)
    for side in ('le','ri'):
        bone_names=names(kind,side);bones=[rests[n].translation for n in bone_names]
        i1,i2,t2,t3,w,p=bones
        normal=(i1-w).cross(t2-w).normalized();width=(i1-p).length
        origin=(i2+t3)*.5;end=(i1+t2)*.5;direction=(end-origin).normalized()
        web_x=(i1-t2).normalized();web_y=normal.cross(web_x).normalized()
        anchors=[]
        canonical=palm_frame(w,i1,p).inverted()
        for depth in (() if frozen else (-.08,0,.08)):
            start=origin+normal*(depth*width)
            hits=[]
            for obj,(verts,tris,groups) in geometry.items():
                bvh=BVHTree.FromPolygons(verts,tris,all_triangles=True)
                point,n,face,dist=bvh.ray_cast(start,direction,width*2)
                if point is not None:
                    tri=tris[face]
                    bary=barycentric_transform(point,*[Vector(verts[i]) for i in tri],Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1)))
                    along=(point-t2).dot(i1-t2)/(i1-t2).length_squared
                    if not .05 < along < .95: continue
                    hits.append((dist,{'object':obj.name,'triangle':list(tri),'weights':list(bary),'bindPointM':list(point),'canonicalBindPointM':list(canonical@(point-w)),
                                       'webLocalBindPointM':[(point-end).dot(axis) for axis in (web_x,web_y,normal)],'interRootFraction':along,'depthWidth':depth,'rayDistanceM':dist}))
            if not hits:raise RuntimeError(f'No web candidate {kind}/{side}/{depth}')
            anchors.append(min(hits,key=lambda x:x[0])[1])
        if frozen:
            anchors=json.loads(json.dumps(frozen['landmarks'][f'{kind}-{side}']['anchors']))
            for a in anchors:
                verts=geometry[bpy.data.objects[a['object']]][0]
                point=sum((Vector(verts[i])*wt for i,wt in zip(a['triangle'],a['weights'])),Vector())
                a.update(bindPointM=list(point),canonicalBindPointM=list(canonical@(point-w)),webLocalBindPointM=[(point-end).dot(axis) for axis in (web_x,web_y,normal)])
        report['landmarks'][f'{kind}-{side}']={'anchors':anchors,'boneNames':bone_names,'bindBonesM':[list(v) for v in bones]}
        render(kind,side,'bind',[a['bindPointM'] for a in anchors],bones)
    rig.data.pose_position='POSE';bpy.context.view_layer.update()
    posed=armature_pose(rig)
    geometry={o.name:evaluated_mesh(o,Matrix.Identity(4)) for o in meshes[kind]}
    for side in ('le','ri'):
        item=report['landmarks'][f'{kind}-{side}'];points=[]
        for a in item['anchors']:
            point=sum((geometry[a['object']][i]*wt for i,wt in zip(a['triangle'],a['weights'])),np.zeros(3))
            a['posedPointM']=point.tolist();points.append(point)
        render(kind,side,'posed',points,[posed[n].translation for n in item['boneNames']])
allv=[];allt=[]
for obj in weapons:
    v,t,_=evaluated_mesh(obj,Matrix.Identity(4),True);base=len(allv);allv.extend(v);allt.extend(tuple(i+base for i in tri) for tri in t)
weapon=BVHTree.FromPolygons(allv,allt,all_triangles=True)
for side in ('le','ri'):
    comparisons=[]
    for a,b in zip(report['landmarks'][f'source-{side}']['anchors'],report['landmarks'][f'rf-{side}']['anchors']):
        pa,pb=Vector(a['posedPointM']),Vector(b['posedPointM'])
        ca,cb=Vector(a['canonicalBindPointM']),Vector(b['canonicalBindPointM'])
        wa,wb=Vector(a['webLocalBindPointM']),Vector(b['webLocalBindPointM'])
        comparisons.append({'webLocalBindDeltaM':list(wb-wa),'webLocalBindDistanceM':(wb-wa).length,'canonicalBindDeltaM':list(cb-ca),'canonicalBindDistanceM':(cb-ca).length,'worldDeltaM':list(pb-pa),'worldDistanceM':(pb-pa).length,'sourceWeaponDistanceM':weapon.find_nearest(pa)[3],'rfWeaponDistanceM':weapon.find_nearest(pb)[3]})
    report[side]=comparisons
report['maxCorrespondenceErrorM']=max(p['worldDistanceM'] for side in ('le','ri') for p in report[side])
report['toleranceM']=.005
report['passed']=report['maxCorrespondenceErrorM']<=report['toleranceM']
report['frozenAnchorsFrom']=str(args.anchors) if args.anchors else None
(OUT/'landmarks.json').write_text(json.dumps(report,indent=2),encoding='utf-8')


print('RAVENFIELD_WEB_VERIFICATION '+json.dumps({k:report[k] for k in ('passed','maxCorrespondenceErrorM','toleranceM','le','ri')}),flush=True)
if args.require_pass and not report['passed']:
    raise AssertionError(f"First web-space corresponding-point mismatch: {report['maxCorrespondenceErrorM']*1000:.3f} mm > 5 mm")
