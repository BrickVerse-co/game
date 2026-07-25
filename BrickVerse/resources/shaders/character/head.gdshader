shader_type spatial;

render_mode blend_mix,
	depth_draw_opaque,
	cull_back,
	diffuse_burley,
	specular_schlick_ggx;

instance uniform vec4 albedo : source_color = vec4(1.0);

uniform sampler2D face_texture
	: source_color,
	filter_linear_mipmap,
	repeat_disable;

uniform bool face_enabled = true;

uniform vec2 face_center = vec2(0.0, 0.02568);
uniform vec2 face_size = vec2(0.0115, 0.0115);
uniform float front_threshold : hint_range(-1.0, 1.0) = 0.15;

varying vec3 mesh_position;
varying vec3 mesh_normal;

void vertex()
{
	mesh_position = VERTEX;
	mesh_normal = NORMAL;
}

void fragment()
{
	vec3 final_albedo = albedo.rgb;

	vec3 normal = normalize(mesh_normal);

	float front_facing = -normal.y;

	vec2 face_uv = vec2(
		(mesh_position.x - face_center.x) / face_size.x + 0.5,
		(mesh_position.z - face_center.y) / face_size.y + 0.5
	);

	bool inside_face =
		face_uv.x >= 0.0 &&
		face_uv.x <= 1.0 &&
		face_uv.y >= 0.0 &&
		face_uv.y <= 1.0;

	if (
		face_enabled &&
		inside_face &&
		front_facing >= front_threshold
	)
	{
		face_uv.y = 1.0 - face_uv.y;

		vec4 face = texture(face_texture, face_uv);

		final_albedo = mix(
			final_albedo,
			face.rgb,
			face.a
		);
	}

	ALBEDO = final_albedo;
	ALPHA = albedo.a;
}