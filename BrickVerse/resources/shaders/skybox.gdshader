shader_type sky;

// Sun Disc
uniform vec3 sun_disc_color : source_color = vec3(1.0, 1.0, 1.0);
uniform float sun_disc_multiplier : hint_range(0.0, 50.0) = 25.0;
uniform float sun_disc_exponent : hint_range(1000.0, 200000.0) = 125000.0;

// Sun Halo  
uniform vec3 sun_halo_color : source_color = vec3(0.8970588, 0.7760561, 0.6661981);
uniform float sun_halo_exponent : hint_range(50.0, 500.0) = 125.0;
uniform float sun_halo_contribution : hint_range(0.0, 1.0) = 0.75;

// Horizon Line
uniform vec3 horizon_line_color : source_color = vec3(0.9044118, 0.8872592, 0.7913603);
uniform float horizon_line_exponent : hint_range(1.0, 10.0) = 4.0;
uniform float horizon_line_contribution : hint_range(0.0, 1.0) = 0.25;

// Sky Gradient
uniform vec3 sky_gradient_top : source_color = vec3(0.172549, 0.5686274, 0.6941177);
uniform vec3 sky_gradient_bottom : source_color = vec3(0.764706, 0.8156863, 0.8509805);
uniform float sky_gradient_exponent : hint_range(0.5, 5.0) = 2.5;

void sky() {
    // Get the current pixel's direction in world space
    vec3 world_position = normalize(EYEDIR);
    
    // Masks
    float mask_horizon = dot(world_position, vec3(0.0, 1.0, 0.0));
    float mask_sun_dir = dot(world_position, LIGHT0_DIRECTION);
    
    // Sun disc
    float mask_sun = pow(clamp(mask_sun_dir, 0.0, 1.0), sun_disc_exponent);
    mask_sun = clamp(mask_sun * sun_disc_multiplier, 0.0, 1.0);
    
    // Sun halo
    vec3 sun_halo_col = sun_halo_color * sun_halo_contribution;
    float bell_curve = pow(clamp(mask_sun_dir, 0.0, 1.0), sun_halo_exponent * clamp(abs(mask_horizon), 0.0, 1.0));
    float horizon_soften = 1.0 - pow(1.0 - clamp(mask_horizon, 0.0, 1.0), 50.0);
    sun_halo_col *= clamp(bell_curve * horizon_soften, 0.0, 1.0);
    
    // Horizon line
    vec3 horizon_line_col = horizon_line_color * clamp(pow(1.0 - abs(mask_horizon), horizon_line_exponent), 0.0, 1.0);
    horizon_line_col = mix(vec3(0.0), horizon_line_col, horizon_line_contribution);
    
    // Sky gradient
    vec3 sky_gradient_col = mix(sky_gradient_top, sky_gradient_bottom, pow(1.0 - clamp(mask_horizon, 0.0, 1.0), sky_gradient_exponent));
    
    // Final color
    vec3 base_color = clamp(sun_halo_col + horizon_line_col + sky_gradient_col, 0.0, 1.0);
    vec3 final_color = mix(base_color, sun_disc_color, mask_sun);
    
    COLOR = final_color;
}