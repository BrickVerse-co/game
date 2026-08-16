shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

instance uniform int bv_effect = 0;
instance uniform vec4 bv_color : source_color = vec4(1.0);
instance uniform vec4 bv_effect_color : source_color = vec4(0.15, 0.8, 1.0, 1.0);
instance uniform vec4 bv_secondary_color : source_color = vec4(0.7, 0.2, 1.0, 1.0);
instance uniform float bv_strength : hint_range(0.0, 1.0) = 0.75;
instance uniform float bv_speed : hint_range(0.0, 10.0) = 1.0;
instance uniform float bv_scale : hint_range(0.1, 100.0) = 4.0;
instance uniform float bv_progress : hint_range(0.0, 1.0) = 0.0;

varying vec3 bv_world_position;

float bv_hash(vec3 value) {
	value = fract(value * 0.1031);
	value += dot(value, value.yzx + 33.33);
	return fract((value.x + value.y) * value.z);
}

void vertex() {
	bv_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	if (bv_effect == 7) {
		float slice = step(0.82, bv_hash(vec3(floor(VERTEX.y * bv_scale), floor(TIME * bv_speed * 8.0), 1.0)));
		VERTEX.x += (slice * 2.0 - 1.0) * bv_strength * 0.12;
	}
}

void fragment() {
	vec3 base = bv_color.rgb;
	float alpha = bv_color.a;
	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	float pulse = 0.5 + 0.5 * sin(TIME * bv_speed * 6.283185);

	if (bv_effect == 1) {
		float scan = 0.55 + 0.45 * sin((bv_world_position.y * bv_scale - TIME * bv_speed) * 12.0);
		base = mix(base, bv_effect_color.rgb, bv_strength);
		EMISSION = bv_effect_color.rgb * (scan + fresnel) * bv_strength;
		alpha *= mix(1.0, 0.3 + scan * 0.35, bv_strength);
	} else if (bv_effect == 2) {
		base = mix(base, bv_effect_color.rgb, fresnel * bv_strength);
		EMISSION = mix(bv_secondary_color.rgb, bv_effect_color.rgb, fresnel) * fresnel * bv_strength * 2.0;
		alpha *= mix(1.0, 0.18 + fresnel * 0.72, bv_strength);
	} else if (bv_effect == 3) {
		float bands = floor((base.r + base.g + base.b) * bv_scale) / max(bv_scale, 1.0);
		base = mix(base, base * (0.65 + bands * 0.35), bv_strength);
	} else if (bv_effect == 4) {
		base = mix(base, bv_effect_color.rgb, pulse * bv_strength);
		EMISSION = mix(bv_effect_color.rgb, bv_secondary_color.rgb, pulse) * pulse * bv_strength * 2.0;
	} else if (bv_effect == 5) {
		float noise = bv_hash(floor(bv_world_position * bv_scale));
		float edge = smoothstep(bv_progress - 0.08, bv_progress + 0.02, noise);
		alpha *= edge;
		EMISSION = bv_effect_color.rgb * (1.0 - smoothstep(bv_progress, bv_progress + 0.08, noise)) * edge * bv_strength * 3.0;
	} else if (bv_effect == 6) {
		base = mix(base, bv_effect_color.rgb, fresnel * bv_strength);
		EMISSION = bv_effect_color.rgb * fresnel * bv_strength * 2.0;
	} else if (bv_effect == 7) {
		float split = step(0.5, fract(bv_world_position.y * bv_scale + TIME * bv_speed));
		base = mix(base, mix(bv_effect_color.rgb, bv_secondary_color.rgb, split), bv_strength * 0.45);
		EMISSION = base * bv_strength * 0.5;
	}

	ALBEDO = base;
	ALPHA = clamp(alpha, 0.0, 1.0);
	ROUGHNESS = mix(0.75, 0.2, bv_strength);
}
