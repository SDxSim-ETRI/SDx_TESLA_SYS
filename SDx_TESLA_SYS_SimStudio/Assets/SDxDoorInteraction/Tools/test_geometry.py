#!/usr/bin/env python3
"""Static geometry tests only. They do not substitute for MuJoCo or FR3 tests."""
import unittest, math, json
import xml.etree.ElementTree as ET
from pathlib import Path
from verify_doors import ROOT, MODEL, nums, static_check, obj_info


def interval(g):
    if g.get('type') == 'box':
        c,h=nums(g.get('pos')),nums(g.get('size'))
        return [x-y for x,y in zip(c,h)],[x+y for x,y in zip(c,h)]
    v=nums(g.get('fromto'));rad=nums(g.get('size'))[0]
    return [min(v[k],v[k+3])-rad for k in range(3)],[max(v[k],v[k+3])+rad for k in range(3)]


def distance(point,g):
    if g.get('type')=='box':
        c,h=nums(g.get('pos')),nums(g.get('size'))
        delta=[abs(p-v)-hh for p,v,hh in zip(point,c,h)]
        return math.sqrt(sum(max(d,0)**2 for d in delta))+min(max(delta),0)
    v=nums(g.get('fromto'));a=v[:3];b=v[3:];u=[y-x for x,y in zip(a,b)]
    t=max(0,min(1,sum((p-x)*z for p,x,z in zip(point,a,u))/sum(z*z for z in u)))
    return math.sqrt(sum((p-x-t*z)**2 for p,x,z in zip(point,a,u)))-nums(g.get('size'))[0]


class GeometryTests(unittest.TestCase):
    def setUp(self):
        self.root=ET.parse(MODEL).getroot()
        self.frame=self.root.find("worldbody/body[@name='sdxdoor_frame']")
        self.leaf=self.root.find("worldbody/body[@name='sdxdoor_leaf']")
        self.fc=[g for g in self.frame.findall('geom') if g.get('contype')=='1']
        self.lc=[g for g in self.leaf.findall('geom') if g.get('contype')=='1']
    def test_full_static_validation(self):
        self.assertEqual(static_check()['static'],'passed')
    def test_collision_count(self):
        self.assertEqual((len(self.fc),len(self.lc)),(3,7))
    def test_no_motor_or_artificial_grasp(self):
        for name in ['actuator','equality','contact','option','size']:
            self.assertIsNone(self.root.find(name))
    def test_source_triangle_accounting(self):
        data=json.loads((ROOT/'Docs/model_measurements.json').read_text())
        self.assertEqual(sum(v['triangles'] for v in data['pieces'].values())+data['removed_original_handle_faces'],data['source_faces'])
    def test_valid_positive_box_extents(self):
        for g in self.fc+self.lc:
            self.assertTrue(all(x>0 for x in nums(g.get('size'))))
    def test_sliding_sweep_does_not_hit_own_frame(self):
        for sign in [-1,1]:
            for rail in [-1,1]:
                for step in range(49):
                    q=2.4*step/48
                    for g in self.lc:
                        lo,hi=interval(g);lo[0]+=.16*rail;hi[0]+=.16*rail;lo[1]+=sign*q;hi[1]+=sign*q
                        for f in self.fc:
                            a,b=interval(f)
                            intersect=all(lo[k] < b[k]+.001 and hi[k] > a[k]-.001 for k in range(3))
                            self.assertFalse(intersect, (sign,rail,q,g.get('name'),f.get('name')))
    def test_finger_clearance_in_front_and_back_channels(self):
        panel=self.leaf.find("geom[@name='sdxdoor_panel_collision']");a,b=interval(panel)
        for side in ['front','back']:
            bar=self.leaf.find("geom[@name='sdxdoor_handle_%s_grip']"%side)
            x=nums(bar.get('fromto'))[0];r=nums(bar.get('size'))[0]
            mid=(b[0]+x-r)/2 if x>0 else (a[0]+x+r)/2
            for z in [.80,.85,.90]:
                for y in [-.012,0,.012]:
                    for g in self.lc:
                        self.assertGreater(distance([mid,y,z],g),.009)
    def test_spherical_pad_contact_hits_bar_not_panel(self):
        for side in ['front','back']:
            bar=self.leaf.find("geom[@name='sdxdoor_handle_%s_grip']"%side)
            x=nums(bar.get('fromto'))[0]
            for y in [-.020,.020]:
                self.assertLess(distance([x,y,.85],bar),.009)
                for g in self.lc:
                    if g is not bar:self.assertGreater(distance([x,y,.85],g),.009)
    def test_frame_opening_not_filled(self):
        for y in [-.8,0,.8]:
            for z in [.2,1,1.9]:
                for g in self.fc:self.assertGreater(distance([0,y,z],g),.1)
    def test_full_open_clears_aperture(self):
        panel=self.leaf.find("geom[@name='sdxdoor_panel_collision']");a,b=interval(panel)
        self.assertGreater(a[1]+2.4,1.15963)
        self.assertLess(b[1]-2.4,-1.15963)
    def test_sites_in_same_moving_body(self):
        self.assertEqual(len(self.leaf.findall('site')),4)
        self.assertEqual(len(self.frame.findall('site')),0)
    def test_no_library_or_absolute_home_dependency(self):
        text=MODEL.read_text()
        for bad in ['/home/','/mnt/','libmujoco','<include','<freejoint']:
            self.assertNotIn(bad,text)

if __name__=='__main__':unittest.main(verbosity=2)
