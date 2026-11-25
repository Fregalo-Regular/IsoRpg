#version 450 core

in vec3 vColor;      // сюда приходит УЖЕ интерполированный цвет
out vec4 FragColor;

uniform float uTime;

void main()
{
    FragColor = vec4(vColor * (0.5 + 0.5 * sin(uTime * 2.0)), 1.0);
}