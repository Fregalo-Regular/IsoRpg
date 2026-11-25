#version 450 core

in vec2 vUV;
in vec4 vColor;

uniform sampler2D uTexture;

out vec4 FragColor;

void main()
{
    FragColor = vColor * texture(uTexture, vUV);
}