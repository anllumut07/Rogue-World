#!/usr/bin/env python3
import json

with open('../Data/map/cities_runtime.json', 'r', encoding='utf-8') as f:
    data = json.load(f)
cities = data['Cities']

for name in ['Cairo', 'Tokyo', 'New York', 'Ankara', 'Berlin', 'Moscow', 'Cape Town']:
    city = next((c for c in cities if c['Name'] == name), None)
    if city:
        print('{}: X={:.1f} Y={:.1f} Country={} Type={} Cap={}'.format(
            name, city['X'], city['Y'], city['Country'], city['Type'], city['Cap']))
    else:
        print('{}: NOT FOUND'.format(name))