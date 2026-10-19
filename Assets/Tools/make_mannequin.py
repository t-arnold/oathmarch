# Oathmarch stand-in mannequin. Run: blender -b -P make_mannequin.py -- out.fbx
# Generates a skinned humanoid (Unity-humanoid bone names) + Idle/Walk/Run/Attack/Death clips.
# Original work, released CC0.
import bpy, sys, math
from mathutils import Matrix, Vector

out = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = 30

# ---------------------------------------------------------------- skeleton
B = {  # name: (head, tail, parent)
 "Hips": ((0,0,0.95),(0,0,1.05),None),
 "Spine": ((0,0,1.05),(0,0,1.25),"Hips"),
 "Chest": ((0,0,1.25),(0,0,1.45),"Spine"),
 "Neck": ((0,0,1.45),(0,0,1.55),"Chest"),
 "Head": ((0,0,1.55),(0,0,1.80),"Neck"),
}
for side, sx in (("Left",1),("Right",-1)):
    B[side+"Shoulder"] = ((0.04*sx,0,1.42),(0.17*sx,0,1.42),"Chest")
    B[side+"UpperArm"] = ((0.17*sx,0,1.42),(0.45*sx,0,1.42),side+"Shoulder")
    B[side+"LowerArm"] = ((0.45*sx,0,1.42),(0.70*sx,0,1.42),side+"UpperArm")
    B[side+"Hand"]     = ((0.70*sx,0,1.42),(0.80*sx,0,1.42),side+"LowerArm")
    B[side+"UpperLeg"] = ((0.10*sx,0,0.93),(0.10*sx,0,0.52),"Hips")
    B[side+"LowerLeg"] = ((0.10*sx,0,0.52),(0.10*sx,0,0.09),side+"UpperLeg")
    B[side+"Foot"]     = ((0.10*sx,0,0.09),(0.10*sx,-0.13,0.03),side+"LowerLeg")
    B[side+"Toes"]     = ((0.10*sx,-0.13,0.03),(0.10*sx,-0.21,0.03),side+"Foot")

arm_data = bpy.data.armatures.new("MannequinRig")
arm = bpy.data.objects.new("Mannequin", arm_data)
scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
for name,(h,t,p) in B.items():
    eb = arm_data.edit_bones.new(name); eb.head = h; eb.tail = t
    if p: eb.parent = arm_data.edit_bones[p]; eb.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------------------------------------------------------- skin (rigid-weighted segments)
SEG = {  # bone: (radius_x, radius_y, extend)
 "Hips": (0.17,0.11,0.06), "Spine": (0.15,0.10,0.0), "Chest": (0.19,0.12,0.02), "Neck": (0.05,0.05,0.0),
 "Head": (0.10,0.11,0.0),
}
for s in ("Left","Right"):
    SEG.update({s+"Shoulder":(0.06,0.06,0.0), s+"UpperArm":(0.055,0.055,0.0), s+"LowerArm":(0.045,0.045,0.0), s+"Hand":(0.04,0.025,0.02),
                s+"UpperLeg":(0.08,0.08,0.0), s+"LowerLeg":(0.06,0.06,0.0), s+"Foot":(0.05,0.045,0.0), s+"Toes":(0.05,0.035,0.0)})
mat = bpy.data.materials.new("MannequinBody"); mat.diffuse_color = (0.75,0.72,0.68,1)
parts = []
for name,(rx,ry,ext) in SEG.items():
    h,t,_ = B[name]; h=Vector(h); t=Vector(t); d=(t-h); L=d.length
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=1, depth=1)
    o = bpy.context.active_object
    o.scale = (rx, ry, L/2 + ext)
    o.rotation_mode='QUATERNION'; o.rotation_quaternion = Vector((0,0,1)).rotation_difference(d.normalized())
    o.location = (h+t)/2
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if name == "Head":
        bpy.ops.object.shade_smooth()
    vg = o.vertex_groups.new(name=name); vg.add(list(range(len(o.data.vertices))),1.0,'REPLACE')
    o.data.materials.append(mat)
    parts.append(o)
bpy.ops.object.select_all(action='DESELECT')
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
body = bpy.context.active_object; body.name = "Body"
body.parent = arm
mod = body.modifiers.new("Armature",'ARMATURE'); mod.object = arm

# ---------------------------------------------------------------- animation helpers
def wrot(pb, axis, deg):
    """rotation about a world axis expressed in the bone's rest frame"""
    M = pb.bone.matrix_local.to_3x3()
    R = Matrix.Rotation(math.radians(deg),3,axis)
    return (M.inverted() @ R @ M).to_quaternion()

def pose(frame, rots, hips_loc=(0,0,0)):
    """rots: {bone: [(axis,deg),...]}"""
    for pb in arm.pose.bones:
        q = pb.bone.matrix_local.to_quaternion().inverted() @ pb.bone.matrix_local.to_quaternion() # identity
        for ax,deg in rots.get(pb.name,[]):
            q = wrot(pb,ax,deg) @ q
        pb.rotation_mode='QUATERNION'; pb.rotation_quaternion = q
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        pb.location = Vector(hips_loc) if pb.name=="Hips" else Vector((0,0,0))
        pb.keyframe_insert("location", frame=frame)

X, Y, Z = 'X','Y','Z'
ARMS_DOWN = {"LeftUpperArm":[(Y,72)], "RightUpperArm":[(Y,-72)], "LeftLowerArm":[(Y,8)], "RightLowerArm":[(Y,-8)]}

def merge(*ds):
    r = {}
    for d in ds:
        for k,v in d.items(): r.setdefault(k,[]).extend(v)
    return r

def action(name, frames):
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    for f, rots, loc in frames: pose(f, rots, loc)
    return act

def gait(amp_leg, amp_knee, amp_arm, lean, bob, length):
    fr=[]
    for i in range(5):
        ph = i/4*2*math.pi; s = math.sin(ph); f = round(i/4*length)+1
        knL = amp_knee*max(0,math.sin(ph+math.pi/2)) + 5
        knR = amp_knee*max(0,math.sin(ph-math.pi/2)) + 5
        r = merge(ARMS_DOWN, {
            "LeftUpperLeg":[(X,-amp_leg*s)], "RightUpperLeg":[(X,amp_leg*s)],
            "LeftLowerLeg":[(X,knL)], "RightLowerLeg":[(X,knR)],
            "LeftUpperArm":[(X,amp_arm*s)], "RightUpperArm":[(X,-amp_arm*s)],
            "LeftLowerArm":[(X,-amp_arm*0.6)], "RightLowerArm":[(X,-amp_arm*0.6)],
            "Spine":[(X,lean)], "Chest":[(Z,6*s)]})
        fr.append((f, r, (0, -bob*abs(math.cos(ph)), 0)))
    return fr

action("Idle", [(1, merge(ARMS_DOWN,{"Chest":[(X,0)]}),(0,0,0)),
                (31, merge(ARMS_DOWN,{"Chest":[(X,3)],"Head":[(X,-2)]}),(0,-0.01,0)),
                (61, merge(ARMS_DOWN,{"Chest":[(X,0)]}),(0,0,0))])
action("Walk", gait(28,35,22,4,0.03,30))
action("Run",  gait(45,70,45,14,0.06,20))
action("Attack", [
 (1,  ARMS_DOWN,(0,0,0)),
 (9,  merge(ARMS_DOWN,{"RightUpperArm":[(X,-150)],"RightLowerArm":[(X,-40)],"Chest":[(Z,-25),(X,-5)],"LeftUpperLeg":[(X,-15)]}),(0,0,0)),
 (14, merge(ARMS_DOWN,{"RightUpperArm":[(X,-40)],"RightLowerArm":[(X,-5)],"Chest":[(Z,20),(X,15)],"LeftUpperLeg":[(X,-25)],"LeftLowerLeg":[(X,20)]}),(0,-0.05,0)),
 (24, ARMS_DOWN,(0,0,0))])
action("Death", [
 (1,  ARMS_DOWN,(0,0,0)),
 (12, merge(ARMS_DOWN,{"Spine":[(X,-30)],"LeftUpperLeg":[(X,-30)],"LeftLowerLeg":[(X,60)],"RightLowerLeg":[(X,50)]}),(0,-0.35,0)),
 (24, merge(ARMS_DOWN,{"Hips":[(X,-85)],"Head":[(X,-10)],"LeftUpperArm":[(Y,-40)],"RightUpperArm":[(Y,40)]}),(0,-0.8,0)),
 (36, merge(ARMS_DOWN,{"Hips":[(X,-88)],"Head":[(X,-10)],"LeftUpperArm":[(Y,-40)],"RightUpperArm":[(Y,40)]}),(0,-0.82,0))])
arm.animation_data.action = None
for pb in arm.pose.bones: pb.rotation_quaternion=(1,0,0,0); pb.location=(0,0,0)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE','MESH'},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
    primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL', mesh_smooth_type='FACE')
print("EXPORTED", out)
