#!/usr/bin/env python3
import json

with open('../Data/map/country_borders.json', 'r') as f:
    borders = json.load(f)

# Check Egypt
egy = borders.get('EGY', [])
if egy:
    for poly in egy[:2]:
        xs = [p[0] for p in poly]
        ys = [p[1] for p in poly]
        print('EGY: x=[{:.1f},{:.1f}] y=[{:.1f},{:.1f}] npoints={}'.format(min(xs), max(xs), min(ys), max(ys), len(poly)))

# Check Turkey
tur = borders.get('TUR', [])
if tur:
    for poly in tur[:2]:
        xs = [p[0] for p in poly]
        ys = [p[1] for p in poly]
        print('TUR: x=[{:.1f},{:.1f}] y=[{:.1f},{:.1f}] npoints={}'.format(min(xs), max(xs), min(ys), max(ys), len(poly)))

# Check Cairo coordinates
cairo_x, cairo_y = 1267.4, 453.1
print('Cairo: ({}, {})'.format(cairo_x, cairo_y))

def point_in_polygon(point, polygon):
    x, y = point
    inside = False
    n = len(polygon)
    for i in range(n):
        x1, y1 = polygon[i]
        x2, y2 = polygon[(i + 1) % n]
        if ((y1 > y) != (y2 > y)) and (x < (x2 - x1) * (y - y1) / (y2 - y1) + x1):
            inside = not inside
    return inside

def point_in_multi_polygon(point, polygons):
    for poly in polygons:
        if point_in_polygon(point, poly):
            return True
    return False

cairo_point = (1267.4, 453.1)
print('Cairo in EGY: {}'.format(point_in_multi_polygon(cairo_point, borders.get('EGY', []))))
print('Cairo in TUR: {}'.format(point_in_multi_polygon(cairo_point, borders.get('TUR', []))))
print('Cairo in USA: {}'.format(point_in_multi_polygon(cairo_point, borders.get('USA', []))))

# Check Tokyo
tokyo_x, tokyo_y = 1918.5, 418.0
print('Tokyo: ({}, {})'.format(tokyo_x, tokyo_y))
print('Tokyo in JPN: {}'.format(point_in_multi_polygon((tokyo_x, tokyo_y), borders.get('JPN', []))))
print('Tokyo in RUS: {}'.format(point_in_multi_polygon((tokyo_x, tokyo_y), borders.get('RUS', []))))

# Check New York
nyc_x, nyc_y = 636.0, 386.7
print('NYC: ({}, {})'.format(nyc_x, nyc_y))
print('NYC in USA: {}'.format(point_in_multi_polygon((nyc_x, nyc_y), borders.get('USA', []))))
print('NYC in CAN: {}'.format(point_in_multi_polygon((nyc_x, nyc_y), borders.get('CAN', []))))