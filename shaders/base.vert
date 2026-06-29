#version 450
layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec4 inColor;
layout(location = 2) in vec2 instancePos;
layout(location = 3) in vec2 velocity;
layout(location = 4) in float density;
layout(location = 5) in float pressure;
layout(location = 6) in float energy;
layout(location = 7) in vec4 c;

layout(location = 0) out float fragColor;
layout(location = 1) out vec2 uv;

layout(push_constant) uniform PushConstants {
    vec2 xrange;
    vec2 yrange;
    int visualizationIndex;
    float particleSize;
    int particlesNum;
}push;

float array[4] = float[4] (density,
        length(velocity),
        pressure,
        energy);
void main() {
    vec2 dpos = vec2((push.xrange.y - push.xrange.x), (push.yrange.y - push.yrange.x));

    vec2 pos = vec2(instancePos.x -push.xrange.x, 
                instancePos.y - push.yrange.x)/dpos*2.0
                +vec2(-1, -1);
    
    float h = push.particleSize*c.b;
    float alpha = 1.0/density/(h*h)*10.0/push.particlesNum;
    
    vec2 newPos = inPosition/dpos*h+ pos;

    gl_Position = vec4(newPos, 0.0, 1.0);
    
    fragColor = array[push.visualizationIndex]*alpha;
    uv = (inPosition+vec2(1))*0.5;
}
