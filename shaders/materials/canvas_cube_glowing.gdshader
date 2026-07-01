shader_type canvas_item;

// 3D顶点坐标数组 (单位立方体0~1)
uniform vec3[8] points;

// 边索引数组，每两个int为一条边 (point_a, point_b, ...)
uniform int[100] edges;

// edges数组的有效长度 (边数 × 2)
uniform int edges_size = 24;

// 线宽/柔度：越大线越粗、边缘越柔和
uniform float sofness = 0.1;

// 旋转速度倍率：越大旋转越快
uniform float time_scale = 500.0;

// 亮度衰减：值越小线条越亮/发光越强，值越大线条越暗/收束越紧
uniform float briness_scale = 1.6;

// 整体透明度：用于淡入淡出动画
uniform float alpha : hint_range(0.0, 1.0) = 1.0;

float line(vec2 pa, vec2 pb, vec2 uv) {
    vec2 direction = pb - pa;
    vec2 offset = uv - pa;
    float t = clamp(dot(offset, direction) / dot(direction, direction), 0.0, 1.0);
    vec2 projection = pa + direction * t;
    return length(uv - projection);
}

vec2 unproject(vec3 v3) {
    // 正交投影：丢弃Z轴
    return vec2(v3.x, v3.y);
}

vec3 rotated(vec3 v3, vec3 rotation) {
    float radX = radians(rotation.x);
    float radY = radians(rotation.y);
    float radZ = radians(rotation.z);
    // Rotation around the X-axis
    mat4 rotX = mat4(
        vec4(1., 0., 0., 0.),
        vec4(0., cos(radX), -sin(radX), 0.),
        vec4(0., sin(radX), cos(radX), 0.),
        vec4(0., 0., 0., 1.)
    );
    // Rotation around the Y-axis
    mat4 rotY = mat4(
        vec4(cos(radY), 0., sin(radY), 0.),
        vec4(0., 1., 0., 0.),
        vec4(-sin(radY), 0., cos(radY), 0.),
        vec4(0., 0., 0., 1.)
    );
    // Rotation around the Z-axis
    mat4 rotZ = mat4(
        vec4(cos(radZ), -sin(radZ), 0., 0.),
        vec4(sin(radZ), cos(radZ), 0., 0.),
        vec4(0., 0., 1., 0.),
        vec4(0., 0., 0., 1.)
    );
    // Combine rotations (order matters)
    mat4 rotationMatrix = rotX * rotY * rotZ;
    // Apply the rotation to the vector
    vec4 rotatedVec4 = rotationMatrix * vec4(v3, 1.0);
    // Convert back to vec3
    return vec3(rotatedVec4.rgb);
}

void fragment() {
    float time = time_scale * TIME + 100.;
    float liness = 0.0;
    for (int i = 0; i < edges_size / 2; i++) {
        int index_a = edges[i * 2];
        int index_b = edges[i * 2 + 1];
        vec3 pa = points[index_a];
        vec3 pb = points[index_b];
        // Rotate points around Y axis
        pa = rotated(pa - vec3(.5), vec3(time) * vec3(-.11, .17, .19));
        pb = rotated(pb - vec3(.5), vec3(time) * vec3(-.11, .17, .19));
        // Project to 2D
        vec2 pa2d = unproject(pa);
        vec2 pb2d = unproject(pb);
        // 计算当前像素到线段的距离 → -log 产生发光渐变
        liness += -log(briness_scale * line(pa2d, pb2d, UV * 2. - vec2(1.))) * sofness;
    }
    COLOR.a = liness * alpha;
}
