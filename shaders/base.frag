#version 450

layout(location = 0) flat in float fragColor;
layout(location = 1) in vec2 uv;

layout(location = 0) out vec4 outColor;

layout(binding = 0) uniform sampler1D kernelTex;

vec3 SmoothFunction(vec2 uv) {
    float d = length(uv);
    vec2 kernel_slope = texture(kernelTex, d).xy;
    vec2 dir = vec2(0);
    if(d>0.0) dir = normalize(uv);
    return vec3(kernel_slope.x, kernel_slope.y*dir);
}

void main() {
    vec2 xy = (uv-vec2(0.5,0.5))*2;
    float r = length(xy);
    if (r>=1)
        discard;
    vec3 kernel = SmoothFunction(xy);
    outColor = vec4(fragColor*kernel, 1);
}