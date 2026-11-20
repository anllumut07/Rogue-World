#!/usr/bin/env python3
"""Check microstate cities"""

import json

with open('../Data/map/cities.json', 'r', encoding='utf-8') as f:
    cities = json.load(f)

# Microstates that might have issues
microstates = ['SGP', 'MDV', 'MCO', 'SMR', 'VCT', 'LCA', 'GRD', 'ATG', 'KNA', 'PLW', 'MHL', 'FSM', 'NRU', 'TLS', 'KIR', 'TON', 'WSM', 'STP', 'SYC', 'CPV', 'SLB', 'VUT', 'WLF', 'PYF', 'GUM', 'MNP', 'ASM', 'VGB', 'AIA', 'MSR', 'SHN', 'TKL', 'NIU', 'COK', 'FLK', 'SPM', 'BVT', 'SGS', 'ATF', 'IOT', 'HMD', 'UMI', 'ATA', 'ABW', 'ALA', 'AND', 'BHR', 'BRB', 'COM', 'CPV', 'CUW', 'DMA', 'FSM', 'GGY', 'GRD', 'IMN', 'JEY', 'KIR', 'KNA', 'LCA', 'LIE', 'LSO', 'MAC', 'MCO', 'MDV', 'MHL', 'MLT', 'MNP', 'NRU', 'PLW', 'SGP', 'SMR', 'STP', 'SXM', 'SYC', 'TON', 'VCT']

print('Cities in microstates:')
for c in cities:
    iso3 = c.get('country_iso3', '').upper()
    if iso3 in microstates:
        name = c['name']
        lat = c['latitude']
        lon = c['longitude']
        cap = c.get('is_admin0_capital', False)
        print(f'  {name:30s} country_iso3={iso3} lat={lat:8.4f} lon={lon:8.4f} capital={cap}')

print()
print('Countries in source data:')
countries = set(c.get('country_iso3', '').upper() for c in cities)
print(f'Total unique countries: {len(countries)}')
print(sorted(countries))