shader_type canvas_item;

uniform sampler2D  SCREEN_TEXTURE:hint_screen_texture,  filter_nearest;
uniform float offset = 0;
uniform float scale: hint_range(0.05, 1.0, 0.05) = 1.0;
uniform float alpha : hint_range(0.0, 1.0, 0.01) = 0.5;

varying float screen_baseY;

void vertex() {
    // uv to screen_uv for getting top screen_uv.y when uv.y==0
    // firstly uv to local then local to screen_uv, local coordinate is center based so we should convert (0,1) to (-1, 1) mulity with pixles distance
	mat4 t = mat4(vec4(2.0, 0, 0, 0), vec4(0, 2.0, 0, 0), vec4(0, 0, 1.0, 0), vec4(-1.0, -1.0, 0, 1.0));
	screen_baseY = (inverse(t)*SCREEN_MATRIX*CANVAS_MATRIX *  (MODEL_MATRIX)*vec4(-1.0/TEXTURE_PIXEL_SIZE.x/2.0,  -1.0/TEXTURE_PIXEL_SIZE.y/2.0, 0, 1.0)).y;
}

void fragment() {

	vec2 uv =  SCREEN_UV;
	uv.y -= screen_baseY;
	uv.y *= scale;
	uv.y += screen_baseY;

	COLOR = texture(SCREEN_TEXTURE, vec2(uv.x, (screen_baseY*2.0- (uv.y-offset))));
	COLOR.a *= alpha;
}
