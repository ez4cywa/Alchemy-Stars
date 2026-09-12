"""Local web shape and baked skin controls on an exported RF hand copy.

Original bones and topology stay unchanged. Local weights are reassigned to
mirrored auxiliary bones without increasing per-vertex influence counts.
"""
from __future__ import annotations

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

import ravenfield_adapter as rf
from ravenfield_contact import evaluated_mesh, PalmContactFitter


class GripContactFitter(PalmContactFitter):
    def __init__(self, source, target, source_meshes, target_meshes, transform, web_samples):
        self.web_samples = web_samples
        self.dynamic = None
        super().__init__(source,target,source_meshes,target_meshes,transform)

    def points(self, kind):
        palms = super().points(kind)
        if self.dynamic is not None:
            return palms
        webs = self.web_samples.points(kind)
        return {side:np.concatenate((palms[side],webs[side])) for side in ('le','ri')}

    def fit(self, pose, rest, config, report, continuity=None):
        if self.dynamic is not None:
            self.dynamic.reset()
            rf.fit_hands(self.target,pose,rest,config,{'warnings':[]},dict(continuity) if continuity is not None else None)
            baseline_web=self.web_samples.points('rf')
        detail = super().fit(pose,rest,config,report,continuity)
        if self.dynamic is not None:
            web = self.dynamic.apply(pose,config)
            for side,setting in (('le','left'),('ri','right')):
                item=detail[setting]
                item['afterPointsM']=web[setting]['afterPointsM']
                item['sourcePointsM']=web[setting]['sourcePointsM']
                item['beforePointsM']+=baseline_web[side].tolist()
                item['gripAfterPalmPointsM']=web[setting]['beforePointsM']
                item['webAfterPalmPointsM']=web[setting]['beforePointsM'][-3:]
                item['webCorrectionM']=web[setting]['shiftM']
                item['webControlShiftsM']=web[setting]['controlShiftsM']
                item['maxWebControlShiftM']=float(np.linalg.norm(web[setting]['controlShiftsM'],axis=1).max())
                item['webCorrectionLimited']=web[setting]['limited']
                item['authoredPointsM']=web[setting]['authoredPointsM']
                item['weaponContact']=web[setting]['weaponContact']
                item['targetPointsM']=item['sourcePointsM']
                errors=np.linalg.norm(np.array(item['afterPointsM'])-np.array(item['sourcePointsM']),axis=1)
                item.update(maxErrorM=float(errors.max()),rmsErrorM=float(np.sqrt(np.mean(errors**2))),passed=bool(errors.max()<=.005))
                item['beforeMaxErrorM']=float(np.linalg.norm(np.array(item['beforePointsM'])-np.array(item['sourcePointsM']),axis=1).max())
                rf.require(item['maxErrorM']<=item['beforeMaxErrorM']+1e-5,f'{setting} local grip correction worsened the same-frame baseline')
            detail['passed']=all(detail[s]['passed'] for s in ('left','right'))
            detail['method']='reference-bind-shape-joint-grip-controls-with-authored-release'
        detail['scope'] = 'Four palm and three fixed web points; left support targets close reference weapon contacts, with authored release'
        for side in ('left','right'):
            errors = np.linalg.norm(np.array(detail[side]['afterPointsM'])-np.array(detail[side]['sourcePointsM']),axis=1)
            detail[side]['webMaxErrorM'] = float(errors[-3:].max())
            detail[side]['palmMaxErrorM'] = float(errors[:-3].max())
        return detail


def prepare_grip(source,target,source_meshes,target_meshes,transform,pose,rest,config,report):
    for obj in [target, *target_meshes]:
        rf.require(max(abs(obj.matrix_world[i][j] - float(i == j)) for i in range(4) for j in range(4)) < 1e-6,
                   'Local hand fit requires normalized RF object transforms')
    samples=WebContactSamples(source,target,source_meshes,target_meshes,transform)
    original_bones={b.name:(b.head_local.copy(),b.tail_local.copy()) for b in target.data.bones}
    original_vertices={obj:np.array([v.co for v in obj.data.vertices]) for obj in target_meshes}
    calibration={'method':'reference-pose-local-web-shape','originalBonesPreserved':True}
    fitter=GripContactFitter(source,target,source_meshes,target_meshes,transform,samples)
    calibration['referenceRefinement']=refine_web_reference(fitter,pose,rest,config,palm_first=True)
    fitter.dynamic=DynamicWebCorrection(fitter)
    calibration['dynamicWeb']=fitter.dynamic.description
    calibration['leftSupportReference']=fitter.dynamic.support_contacts.describe()
    calibration['contract']={'version':1,'targetToleranceM':.005,'maximumControlShiftM':.03,
                             'preserveOriginalBones':True,'preservePerVertexInfluenceCounts':True}
    if not calibration['leftSupportReference']['enabled']:
        report.setdefault('warnings',[]).append('参考帧未建立左掌武器接触，请选择靠近武器的持枪姿势；仍执行手型拟合 / '
                                                'No left-palm weapon contact captured; choose a holding reference pose. Hand-shape fitting remains enabled.')
    calibration['maxBindVertexShiftM']=max(float(np.linalg.norm(np.array([v.co for v in obj.data.vertices])-before,axis=1).max()) for obj,before in original_vertices.items())
    for name,(head,tail) in original_bones.items():
        bone=target.data.bones[name]
        rf.require((bone.head_local-head).length<1e-6 and (bone.tail_local-tail).length<1e-6,
                   f'Local hand fit moved an original bind bone: {name}')
    report['localHandFit']=calibration
    return fitter


class DynamicWebCorrection:
    """Localized translation baked as standard auxiliary skin bones.

    Each auxiliary bone mirrors one original bind/pose at a welded skin vertex.
    Transferring the entire original weight preserves neutral skin and influence
    counts. The smooth control field becomes that bone's world-space translation.
    """
    def __init__(self,fitter):
        self.fitter,self.target=fitter,fitter.target
        self.samples=GripSurfaceSamples(fitter)
        from ravenfield_weapon_contact import SupportContacts
        self.support_contacts=SupportContacts(self.samples,fitter.transform)
        self.helpers,self.influence={},{}
        baseline={obj:evaluated_mesh(obj,Matrix.Identity(4)) for obj in fitter.meshes['rf']}
        original_counts={obj:[sum(g.weight>1e-8 for g in v.groups) for v in obj.data.vertices] for obj in fitter.meshes['rf']}
        rig=self.target
        for side in ('le','ri'):
            additions={}
            masks={}
            bindings={}
            centers=[]
            for obj,triangle,_ in self.samples.anchors['rf',side]:
                centers.extend([list(obj.data.vertices[i].co) for i in triangle])
            centers=np.unique(np.round(centers,6),axis=0)
            count=len(centers)
            for mesh_index,obj in enumerate(fitter.meshes['rf']):
                points=np.array([v.co for v in obj.data.vertices])
                constraints=[]
                desired=[]
                selected=[]
                for mesh,triangle,bary in self.samples.anchors['rf',side]:
                    if mesh==obj:
                        constraints.append((triangle,bary));desired.append(1.)
                        selected.append(sum((points[i]*w for i,w in zip(triangle,bary)),np.zeros(3)))
                if not selected:continue
                center=np.mean(selected,axis=0)
                support=max(float(np.linalg.norm(np.array(selected)-center,axis=1).max())+.025,.045)
                unique,weld,active,lookup,h=local_system(points,[tuple(p.vertices) for p in obj.data.polygons],center,support,hand_vertices(obj,rig,side))
                a=np.zeros((len(constraints),len(active)))
                for row,(triangle,bary) in enumerate(constraints):
                    for i,w in zip(triangle,bary):
                        if int(weld[i]) in lookup:a[row,lookup[int(weld[i])]]+=w
                basis=np.linalg.solve(h,a.T)
                mask=np.zeros(len(unique))
                mask[active]=np.clip(basis@np.linalg.pinv(a@basis,rcond=1e-8)@np.array(desired),0,1)
                mask=mask[weld]
                mask[mask<1e-5]=0
                blend=cardinal_control_weights(unique[weld],centers)
                masks[obj]=mask[:,None]*blend
                bindings[obj]={}
                groups={g.index:g.name for g in obj.vertex_groups}
                for vertex,amount in zip(obj.data.vertices,mask):
                    if amount<=0:continue
                    for group in vertex.groups:
                        name=groups[group.group]
                        if name in rig.data.bones and group.weight>1e-8:
                            helper=f'RF_Web_{side}_{mesh_index}_{weld[vertex.index]}_{name}'
                            additions[helper]=(name,masks[obj][vertex.index])
                            bindings[obj][vertex.index,name]=helper
            rf.activate(rig)
            bpy.ops.object.mode_set(mode='EDIT')
            try:
                for helper,(original,field) in additions.items():
                    parent=rig.data.edit_bones[original]
                    rf.require(helper not in rig.data.edit_bones, f'Grip helper bone already exists: {helper}')
                    bone=rig.data.edit_bones.new(helper)
                    bone.head,bone.tail=parent.head.copy(),parent.tail.copy()
                    bone.roll=parent.roll
                    bone.parent=parent
                    bone.use_connect=False
                    bone.use_deform=True
            finally:bpy.ops.object.mode_set(mode='OBJECT')
            self.helpers[side]=additions
            for obj,mask in masks.items():
                groups={g.index:g.name for g in obj.vertex_groups}
                data=[[(groups[g.group],g.weight) for g in v.groups] for v in obj.data.vertices]
                for helper in set(bindings[obj].values()):
                    if obj.vertex_groups.get(helper) is None:obj.vertex_groups.new(name=helper)
                for vertex,control_weights,weights in zip(obj.data.vertices,mask,data):
                    amount=float(control_weights.sum())
                    if amount<=0:continue
                    for original,weight in weights:
                        helper=bindings[obj].get((vertex.index,original))
                        if helper:
                            obj.vertex_groups[original].remove([vertex.index])
                            obj.vertex_groups[helper].add([vertex.index],weight,'REPLACE')
            self.influence[side]=np.array([sum((masks[obj][i]*w for i,w in zip(triangle,bary)),np.zeros(count)) for obj,triangle,bary in self.samples.anchors['rf',side]])
            rf.require(self.influence[side].sum(axis=1).min()>.5 and np.linalg.matrix_rank(self.influence[side])>=3,'Insufficient independent grip control influence')
        self.reset()
        bpy.context.view_layer.update()
        maximum=max(float(np.linalg.norm(evaluated_mesh(obj,Matrix.Identity(4))-before,axis=1).max()) for obj,before in baseline.items())
        rf.require(maximum<1e-5,'Neutral web helper bones changed the original skin')
        for obj,counts in original_counts.items():
            rf.require(all(sum(g.weight>1e-8 for g in vertex.groups)<=count for vertex,count in zip(obj.data.vertices,counts)),
                       'Local grip correction increased per-vertex skin influences')
        self.description={'method':'joint-palm-web-mirrored-skin-controls','maxShiftM':.03,
                          'leftSupportContact':'fixed-reference-weapon-triangles-with-authored-release',
                          'helperBones':sum(len(h) for h in self.helpers.values()),'neutralSkinErrorM':maximum,
                          'perVertexInfluenceCountPreserved':True,
                          'influence':{s:v.tolist() for s,v in self.influence.items()}}

    def reset(self):
        for mapping in self.helpers.values():
            for helper in mapping:self.target.pose.bones[helper].matrix_basis.identity()
        bpy.context.view_layer.update()

    def apply(self,pose,config):
        from mathutils import Euler
        import math
        source=self.samples.points('source')
        actual=self.samples.points('rf')
        result={}
        for side,setting in (('le','left'),('ri','right')):
            wrist=pose[f'j_wrist_{side}'].translation
            rotation=Euler([math.radians(v) for v in config[setting]['rotation']],'XYZ').to_quaternion()
            offset=Vector(config[setting]['position'])
            wanted=np.array([wrist+offset+rotation@(Vector(p)-wrist) for p in source[side]])
            authored=wanted.copy()
            contact={'active':False,'weight':0.}
            if side=='le':
                supported,contact=self.support_contacts.targets(source[side])
                wanted=np.array([wrist+offset+rotation@(Vector(p)-wrist) for p in supported])
            influence=self.influence[side]
            shifts,limited=bounded_control_offsets(influence,wanted-actual[side])
            for helper,(original,field) in self.helpers[side].items():
                matrix=self.target.pose.bones[original].matrix.copy()
                matrix.translation+=Vector(field@shifts)
                self.target.pose.bones[helper].matrix=matrix
            result[setting]={'sourcePointsM':wanted.tolist(),'beforePointsM':actual[side].tolist(),'shiftM':shifts.mean(axis=0).tolist(),
                             'controlShiftsM':shifts.tolist(),'limited':limited,'authoredPointsM':authored.tolist(),'weaponContact':contact}
        bpy.context.view_layer.update()
        after=self.samples.points('rf')
        for side,setting in (('le','left'),('ri','right')):result[setting]['afterPointsM']=after[side].tolist()
        return result


def cardinal_control_weights(points, centers):
    """Smooth positive partition, interpolating each fixed material center."""
    distances=np.linalg.norm(np.asarray(points)[:,None]-np.asarray(centers)[None],axis=2)
    weights=1/np.maximum(distances,1e-12)**4
    weights/=weights.sum(axis=1,keepdims=True)
    return weights


def bounded_control_offsets(influence,desired,limit=.03):
    """Bound each control independently; minimize actual sample residuals.

    Uniformly shrinking an exact solution sacrifices every independent sample
    when only one control is limited. Projected accelerated least squares keeps
    the same displacement bounds without coupling those unrelated residuals.
    """
    a,desired=np.asarray(influence,dtype=float),np.asarray(desired,dtype=float)
    rf.require(a.ndim==2 and a.shape[0]>=3 and desired.shape==(a.shape[0],3)
               and np.isfinite(a).all() and np.isfinite(desired).all()
               and np.linalg.matrix_rank(a)>=3 and np.isfinite(limit) and limit>0,'Invalid local web control system')
    offsets=np.linalg.pinv(a,rcond=1e-8)@desired
    if np.linalg.norm(offsets,axis=1).max()<=limit:
        return offsets,False
    offsets*=np.minimum(1,limit/np.maximum(np.linalg.norm(offsets,axis=1),1e-20))[:,None]
    extrapolated=offsets.copy()
    momentum=1.
    step=1/np.linalg.norm(a,2)**2
    for _ in range(1000):
        proposed=extrapolated-step*(a.T@(a@extrapolated-desired))
        proposed*=np.minimum(1,limit/np.maximum(np.linalg.norm(proposed,axis=1),1e-20))[:,None]
        next_momentum=(1+np.sqrt(1+4*momentum*momentum))/2
        extrapolated=proposed+(momentum-1)/next_momentum*(proposed-offsets)
        change=float(np.linalg.norm(proposed-offsets))
        offsets,momentum=proposed,next_momentum
        if change<1e-11:break
    rf.require(np.isfinite(offsets).all(),'Nonfinite local grip control correction')
    return offsets,True


class GripSurfaceSamples:
    """Fixed palm and web material points, with no per-frame rematching."""
    def __init__(self, fitter):
        self.fitter = fitter
        self.anchors = {(kind, side): fitter.anchors[kind,side] + fitter.web_samples.anchors[kind,side]
                        for kind in ('source','rf') for side in ('le','ri')}

    def points(self, kind):
        palms=super(GripContactFitter,self.fitter).points(kind)
        webs=self.fitter.web_samples.points(kind)
        return {side:np.concatenate((palms[side],webs[side])) for side in ('le','ri')}


class WebContactSamples:
    """Three material points across the first thumb/index web-space saddle."""
    def __init__(self, source, target, source_meshes, target_meshes, transform):
        self.transform = transform
        self.meshes = {'source': source_meshes, 'rf': target_meshes}
        self.anchors, self.bind_points = {}, {}
        for kind, rig, matrix in (('source', source, transform), ('rf', target, Matrix.Identity(4))):
            previous = rig.data.pose_position
            try:
                rig.data.pose_position = 'REST'
                bpy.context.view_layer.update()
                geometry = {o:evaluated_mesh(o,matrix,True) for o in self.meshes[kind]}
                rest = {n:matrix@m for n,m in rf.armature_rest(rig).items()}
                for side, suffix in (('le','L'),('ri','R')):
                    names = ([f'j_index_{side}_1',f'j_index_{side}_2',f'j_thumb_{side}_2',f'j_thumb_{side}_3',f'j_wrist_{side}',f'j_pinky_{side}_1']
                             if kind=='source' else [f'Index.{suffix}',f'Index.{suffix}.002',f'Thumb.{suffix}.002',f'Thumb.{suffix}.001',f'Hand.{suffix}',f'Pinky.{suffix}'])
                    i1,i2,t2,t3,w,p = [rest[n].translation for n in names]
                    width = (i1-p).length
                    normal = (i1-w).cross(t2-w).normalized()
                    origin, end = (i2+t3)*.5, (i1+t2)*.5
                    direction = (end-origin).normalized()
                    rf.require(width > 1e-5 and normal.length > .9 and direction.length > .9, 'Degenerate first-web landmarks')
                    descendants = {names[4], *[b.name for b in rig.data.bones[names[4]].children_recursive]}
                    surfaces = []
                    for obj,(points,triangles,groups) in geometry.items():
                        allowed = {g.index for g in obj.vertex_groups if g.name in descendants}
                        weights = [sum(weight for index,weight in gs if index in allowed) for gs in groups]
                        triangles = [t for t in triangles if min(weights[i] for i in t) > .25]
                        if triangles:
                            surfaces.append((obj,points,triangles,BVHTree.FromPolygons(points,triangles,all_triangles=True)))
                    anchors, bind = [], []
                    for depth in (-.08,0,.08):
                        start = origin+normal*(width*depth)
                        hits = []
                        for obj,points,triangles,bvh in surfaces:
                            point,_,face,distance = bvh.ray_cast(start,direction,width*2)
                            if point is not None:
                                triangle = triangles[face]
                                bary = barycentric_transform(point,*[Vector(points[i]) for i in triangle],Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1)))
                                hits.append((distance,obj,triangle,bary,point))
                        rf.require(hits,f'No first-web saddle surface: {kind}/{side}/{depth}')
                        _,obj,triangle,bary,point = min(hits,key=lambda h:h[0])
                        anchors.append((obj,triangle,bary))
                        bind.append(list(point))
                    self.anchors[kind,side], self.bind_points[kind,side] = anchors,np.array(bind)
            finally:
                rig.data.pose_position = previous
                bpy.context.view_layer.update()

    def points(self,kind):
        transform = self.transform if kind=='source' else Matrix.Identity(4)
        geometry = {obj:evaluated_mesh(obj,transform) for obj in self.meshes[kind]}
        return {side:np.array([sum((geometry[obj][i]*weight for i,weight in zip(ids,bary)),np.zeros(3))
                               for obj,ids,bary in self.anchors[kind,side]]) for side in ('le','ri')}


def hand_vertices(obj, rig, side):
    """Keep local edits off the other hand and forearm, including small rigs."""
    hand = rig.data.bones['Hand.' + ('L' if side == 'le' else 'R')]
    names = {hand.name, *[bone.name for bone in hand.children_recursive]}
    groups = {group.index for group in obj.vertex_groups if group.name in names}
    return np.array([sum(g.weight for g in v.groups if g.group in groups) > .25 for v in obj.data.vertices])


def local_system(points,faces,center,radius,eligible=None):
    points = np.asarray(points,dtype=float)
    rf.require(points.ndim == 2 and points.shape[1] == 3 and len(points) > 0
               and np.isfinite(points).all() and np.isfinite(center).all()
               and np.isfinite(radius) and radius > 0, 'Invalid local hand geometry')
    _, representative, weld = np.unique(np.round(points,6),axis=0,return_index=True,return_inverse=True)
    unique = points[representative]
    distance = np.linalg.norm(unique-np.asarray(center),axis=1)
    allowed = np.ones(len(unique), dtype=bool)
    if eligible is not None:
        eligible = np.asarray(eligible, dtype=bool)
        rf.require(eligible.shape == (len(points),), 'Invalid local hand mask')
        np.logical_and.at(allowed, weld, eligible)
    active = np.flatnonzero((distance < radius) & allowed)
    rf.require(len(active) > 0, 'No eligible local hand vertices')
    lookup = {int(index):row for row,index in enumerate(active)}
    neighbors = [set() for _ in unique]
    for face in faces:
        for a,b in zip(face,(*face[1:],face[0])):
            a,b=int(weld[a]),int(weld[b])
            if a != b: neighbors[a].add(b);neighbors[b].add(a)
    laplace = np.zeros((len(unique),len(active)))
    for index,adjacent in enumerate(neighbors):
        if index in lookup:laplace[index,lookup[index]]=1
        for other in adjacent:
            if other in lookup:laplace[index,lookup[other]]=-1/len(adjacent)
    h = laplace.T@laplace + np.diag(.002/(1-distance[active]/radius+.05)**2)
    return unique,weld,active,lookup,h


def refine_web_reference(fitter, pose, rest, config, palm_first=False):
    """Bake a smooth local correction into bind vertices, measured in reference skin.

    The 3x3 derivative for every vertex is its actual linear-blend skin matrix.
    Palm samples are pinned while web samples approach the source hand. This is
    a one-time bind update, not per-frame nearest-surface attraction.
    """
    target = fitter.target
    if palm_first:
        palm_fitter= PalmContactFitter(fitter.source,fitter.target,fitter.meshes['source'],fitter.meshes['rf'],fitter.transform)
        palm_fitter.fit(pose,rest,config,{'warnings':[]})
        web_source=fitter.web_samples.points('source')
        from mathutils import Euler
        import math
        desired_webs={}
        for side,setting in (('le','left'),('ri','right')):
            wrist=pose[f'j_wrist_{side}'].translation
            rotation=Euler([math.radians(v) for v in config[setting]['rotation']],'XYZ').to_quaternion()
            shift=Vector(config[setting]['position'])
            desired_webs[setting]=np.array([wrist+shift+rotation@(Vector(p)-wrist) for p in web_source[side]])
    else:
        detail = fitter.fit(pose,rest,config,{'warnings':[]})
        desired_webs={s:np.array(detail[s]['sourcePointsM'])[-3:] for s in ('left','right')}
    rest_matrices = rf.armature_rest(target)
    pose_matrices = rf.armature_pose(target)
    deformation = {n:np.array(pose_matrices[n]@matrix.inverted()) for n,matrix in rest_matrices.items()}
    records = {}
    for side,setting in (('le','left'),('ri','right')):
        web = fitter.web_samples.anchors['rf',side]
        palm = fitter.anchors['rf',side]
        desired_web = desired_webs[setting]
        for obj in fitter.meshes['rf']:
            values = evaluated_mesh(obj,Matrix.Identity(4))
            points = np.array([v.co for v in obj.data.vertices])
            groups = {g.index:g.name for g in obj.vertex_groups}
            skin=[]
            for vertex in obj.data.vertices:
                weights=[(groups[g.group],g.weight) for g in vertex.groups if groups[g.group] in deformation]
                total=sum(weight for _,weight in weights)
                skin.append(sum((deformation[n]*weight for n,weight in weights),np.zeros((4,4)))/total if total>1e-8 else np.eye(4))
            skin=np.array(skin)
            predicted=np.einsum('nij,nj->ni',skin[:,:3,:3],points)+skin[:,:3,3]
            rf.require(np.linalg.norm(predicted-values,axis=1).max()<1e-5,'Unsupported nonlinear skinning during local hand calibration')
            constraints=[]
            for mesh,triangle,bary in palm:
                if mesh==obj:constraints.append((triangle,bary,np.zeros(3)))
            selected=[]
            for i,(mesh,triangle,bary) in enumerate(web):
                if mesh!=obj:continue
                actual=sum((values[index]*weight for index,weight in zip(triangle,bary)),np.zeros(3))
                constraints.append((triangle,bary,desired_web[i]-actual))
                selected.append(sum((points[index]*weight for index,weight in zip(triangle,bary)),np.zeros(3)))
            if not selected:continue
            unique,weld,active,lookup,h = local_system(points,[tuple(p.vertices) for p in obj.data.polygons],np.mean(selected,axis=0),.055,hand_vertices(obj,target,side))
            a=np.zeros((len(constraints)*3,len(active)*3))
            desired=np.zeros(len(constraints)*3)
            for row,(triangle,bary,delta) in enumerate(constraints):
                desired[row*3:row*3+3]=delta
                for index,weight in zip(triangle,bary):
                    if int(weld[index]) in lookup:
                        col=lookup[int(weld[index])]*3
                        a[row*3:row*3+3,col:col+3] += weight*skin[index,:3,:3]
            basis=np.linalg.solve(np.kron(h,np.eye(3)),a.T)
            offsets=(basis@np.linalg.pinv(a@basis,rcond=1e-7)@desired).reshape((-1,3))
            rf.require(np.isfinite(offsets).all() and np.linalg.norm(offsets,axis=1).max()<.06,'Reference web correction exceeds 60 mm')
            delta=np.zeros_like(unique)
            delta[active]=offsets
            for vertex,point,offset in zip(obj.data.vertices,points,delta[weld]):vertex.co=Vector(point+offset)
            obj.data.update()
            records[setting]={'maxVertexShiftM':float(np.linalg.norm(offsets,axis=1).max()),
                              'constraintResidualM':float(np.linalg.norm((a@offsets.ravel()-desired).reshape((-1,3)),axis=1).max())}
    bpy.context.view_layer.update()
    final=fitter.fit(pose,rest,config,{'warnings':[]})
    return {'sides':records,'maxErrorM':max(final[s]['maxErrorM'] for s in ('left','right'))}
