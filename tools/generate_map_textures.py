#!/usr/bin/env python3
"""
Generate high-quality procedural map textures for WorldMapV3
Creates paper texture, terrain texture, and other map assets
"""

import os
import sys
import math
import random
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFilter, ImageEnhance, ImageOps
    HAS_PIL = True
except ImportError:
    HAS_PIL = False
    print("PIL/Pillow not installed. Installing...")
    import subprocess
    subprocess.check_call([sys.executable, "-m", "pip", "install", "pillow"])
    from PIL import Image, ImageDraw, ImageFilter, ImageEnhance, ImageOps
    HAS_PIL = True

# Output directory
OUTPUT_DIR = Path(__file__).parent.parent / "Assets" / "map_textures"
PAPER_DIR = OUTPUT_DIR / "paper"
TERRAIN_DIR = OUTPUT_DIR / "terrain"
OCEAN_DIR = OUTPUT_DIR / "ocean"
CITIES_DIR = OUTPUT_DIR / "cities"

for d in [PAPER_DIR, TERRAIN_DIR, OCEAN_DIR, CITIES_DIR]:
    d.mkdir(parents=True, exist_ok=True)

def generate_paper_texture(width=2048, height=2048, seed=42):
    """Generate a high-quality aged paper texture"""
    random.seed(seed)

    # Base paper color (warm off-white)
    base_color = (245, 240, 230)
    img = Image.new('RGBA', (width, height), base_color + (255,))
    draw = ImageDraw.Draw(img)

    # Add paper fibers/noise
    for _ in range(width * height // 200):
        x = random.randint(0, width - 1)
        y = random.randint(0, height - 1)
        r = random.randint(-10, 10)
        g = random.randint(-10, 10)
        b = random.randint(-10, 10)
        a = random.randint(10, 40)
        draw.point((x, y), fill=(r + base_color[0], g + base_color[1], b + base_color[2], a))

    # Add subtle vertical/horizontal grain
    for y in range(0, height, 2):
        alpha = random.randint(0, 15)
        for x in range(0, width, 3):
            if random.random() < 0.3:
                draw.point((x, y), fill=(255, 255, 255, alpha))

    # Add coffee stains / aging spots
    for _ in range(random.randint(15, 30)):
        cx = random.randint(50, width - 50)
        cy = random.randint(50, height - 50)
        radius = random.randint(30, 150)

        # Create a radial gradient for the stain
        stain = Image.new('RGBA', (radius * 2, radius * 2), (0, 0, 0, 0))
        stain_draw = ImageDraw.Draw(stain)

        stain_color = (
            random.randint(120, 160),  # Sepia brown
            random.randint(80, 110),
            random.randint(40, 70),
        )

        for r in range(radius, 0, -1):
            alpha = int(30 * (1 - r / radius) ** 2)
            stain_draw.ellipse(
                [radius - r, radius - r, radius + r, radius + r],
                fill=stain_color + (alpha,)
            )

        # Rotate and paste
        stain = stain.rotate(random.uniform(0, 360), expand=True)
        px = cx - stain.width // 2
        py = cy - stain.height // 2
        img.paste(stain, (px, py), stain)

    # Add fold lines
    for _ in range(random.randint(2, 5)):
        if random.random() < 0.5:
            # Vertical fold
            x = random.randint(width // 4, 3 * width // 4)
            fold_img = Image.new('RGBA', (3, height), (0, 0, 0, 0))
            fold_draw = ImageDraw.Draw(fold_img)
            for y in range(height):
                alpha = int(20 * math.sin(y / height * math.pi * 3) * (1 - abs(y - height/2) / (height/2)))
                if alpha > 0:
                    fold_draw.line([(1, y), (2, y)], fill=(60, 50, 40, alpha))
            img.paste(fold_img, (x - 1, 0), fold_img)
        else:
            # Horizontal fold
            y = random.randint(height // 4, 3 * height // 4)
            fold_img = Image.new('RGBA', (width, 3), (0, 0, 0, 0))
            fold_draw = ImageDraw.Draw(fold_img)
            for x in range(width):
                alpha = int(20 * math.sin(x / width * math.pi * 3) * (1 - abs(x - width/2) / (width/2)))
                if alpha > 0:
                    fold_draw.line([(x, 1), (x, 2)], fill=(60, 50, 40, alpha))
            img.paste(fold_img, (0, y - 1), fold_img)

    # Subtle vignette
    vignette = Image.new('RGBA', (width, height), (0, 0, 0, 0))
    vignette_draw = ImageDraw.Draw(vignette)
    for r in range(min(width, height) // 2, 0, -10):
        alpha = int(15 * (1 - r / (min(width, height) // 2)) ** 1.5)
        vignette_draw.ellipse(
            [width//2 - r, height//2 - r, width//2 + r, height//2 + r],
            fill=(0, 0, 0, alpha)
        )
    img = Image.alpha_composite(img, vignette)

    # Enhance contrast slightly
    enhancer = ImageEnhance.Contrast(img)
    img = enhancer.enhance(1.05)

    # Save
    output_path = PAPER_DIR / "paper_texture.png"
    img.save(output_path, "PNG")
    print(f"Generated paper texture: {output_path} ({width}x{height})")
    return output_path

def generate_terrain_texture(width=4096, height=2048):
    """Generate a HOI4-style terrain color ramp texture (equirectangular projection)"""
    img = Image.new('RGBA', (width, height), (0, 0, 0, 0))
    pixels = img.load()

    # Terrain color stops (elevation-based)
    # Based on HOI4 map style: ocean -> lowland -> hills -> mountains -> snow
    terrain_stops = [
        (-1.0, (8, 32, 48, 255)),      # Deep ocean
        (-0.5, (14, 74, 90, 255)),     # Shallow ocean
        (0.0, (26, 92, 46, 255)),      # Coastline / sea level
        (0.05, (46, 125, 50, 255)),    # Lowlands
        (0.15, (76, 175, 80, 255)),    # Plains
        (0.25, (104, 159, 56, 255)),   # Grassland
        (0.35, (139, 195, 74, 255)),   # Light forest
        (0.45, (129, 199, 132, 255)),  # Forest
        (0.55, (171, 172, 22, 255)),   # Hills / dry
        (0.65, (191, 141, 15, 255)),   # Dry hills
        (0.72, (183, 54, 12, 255)),    # Mountains
        (0.8, (216, 67, 21, 255)),     # High mountains
        (0.88, (230, 81, 0, 255)),     # Snow line
        (0.95, (255, 248, 225, 255)),  # Snow peaks
        (1.0, (255, 255, 250, 255)),   # Permanent snow
    ]

    # Generate elevation map using fractal noise
    def noise2d(x, y, scale=1.0, octaves=6, persistence=0.5, lacunarity=2.0):
        """Simple 2D fractal noise"""
        value = 0.0
        amplitude = 1.0
        frequency = scale
        max_value = 0.0

        for _ in range(octaves):
            # Simple pseudo-random noise
            nx = (x * frequency) % width
            ny = (y * frequency) % height

            # Hash-based noise
            h = int(nx * 12345 + ny * 67890 + 13579) & 0x7fffffff
            h = (h ^ (h >> 13)) * 0x5bd1e995
            h = (h ^ (h >> 15)) * 0x5bd1e995
            h = h ^ (h >> 13)
            n = (h & 0xffff) / 65535.0 * 2.0 - 1.0

            value += n * amplitude
            max_value += amplitude
            amplitude *= persistence
            frequency *= lacunarity

        return value / max_value if max_value > 0 else 0

    # Generate terrain
    for y in range(height):
        lat = (y / height) * 180 - 90  # -90 to 90
        lat_factor = math.cos(math.radians(lat))  # Compensate for equirectangular distortion

        for x in range(width):
            lon = (x / width) * 360 - 180  # -180 to 180

            # Base elevation from noise
            base_elev = noise2d(x, y, scale=0.01, octaves=5)

            # Add continental-scale features
            continental = noise2d(x * 0.5, y * 0.5, scale=0.002, octaves=3) * 0.5

            # Add mountain ranges
            mountains = noise2d(x * 2, y * 2, scale=0.02, octaves=4) * 0.3

            # Combine
            elevation = base_elev * 0.4 + continental + mountains

            # Latitude modulation (polar regions higher)
            lat_mod = abs(lat) / 90.0 * 0.2
            elevation += lat_mod

            # Ocean mask (rough continental shelves)
            ocean_mask = noise2d(x * 1.5, y * 1.5, scale=0.005, octaves=2)
            if ocean_mask < -0.3 and elevation < 0.1:
                elevation = -0.5 + ocean_mask * 0.2

            # Clamp
            elevation = max(-1.0, min(1.0, elevation))

            # Interpolate color from terrain stops
            color = interpolate_terrain_color(elevation, terrain_stops)
            pixels[x, y] = color

    # Apply slight blur for smoother transitions
    img = img.filter(ImageFilter.GaussianBlur(radius=0.8))

    # Save
    output_path = TERRAIN_DIR / "terrain_texture.png"
    img.save(output_path, "PNG")
    print(f"Generated terrain texture: {output_path} ({width}x{height})")
    return output_path

def interpolate_terrain_color(elevation, stops):
    """Interpolate color from terrain stops"""
    for i in range(len(stops) - 1):
        if stops[i][0] <= elevation <= stops[i + 1][0]:
            t = (elevation - stops[i][0]) / (stops[i + 1][0] - stops[i][0])
            c1 = stops[i][1]
            c2 = stops[i + 1][1]
            return tuple(int(c1[j] + (c2[j] - c1[j]) * t) for j in range(4))
    return stops[-1][1]

def generate_ocean_texture(width=1024, height=1024):
    """Generate a beautiful ocean texture with depth variation"""
    img = Image.new('RGBA', (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # Base ocean gradient (dark to lighter blue)
    for y in range(height):
        t = y / height
        # Deep ocean at top (north pole), shallower at equator, deep at south
        if t < 0.5:
            # Northern hemisphere
            lat_factor = math.sin(t * math.pi)
        else:
            # Southern hemisphere
            lat_factor = math.sin((1 - t) * math.pi)

        # Base color varies with latitude
        r = int(8 + 20 * lat_factor)
        g = int(32 + 50 * lat_factor)
        b = int(48 + 60 * lat_factor)

        # Add subtle noise
        noise = random.randint(-5, 5)
        r = max(0, min(255, r + noise))
        g = max(0, min(255, g + noise))
        b = max(0, min(255, b + noise))

        draw.line([(0, y), (width, y)], fill=(r, g, b, 255))

    # Add caustic-like patterns
    for _ in range(200):
        cx = random.randint(0, width - 1)
        cy = random.randint(0, height - 1)
        radius = random.randint(20, 100)

        for r in range(radius, 0, -5):
            alpha = int(15 * (1 - r / radius))
            color = (
                random.randint(0, 30),
                random.randint(80, 150),
                random.randint(120, 200),
                alpha
            )
            draw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=color)

    # Add subtle wave lines
    for _ in range(50):
        y = random.randint(0, height - 1)
        x1 = 0
        x2 = width
        points = []
        for x in range(0, width, 20):
            points.append((x, y + random.randint(-3, 3)))

        if len(points) > 1:
            draw.line(points, fill=(30, 80, 120, 30), width=1)

    output_path = OCEAN_DIR / "ocean_texture.png"
    img.save(output_path, "PNG")
    print(f"Generated ocean texture: {output_path} ({width}x{height})")
    return output_path

def generate_city_markers():
    """Generate city marker sprites"""
    markers = [
        ("capital", 32, (255, 215, 0, 255)),      # Gold for capitals
        ("major", 24, (255, 255, 255, 230)),      # White for major cities
        ("minor", 16, (200, 200, 200, 200)),      # Gray for minor cities
    ]

    for name, size, color in markers:
        img = Image.new('RGBA', (size * 4, size * 4), (0, 0, 0, 0))
        draw = ImageDraw.Draw(img)

        center = (size * 2, size * 2)

        # Outer glow
        for r in range(size, size // 2, -1):
            alpha = int(100 * (1 - (size - r) / (size // 2)))
            draw.ellipse(
                [center[0] - r, center[1] - r, center[0] + r, center[1] + r],
                fill=color[:3] + (alpha,)
            )

        # Main circle
        draw.ellipse(
            [center[0] - size // 2, center[1] - size // 2,
             center[0] + size // 2, center[1] + size // 2],
            fill=color
        )

        # Inner highlight
        draw.ellipse(
            [center[0] - size // 4, center[1] - size // 4,
             center[0] + size // 4, center[1] + size // 4],
            fill=(255, 255, 255, 100)
        )

        if name == "capital":
            # Add star shape for capitals
            star_points = []
            for i in range(10):
                angle = i * math.pi / 5 - math.pi / 2
                radius = size // 3 if i % 2 == 0 else size // 6
                x = center[0] + radius * math.cos(angle)
                y = center[1] + radius * math.sin(angle)
                star_points.append((x, y))
            draw.polygon(star_points, fill=(255, 255, 255, 255))

        output_path = CITIES_DIR / f"city_marker_{name}.png"
        img.save(output_path, "PNG")
        print(f"Generated city marker: {output_path} ({size*4}x{size*4})")

def generate_border_texture():
    """Generate a nice border line texture"""
    img = Image.new('RGBA', (64, 64), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # Dashed border pattern
    dash_length = 8
    gap_length = 4
    y = 32

    x = 0
    while x < 64:
        x2 = min(x + dash_length, 64)
        draw.line([(x, y), (x2, y)], fill=(14, 20, 27, 255), width=2)
        x += dash_length + gap_length

    output_path = OUTPUT_DIR / "border_texture.png"
    img.save(output_path, "PNG")
    print(f"Generated border texture: {output_path}")

def main():
    print("=" * 60)
    print("Generating HOI4-style Map Textures for WorldMapV3")
    print("=" * 60)

    # Generate all textures
    generate_paper_texture(2048, 2048)
    generate_terrain_texture(4096, 2048)
    generate_ocean_texture(1024, 1024)
    generate_city_markers()
    generate_border_texture()

    print("\n" + "=" * 60)
    print("All textures generated successfully!")
    print(f"Output directory: {OUTPUT_DIR}")
    print("=" * 60)

if __name__ == "__main__":
    main()