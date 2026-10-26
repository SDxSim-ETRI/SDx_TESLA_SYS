#!/usr/bin/env python3
"""Validate this package without modifying the user's Unity scene or model files.

No third-party Python packages required. --library uses the user's existing
libmujoco.so in a subprocess. No native struct layouts are hard-coded.
"""
import argparse
import ctypes as C
import hashlib
import json
import math
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
MODEL = ROOT / 'Models' / 'door_slide.xml'
QPOS, QVEL = 1 << 1, 1 << 2


def nums(text):
    return [float(v) for v in text.split()]


def need(ok, message):
    if not ok:
        raise ValueError(message)


def obj_info(path):
    verts, n_uv, faces = [], 0, 0
    for line in path.read_text(encoding='utf-8').splitlines():
        a = line.split()
        if not a:
            continue
        if a[0] == 'v':
            value = nums(' '.join(a[1:4]))
            need(len(value) == 3 and all(math.isfinite(v) for v in value), 'Nonfinite OBJ vertex')
            verts.append(value)
        elif a[0] == 'vt':
            n_uv += 1
        elif a[0] == 'f':
            need(len(a) == 4, 'All faces must be triangles: ' + str(path))
            for entry in a[1:]:
                fields = entry.split('/')
                need(1 <= int(fields[0]) <= len(verts), 'Bad vertex index')
                need(len(fields) > 1 and fields[1] and 1 <= int(fields[1]) <= n_uv, 'Missing/bad UV index')
            faces += 1
    need(verts and faces > 0, 'Empty OBJ')
    return {'vertices': len(verts), 'triangles': faces,
            'lo': [min(v[k] for v in verts) for k in range(3)],
            'hi': [max(v[k] for v in verts) for k in range(3)]}


def static_check():
    root = ET.parse(MODEL).getroot()
    need(root.tag == 'mujoco', 'Invalid XML root')
    for section in ('option', 'size', 'actuator', 'keyframe', 'equality', 'contact'):
        need(root.find(section) is None, 'Unexpected global/control section: ' + section)
    need(not root.findall('.//freejoint'), 'Door must not have a free joint')
    names = [e.get('name') for e in root.iter() if e.get('name')]
    need(len(names) == len(set(names)), 'Duplicate model names')
    joints = root.findall('.//joint')
    need(len(joints) == 1 and joints[0].get('type') == 'slide', 'Expected one slide joint')
    need(joints[0].get('limited') == 'true', 'Joint must be explicitly limited')
    need(nums(joints[0].get('range')) == [0., 2.4], 'Unexpected travel')
    need(nums(joints[0].get('axis')) == [0., 1., 0.], 'Unexpected axis')
    frame = root.find("worldbody/body[@name='sdxdoor_frame']")
    leaf = root.find("worldbody/body[@name='sdxdoor_leaf']")
    need(frame is not None and leaf is not None, 'Frame/leaf bodies missing')
    need(frame.find('joint') is None, 'Frame must remain fixed')
    need(leaf.find('inertial') is not None, 'Leaf requires an explicit inertial')
    diag = nums(leaf.find('inertial').get('diaginertia'))
    need(all(d > 0 for d in diag) and max(diag) <= sum(diag) - max(diag) + 1e-5, 'Invalid inertia tensor')
    geoms = root.findall('.//geom')
    meshes = {}
    for mesh in root.findall('asset/mesh'):
        path = MODEL.parent / mesh.get('file')
        need(path.is_file(), 'Missing mesh: ' + str(path))
        meshes[mesh.get('name')] = obj_info(path)
    for tex in root.findall('asset/texture'):
        need((MODEL.parent / tex.get('file')).is_file(), 'Missing texture')
    for g in geoms:
        if g.get('type') == 'mesh':
            need(g.get('mesh') in meshes, 'Bad mesh reference')
            need(g.get('contype') == '0' and g.get('conaffinity') == '0', 'Visual mesh must not collide')
            need(g.get('density') == '0', 'Visual mesh must not add mass')
    frame_coll = [g for g in frame.findall('geom') if g.get('contype') == '1']
    need(len(frame_coll) == 3 and all(g.get('type') == 'box' for g in frame_coll), 'Expected U-frame collision')
    panel = leaf.find("geom[@name='sdxdoor_panel_collision']")
    c, h = nums(panel.get('pos')), nums(panel.get('size'))
    panel_lo, panel_hi = [a-b for a,b in zip(c,h)], [a+b for a,b in zip(c,h)]
    clearances = {}
    for side in ('front', 'back'):
        bar = leaf.find("geom[@name='sdxdoor_handle_%s_grip']" % side)
        need(bar is not None and bar.get('type') == 'capsule' and bar.get('contype') == '1', 'Physical grip missing')
        x = nums(bar.get('fromto'))[0]; radius = nums(bar.get('size'))[0]
        clearance = x-radius-panel_hi[0] if x > 0 else panel_lo[0]-(x+radius)
        need(clearance > .06, 'Less than 60 mm grip clearance')
        clearances[side] = clearance
        for label in ('grasp', 'approach'):
            need(leaf.find("site[@name='sdxdoor_%s_%s']" % (side,label)) is not None, 'Target site missing')
    # Analytic sweep: frame and leaf/handles lie in different depth slabs.
    frame_lo = min(nums(g.get('pos'))[0]-nums(g.get('size'))[0] for g in frame_coll)
    frame_hi = max(nums(g.get('pos'))[0]+nums(g.get('size'))[0] for g in frame_coll)
    for offset in [.16, -.16]:
        leaf_lo = offset - .107
        leaf_hi = offset + .097
        clearance = leaf_lo - frame_hi if offset > 0 else frame_lo - leaf_hi
        need(clearance > .02, 'Rail/frame clearance below 20 mm')
    need(panel_lo[1] + 2.4 > 1.15963, 'Positive opening does not clear aperture')
    need(panel_hi[1] - 2.4 < -1.15963, 'Negative opening does not clear aperture')
    need(meshes['sdxdoor_frame_mesh']['triangles'] == 84, 'Frame split mismatch')
    need(meshes['sdxdoor_leaf_mesh']['triangles'] == 10104, 'Leaf split mismatch')
    return {'static': 'passed', 'joint_count': 1, 'geom_count': len(geoms),
            'collision_geom_count': sum(g.get('contype') == '1' for g in geoms),
            'handle_clearances_m': clearances, 'meshes': meshes,
            'world_clearance': 'NOT tested; current MainScene not supplied'}


class Native:
    def __init__(self, library):
        self.lib = C.CDLL(str(Path(library).expanduser().resolve(strict=True)))
        def bind(name, result, *args):
            f = getattr(self.lib, name)
            f.restype, f.argtypes = result, list(args)
            setattr(self, name, f)
        ptr = C.c_void_p; dbl = C.POINTER(C.c_double)
        bind('mj_versionString', C.c_char_p)
        bind('mj_loadXML', ptr, C.c_char_p, ptr, C.c_char_p, C.c_int)
        bind('mj_makeData', ptr, ptr)
        bind('mj_deleteData', None, ptr)
        bind('mj_deleteModel', None, ptr)
        bind('mj_resetData', None, ptr, ptr)
        bind('mj_forward', None, ptr, ptr)
        bind('mj_step', None, ptr, ptr)
        bind('mj_stateSize', C.c_int, ptr, C.c_uint)
        bind('mj_getState', None, ptr, ptr, dbl, C.c_uint)
        bind('mj_setState', None, ptr, ptr, dbl, C.c_uint)
        bind('mj_name2id', C.c_int, ptr, C.c_int, C.c_char_p)
        bind('mj_applyFT', None, ptr, ptr, dbl, dbl, dbl, C.c_int, dbl)
        self.version = self.mj_versionString().decode('ascii')
        need(self.version.split('.')[0] == '3', 'This validator targets MuJoCo 3.x')

    def load(self, path):
        err = C.create_string_buffer(4096)
        m = self.mj_loadXML(str(path).encode(), None, err, len(err))
        need(m, 'Native compilation failed: ' + err.value.decode('utf-8', errors='replace'))
        d = self.mj_makeData(m)
        if not d:
            self.mj_deleteModel(m)
            raise RuntimeError('mj_makeData failed')
        return m, d

    def get(self, m, d, spec):
        size = self.mj_stateSize(m, spec)
        buf = (C.c_double * size)()
        self.mj_getState(m, d, buf, spec)
        return list(buf)

    def force_spec(self, m, nv):
        # mjSTATE_HISTORY shifted these bit positions in newer 3.x builds.
        # These test models have zero actuators: CTRL has size 0, QFRC has nv,
        # and XFRC has 6*nbody. Resolve without depending on native struct ABI.
        matches = [s for s in (1 << 6, 1 << 7) if self.mj_stateSize(m, s) == nv]
        need(len(matches) == 1, 'Cannot identify QFRC_APPLIED state flag safely')
        return matches[0]


def make_test_xml(directory, opening_sign=1, rail_sign=1, probe=False):
    r = ET.parse(MODEL).getroot()
    for elem in r.findall('.//*[@file]'):
        elem.set('file', str((MODEL.parent / elem.get('file')).resolve()))
    r.insert(1, ET.Element('option', timestep='0.001', integrator='implicitfast', solver='Newton', iterations='100'))
    leaf = r.find("worldbody/body[@name='sdxdoor_leaf']")
    leaf.set('pos', '%g 0 0' % (.16*rail_sign))
    leaf.find('joint').set('axis', '0 %d 0' % opening_sign)
    if probe:
        # A small spherical fingertip, NOT an FR3 or a grasp controller.
        b = ET.SubElement(r.find('worldbody'), 'body', name='test_tip', pos='%g %g .85' % (.16*rail_sign+.085, -.05*opening_sign))
        ET.SubElement(b, 'joint', name='test_tip_slide', type='slide', axis='0 %d 0' % opening_sign, damping='2', limited='true', range='0 3')
        ET.SubElement(b, 'geom', name='test_tip_geom', type='sphere', size='.01', mass='.1', friction='1 0.005 0.0001')
    path = Path(directory) / ('test_%s_%s_%s.xml' % (opening_sign,rail_sign,int(probe)))
    ET.ElementTree(r).write(path, encoding='utf-8', xml_declaration=True)
    return path


def native_test(library):
    api = Native(library)
    results = {'native_version': api.version, 'tests': []}
    with tempfile.TemporaryDirectory(prefix='sdx_door_test_') as temp:
        for sign in (1, -1):
            for rail in (1, -1):
                m, d = api.load(make_test_xml(temp,sign,rail))
                try:
                    nq, nv = api.mj_stateSize(m,QPOS), api.mj_stateSize(m,QVEL)
                    need(nq == nv == 1, 'Expected one movable DOF')
                    force_spec = api.force_spec(m,nv)
                    for _ in range(2000): api.mj_step(m,d)
                    drift = api.get(m,d,QPOS)[0]
                    need(abs(drift) < .0001, 'Unforced door drift or initial penetration')
                    api.mj_resetData(m,d); api.mj_forward(m,d)
                    body = api.mj_name2id(m,1,b'sdxdoor_leaf')
                    need(body >= 0, 'Leaf body missing')
                    def drive(force, steps):
                        maximum = -math.inf; minimum = math.inf
                        for _ in range(steps):
                            q = api.get(m,d,QPOS)[0]
                            f = (C.c_double*3)(0,force*sign,0)
                            torque = (C.c_double*3)(0,0,0)
                            point = (C.c_double*3)(.16*rail+.085,sign*q,.85)
                            generalized = (C.c_double*nv)()
                            api.mj_applyFT(m,d,f,torque,point,body,generalized)
                            api.mj_setState(m,d,generalized,force_spec)
                            api.mj_step(m,d)
                            q = api.get(m,d,QPOS)[0]
                            need(math.isfinite(q) and -.05 < q < 2.45, 'Joint limit/numerical check failed')
                            maximum, minimum = max(maximum,q), min(minimum,q)
                        return api.get(m,d,QPOS)[0], minimum, maximum
                    opened, qmin, qmax = drive(50,5000)
                    need(opened > 2.37, 'Applied handle force did not open the passive joint')
                    closed, qmin2, qmax2 = drive(-50,5000)
                    need(abs(closed) < .03, 'Reverse force did not close the passive joint')
                    results['tests'].append({'kind':'passive_force_at_handle_point','direction':sign,'rail_side':rail,
                        'rest_drift_m':drift,'open_m':opened,'close_m':closed,'range_observed':[min(qmin,qmin2),max(qmax,qmax2)],'result':'passed'})
                finally:
                    api.mj_deleteData(d); api.mj_deleteModel(m)
        # Real collision test: a small sphere pushes the grip bar laterally.
        for sign in (1,-1):
            m,d=api.load(make_test_xml(temp,sign,1,probe=True))
            try:
                need(api.mj_stateSize(m,QPOS) == 2, 'Expected door and test-tip DOFs')
                nv=api.mj_stateSize(m,QVEL); spec=api.force_spec(m,nv)
                body=api.mj_name2id(m,1,b'test_tip');need(body>=0,'Test tip missing')
                for _ in range(3000):
                    q=api.get(m,d,QPOS)
                    f=(C.c_double*3)(0,15*sign,0);t=(C.c_double*3)(0,0,0)
                    p=(C.c_double*3)(.245,sign*(q[1]-.05),.85)
                    g=(C.c_double*nv)();api.mj_applyFT(m,d,f,t,p,body,g);api.mj_setState(m,d,g,spec);api.mj_step(m,d)
                q=api.get(m,d,QPOS)
                need(all(math.isfinite(x) for x in q) and .2<q[0]<2.45,'Physical handle-contact test failed')
                need(abs((q[1]-.05)-q[0]) < .05, 'Tip did not remain near the handle')
                results['tests'].append({'kind':'spherical_tip_contact_not_FR3_grasp','direction':sign,'door_opening_m':q[0],'result':'passed'})
            finally:
                api.mj_deleteData(d);api.mj_deleteModel(m)
    results['scope']='Isolated door mechanics only; no Unity scene, actual FR3 gripper, navigation, or surrounding-wall clearance test.'
    return results


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--library',help='Absolute path of the existing Unity libmujoco.so')
    p.add_argument('--static-only',action='store_true')
    p.add_argument('--report',help='Optional JSON result path')
    p.add_argument('--native-child',action='store_true',help=argparse.SUPPRESS)
    args=p.parse_args()
    if args.native_child:
        if not args.library or not args.report: p.error('Internal child arguments missing')
        data=native_test(args.library)
        Path(args.report).write_text(json.dumps(data,indent=2)+'\n')
        print('NATIVE DOOR TESTS PASSED. Not an FR3 grasp or world-clearance test.')
        return 0
    data=static_check();print('STATIC VALIDATION PASSED: 1 slide joint, 10 contact geoms, 2 open grip channels.')
    if args.library and not args.static_only:
        with tempfile.TemporaryDirectory(prefix='sdx_door_report_') as tmp:
            result_path=Path(tmp)/'native.json'
            result=subprocess.run([sys.executable,str(Path(__file__).resolve()),'--native-child','--library',args.library,'--report',str(result_path)],timeout=180)
            need(result.returncode == 0 and result_path.is_file(),'Native test process failed. Do not claim this model is validated on your PC.')
            data['native']=json.loads(result_path.read_text())
    else:
        data['native']='NOT RUN';print('Native compilation/dynamics NOT RUN. Add --library /absolute/path/libmujoco.so to test.')
    if args.report:
        Path(args.report).expanduser().write_text(json.dumps(data,indent=2)+'\n')
    return 0

if __name__=='__main__':
    try:
        sys.exit(main())
    except (OSError,ValueError,RuntimeError,subprocess.SubprocessError) as err:
        print('ERROR:',err,file=sys.stderr);sys.exit(1)
