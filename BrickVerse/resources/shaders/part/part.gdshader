shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

instance uniform vec4 color : source_color = vec4(1.0);

uniform sampler2D albedo : source_color;
uniform bool use_albedo_texture = true;

uniform sampler2D orm;
uniform bool use_orm_texture = true;

uniform sampler2D normal_tex : hint_normal;
uniform bool use_normal_texture = false;

uniform float emissive_strength : hint_range(0.0, 5.0) = 0.0;

uniform float metallic_specular : hint_range(0.0, 1.0) = 0.2;
uniform float roughness : hint_range(0.0, 1.0) = 0.7;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float studs_per_tile = 2.0;
uniform bool use_studs = false;
uniform float stud_spacing : hint_range(0.25, 4.0) = 1.0;
uniform float stud_radius : hint_range(0.05, 0.48) = 0.30;

varying vec3 v_local_pos;
varying vec3 v_local_normal;
varying vec3 v_object_scale;

varying vec4 instance_custom;

vec2 calculate_uv(vec3 pos, vec3 normal)
{
	vec3 an = abs(normal);
	vec2 uv;

	if (an.x >= an.y && an.x >= an.z) {
		uv = pos.zy;
		if (normal.x < 0.0) {
			uv.x = -uv.x;
		}
	} else if (an.y >= an.z) {
		uv = pos.xz;
		if (normal.y < 0.0) {
			uv.y = -uv.y;
		}
	} else {
		uv = pos.xy;
		if (normal.z > 0.0) {
			uv.x = -uv.x;
		}
	}

	return uv;
}

void vertex() {
	v_local_pos = VERTEX;
	v_local_normal = normalize(NORMAL);
	v_object_scale = vec3(
		length(MODEL_MATRIX[0].xyz),
		length(MODEL_MATRIX[1].xyz),
		length(MODEL_MATRIX[2].xyz)
	);

	instance_custom = INSTANCE_CUSTOM;
}

void fragment() {
	vec3 n = normalize(v_local_normal);
	vec3 part_space = v_local_pos * v_object_scale;
	vec2 uv = calculate_uv(part_space, n) / studs_per_tile;
	vec4 albedo_color = color;
	
	float roughness_tex = 1.0;
	float metallic_tex = 1.0;

	if (use_albedo_texture) {
		albedo_color *= texture(albedo, uv);
	}

	if (use_orm_texture) {
		vec3 orm_values = texture(orm, uv).rgb;
		roughness_tex = orm_values.g;
		metallic_tex = orm_values.b;
	}

	if (use_normal_texture) {
		NORMAL_MAP = texture(normal_tex, uv).rgb;
	}

	// Studs belong to the local top face only. They are generated
	// in part space so resizing a Part adds/removes studs instead of stretching
	// a texture across it.
	if (use_studs && n.y > 0.9) {
		vec2 stud_grid = part_space.xz + v_object_scale.xz * 0.5;
		vec2 stud_cell = fract(stud_grid / stud_spacing) - vec2(0.5);
		float stud_distance = length(stud_cell) * stud_spacing;
		float edge_width = max(fwidth(stud_distance), 0.006);
		float stud_mask = 1.0 - smoothstep(stud_radius - edge_width, stud_radius + edge_width, stud_distance);
		float normalized_distance = clamp(stud_distance / stud_radius, 0.0, 1.0);
		float dome = sqrt(max(0.0, 1.0 - normalized_distance * normalized_distance));
		vec3 stud_normal = normalize(vec3(-stud_cell.x * 1.8, stud_cell.y * 1.8, max(0.22, dome)));
		NORMAL_MAP = mix(vec3(0.5, 0.5, 1.0), stud_normal * 0.5 + 0.5, stud_mask);
		NORMAL_MAP_DEPTH = 0.85;
		float rim = smoothstep(stud_radius - 0.055, stud_radius, stud_distance) * stud_mask;
		albedo_color.rgb *= 1.0 + dome * stud_mask * 0.055 - rim * 0.10;
		roughness_tex = mix(roughness_tex, 0.82, stud_mask);
	}

	ALBEDO = albedo_color.rgb * COLOR.rgb;
	SPECULAR = metallic_specular;
	ROUGHNESS = roughness_tex * roughness;
	METALLIC = metallic_tex * metallic;

	if (emissive_strength > 0.0) {
		EMISSION = ALBEDO * emissive_strength;
	}
}

//void light() {
//	// Called for every pixel for every light affecting the material.
//	// Uncomment to replace the default light processing function with this one.
//}
