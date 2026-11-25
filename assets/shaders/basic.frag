#version 450 core

in vec3 vColor;
in vec2 vUV;
in vec3 vNormal;

uniform sampler2D uTexture;
uniform vec3  uTint;
uniform float uUvScale;
uniform int   uUseTexture;
uniform vec3  uLightDir;
uniform int   uDebugNormals;

out vec4 FragColor;

void main()
{
    vec3 N = normalize(vNormal);

    if (uDebugNormals != 0)
    {
        FragColor = vec4(N * 0.5 + 0.5, 1.0);
        return;
    }

    vec3 albedo = vColor * uTint;
    if (uUseTexture != 0)
        albedo *= texture(uTexture, vUV * uUvScale).rgb;

    // Приближённый линейный workflow: sRGB -> linear -> освещение -> sRGB.
    // Настоящий sRGB-трансфер кусочный, а текстуры правильнее грузить
    // как GL_SRGB8_ALPHA8 — тогда декодирование делает железо бесплатно.
    albedo = pow(albedo, vec3(2.2));

    vec3 sky    = vec3(0.20, 0.23, 0.28);
    vec3 ground = vec3(0.07, 0.06, 0.055);
    vec3 sun    = vec3(0.75, 0.71, 0.62);

    vec3 ambient = mix(ground, sky, N.y * 0.5 + 0.5);
    float ndl = max(dot(N, normalize(uLightDir)), 0.0);
    vec3 lit = ambient + sun * ndl;          // максимум ~0.88, клиппинга нет

    FragColor = vec4(pow(albedo * lit, vec3(1.0 / 2.2)), 1.0);
}