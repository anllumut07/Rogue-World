shader_type canvas_item;
render_mode blend_mix;

uniform sampler2D terrain_texture : source_color;
uniform sampler2D shaded_relief : source_color;
uniform float shaded_relief_strength : hint_range(0, 1) = 0.55;
uniform float terrain_saturation : hint_range(0, 2) = 0.6;
uniform float terrain_contrast : hint_range(0.5, 2.0) = 1.1;
uniform float detail_strength : hint_range(0, 0.15) = 0.06;
uniform float detail_scale : hint_range(100, 2000) = 800.0;

// --- Visual tuning uniforms ---
uniform float terrain_brightness : hint_range(-0.5, 0.5) = 0.0;
uniform float biome_warmth : hint_range(-0.3, 0.3) = 0.0;     // positive = warmer (deserts), negative = cooler (tundra)
uniform float biome_green : hint_range(-0.3, 0.3) = 0.0;       // positive = greener (forests), negative = less green
uniform float elevation_contrast : hint_range(0.0, 1.0) = 0.0; // extra contrast for high elevations

// Simple hash-based noise for procedural detail
float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f); // Smoothstep
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p, int octaves) {
    float value = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 4; i++) {
        value += amplitude * noise(p);
        p *= 2.0;
        amplitude *= 0.5;
    }
    return value;
}

// Approximate elevation from shaded relief (darker = higher)
float estimate_elevation(vec4 relief) {
    // relief.r is 0-1 where darker = higher elevation in SR_HR
    return 1.0 - relief.r;
}

void fragment() {
    vec4 terrain = texture(terrain_texture, UV);
    vec4 relief = texture(shaded_relief, UV);

    // Base elevation for biome modulation
    float elev = estimate_elevation(relief);

    // Multiply blend for shaded relief
    vec3 result = terrain.rgb * (1.0 - shaded_relief_strength) +
                  terrain.rgb * relief.r * shaded_relief_strength;

    // Brightness adjustment (before contrast)
    result += terrain_brightness;

    // Biome color modulation based on elevation and base color
    // High elevation (mountains) -> cooler, less saturated
    // Low elevation warm areas (deserts) -> warmer if biome_warmth > 0
    // Mid elevation green areas (forests) -> greener if biome_green > 0

    float warmth_mask = smoothstep(0.2, 0.5, terrain.r) * (1.0 - smoothstep(0.6, 0.9, terrain.g)); // Desert-like: high R, low G
    float green_mask = smoothstep(0.3, 0.7, terrain.g) * (1.0 - smoothstep(0.5, 0.8, terrain.r));  // Forest-like: high G
    float snow_mask = smoothstep(0.7, 0.9, elev); // High elevation = snow

    // Apply biome shifts
    result.r += biome_warmth * warmth_mask * 0.3;
    result.g += biome_green * green_mask * 0.3;
    result.b -= biome_warmth * warmth_mask * 0.1; // Deserts less blue

    // Snow/ice tint for high elevations
    result.rgb = mix(result.rgb, vec3(1.0, 0.98, 0.95), snow_mask * 0.6);

    // Contrast adjustment (centered)
    result = (result - 0.5) * terrain_contrast + 0.5;

    // Extra elevation contrast for mountains
    if (elevation_contrast > 0.0) {
        float elev_con = 1.0 + elevation_contrast * elev;
        result = (result - 0.5) * elev_con + 0.5;
    }

    // Procedural detail noise (fBm) for high-frequency terrain variation
    vec2 detail_uv = UV * detail_scale;
    float detail = fbm(detail_uv, 4) - 0.5;
    result += detail * detail_strength;

    // Saturation control
    float gray = dot(result, vec3(0.299, 0.587, 0.114));
    result = mix(vec3(gray), result, terrain_saturation);

    // Preserve original alpha
    COLOR = vec4(result, terrain.a);
}