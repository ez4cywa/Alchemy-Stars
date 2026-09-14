"""Actual Lua lifecycle and 3D hierarchy tests; needs lupa (optional path argv[1]).

Does not validate Unity API binding, importer behavior or game scheduling.
"""
import pathlib
import sys
if len(sys.argv) > 1:
    sys.path.insert(0, sys.argv[1])
from lupa import LuaRuntime
lua = LuaRuntime()
lua.execute(r'''
local vm,qm={},{}
function V(x,y,z) return setmetatable({x=x,y=y,z=z,sqrMagnitude=x*x+y*y+z*z},vm) end
vm.__add=function(a,b) return V(a.x+b.x,a.y+b.y,a.z+b.z) end
vm.__sub=function(a,b) return V(a.x-b.x,a.y-b.y,a.z-b.z) end
vm.__mul=function(a,b) return V(a.x*b,a.y*b,a.z*b) end
function Q(x,y,z,w) return setmetatable({x=x,y=y,z=z,w=w},qm) end
function AA(deg,x,y,z)
    local half=deg*math.pi/360; local s=math.sin(half)/math.sqrt(x*x+y*y+z*z)
    return Q(x*s,y*s,z*s,math.cos(half))
end
local function product(a,b)
    return Q(a.w*b.x+a.x*b.w+a.y*b.z-a.z*b.y,
        a.w*b.y-a.x*b.z+a.y*b.w+a.z*b.x,
        a.w*b.z+a.x*b.y-a.y*b.x+a.z*b.w,
        a.w*b.w-a.x*b.x-a.y*b.y-a.z*b.z)
end
Quaternion={Inverse=function(q) return Q(-q.x,-q.y,-q.z,q.w) end,
    Angle=function(a,b)
        local dot=math.abs(a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w)
        return 2*math.acos(math.min(1,dot))*180/math.pi
    end}
qm.__mul=function(a,b)
    if b.w then return product(a,b) end
    local q=product(product(a,Q(b.x,b.y,b.z,0)),Quaternion.Inverse(a))
    return V(q.x,q.y,q.z)
end
I=Q(0,0,0,1)
function nearV(a,b) assert((a-b).sqrMagnitude<1e-16,"vector mismatch") end
function nearQ(a,b) assert(Quaternion.Angle(a,b)<0.00001,"rotation mismatch") end
function behaviour(name) _G[name]={} end
baseP=V(3,-2,1); baseQ=AA(7,1,2,3)
PlayerCamera={fpCameraLocalPosition=baseP,fpCameraLocalRotation=baseQ,
    isUsingFPCamera=true,isUsingOverrideCamera=false}
Weapon={}
local weapon={animator={isActiveAndEnabled=true}}
Player={actor={activeWeapon=weapon,isDead=false,isSeated=false}}
rig={position=V(8,3,-4),rotation=AA(53,1,3,2),scale=2}
rig.InverseTransformPoint=function(p) return (Quaternion.Inverse(rig.rotation)*(p-rig.position))*(1/rig.scale) end
rig.TransformPoint=function(p) return rig.position+rig.rotation*(p*rig.scale) end
motion={gameObject={activeInHierarchy=true}}
-- Full hierarchy: rig * animatedRoot * localMotion = rig * D.
function pose(rootQ,rootP,desiredQ,desiredP)
    motion.localRotation=Quaternion.Inverse(rootQ)*desiredQ
    motion.localPosition=Quaternion.Inverse(rootQ)*(desiredP-rootP)
    motion.rotation=rig.rotation*rootQ*motion.localRotation
    motion.position=rig.TransformPoint(rootP+rootQ*motion.localPosition)
end
basisQ=AA(35,2,1,3)
targets={weapon={GetComponent=function(_) return weapon end},
    motion={transform=motion},rig={transform=rig},
    rest={transform={localPosition=V(0,0,0),localRotation=I}},
    basis={transform={localRotation=basisQ,localScale={x=2}}}}
''')
lua.execute((pathlib.Path(__file__).parent / "RFCameraMotion.txt").read_text(encoding="utf-8"))
lua.execute(r'''
local s=RFCameraMotion; s.targets=targets; s:Awake()
-- Root bind A=90degX; camera bind in rig is identity, parent-local rest inverse(A).
local A=AA(90,1,0,0)
local wrongLocalRest=Quaternion.Inverse(A)
pose(I,V(0,0,0),I,V(0,0,0))
assert(Quaternion.Angle(motion.localRotation*Quaternion.Inverse(wrongLocalRest),I)>89)
s:LateUpdate(); nearV(PlayerCamera.fpCameraLocalPosition,baseP); nearQ(PlayerCamera.fpCameraLocalRotation,baseQ)
local D=AA(17,1,2,3)*AA(-11,0,1,0)
local p=V(0.01,-0.02,0.03)
for i=1,100 do
    -- Moving parent/rig must cancel, not leak into camera delta.
    rig.rotation=AA(i,1,3,2)
    pose(AA(i*3,2,1,0),V(0.2,0.3,-0.4),D,p)
    s:Update(); s:LateUpdate()
    nearV(PlayerCamera.fpCameraLocalPosition,baseP+basisQ*(p*2))
    nearQ(PlayerCamera.fpCameraLocalRotation,baseQ*basisQ*D*Quaternion.Inverse(basisQ))
end
s:OnDisable(); nearV(PlayerCamera.fpCameraLocalPosition,baseP); nearQ(PlayerCamera.fpCameraLocalRotation,baseQ)
s:LateUpdate(); Player.actor.activeWeapon={}; s:LateUpdate()
nearV(PlayerCamera.fpCameraLocalPosition,baseP); nearQ(PlayerCamera.fpCameraLocalRotation,baseQ)
Player.actor.activeWeapon=targets.weapon.GetComponent(Weapon); s:LateUpdate()
local otherP=V(42,43,44); local otherQ=AA(43,3,1,2)
PlayerCamera.fpCameraLocalPosition=otherP; PlayerCamera.fpCameraLocalRotation=otherQ
s:OnDestroy(); nearV(PlayerCamera.fpCameraLocalPosition,otherP); nearQ(PlayerCamera.fpCameraLocalRotation,otherQ)
for _,field in ipairs({"isDead","isSeated"}) do
    Player.actor[field]=true; s:LateUpdate(); nearV(PlayerCamera.fpCameraLocalPosition,otherP); Player.actor[field]=false
end
targets.weapon.GetComponent(Weapon).animator.isActiveAndEnabled=false
s:LateUpdate(); nearV(PlayerCamera.fpCameraLocalPosition,otherP)
targets.weapon.GetComponent(Weapon).animator.isActiveAndEnabled=true
PlayerCamera.isUsingFPCamera=false; s:LateUpdate(); nearV(PlayerCamera.fpCameraLocalPosition,otherP)
PlayerCamera.isUsingFPCamera=true
PlayerCamera.isUsingOverrideCamera=true; s:LateUpdate(); nearV(PlayerCamera.fpCameraLocalPosition,otherP)
PlayerCamera.isUsingOverrideCamera=false
motion.gameObject.activeInHierarchy=false; s:LateUpdate(); nearV(PlayerCamera.fpCameraLocalPosition,otherP)
motion.gameObject.activeInHierarchy=true
pose(I,V(0,0,0),I,V(0,0,0)); s:LateUpdate()
nearV(PlayerCamera.fpCameraLocalPosition,otherP); nearQ(PlayerCamera.fpCameraLocalRotation,otherQ)
s:OnDisable()
-- Imported armature-relative rest can itself be nonidentity/nonzero.
targets.rest.transform.localRotation=AA(31,3,2,1)
targets.rest.transform.localPosition=V(0.2,-0.1,0.3)
pose(AA(71,1,2,0),V(-0.1,0.4,0.2),targets.rest.transform.localRotation,targets.rest.transform.localPosition)
s:LateUpdate(); nearV(PlayerCamera.fpCameraLocalPosition,otherP); nearQ(PlayerCamera.fpCameraLocalRotation,otherQ)
s:OnDisable()
''')
print("PASS: actual Lua, 3D noncommuting rotations, nonidentity bind/animated root,")
print("moving rig/root cancellation, 100-frame nonaccumulation and lifecycle gates.")
