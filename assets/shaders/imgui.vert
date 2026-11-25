#version 450 core

layout (location = 0) in vec2 aPos;    // координаты в ПИКСЕЛЯХ экрана
layout (location = 1) in vec2 aUV;
layout (location = 2) in vec4 aColor;  // упакован в 4 байта, не в float!

uniform mat4 uProjection;

out vec2 vUV;
out vec4 vColor;

void main()
{
    vUV = aUV;
    vColor = aColor;
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
}