shader_type canvas_item;

uniform bool isInRadians = false;

group_uniforms ShaderParameters.DegreesRotation;
uniform float xDegrees : hint_range(0, 360);
uniform float yDegrees : hint_range(0, 360);
uniform float zDegrees : hint_range(0, 360);
group_uniforms;

group_uniforms ShaderParameters.RadianRotation;
uniform float xRadians : hint_range(0, 6.2830);
uniform float yRadians : hint_range(0, 6.2830);
uniform float zRadians : hint_range(0, 6.2830);
group_uniforms;

float degToRad(float angle) {
	return angle * PI / 180.0;
}

void vertex() {
	float radX = xRadians;
	float radY = yRadians;
	float radZ = zRadians;

	if (!isInRadians) {
		radX = degToRad(xDegrees);
		radY = degToRad(yDegrees);
		radZ = degToRad(zDegrees);
	}

	float cosX = cos(radX);
	float cosY = cos(radY);
	float cosZ = cos(radZ);

	float sinX = sin(radX);
	float sinY = sin(radY);
	float sinZ = sin(radZ);

	mat3 finalRot;
	finalRot[0][0] = cosZ * cosY;
	finalRot[1][0] = cosZ * sinY * sinX - sinZ * cosX;
	finalRot[2][0] = sinZ * sinX + cosZ * sinY * cosX;

	finalRot[0][1] = sinZ * cosY;
	finalRot[1][1] = cosZ * cosX + sinZ * sinY * sinX;
	finalRot[2][1] = sinZ * sinY * cosX - cosZ * sinX;

	finalRot[0][2] = -sinY;
	finalRot[1][2] = cosY * sinX;
	finalRot[2][2] = cosY * cosX;

	vec3 pos = vec3(VERTEX.xy, 0.0) * finalRot;
	VERTEX = pos.xy;
}

void fragment() {
}
