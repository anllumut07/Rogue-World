shader_type canvas_item;
render_mode blend_mix;

uniform sampler2D terrain_texture : source_color;
uniform sampler2D shaded_relief : source_color;
uniform float shaded_relief_strength : hint_range(0, 1) = 0.35;
uniform float terrain_saturation : hint_range(0, 2) = 1.0;
uniform float terrain_contrast : hint_range(0.5, 2.0) = 1.0;

void fragment() {
    vec4 terrain = texture(terrain_texture, UV);
    vec4 relief = texture(shaded_relief, UV);

    // Multiply blend for shaded relief
    vec3 result = terrain.rgb * (1.0 - shaded_relief_strength) +
                  terrain.rgb * relief.r * shaded_relief_strength;

    // Contrast adjustment
    result = (result - 0.5) * terrain_contrast + 0.5;

    // Saturation control
    float gray = dot(result, vec3(0.299, 0.587, 0.114));
    result = mix(vec3(gray), result, terrain_saturation);

    // Preserve original alpha
    COLOR = vec4(result, terrain.a);
}