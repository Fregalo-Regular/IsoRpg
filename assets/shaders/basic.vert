#version 450 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aColor;
layout (location = 2) in vec2 aUV;
layout (location = 3) in vec3 aNormal;
layout (location = 4) in vec4 aJoints;
layout (location = 5) in vec4 aWeights;

uniform mat4 uViewProjection;
uniform mat4 uModel;
uniform mat4 uJoints[128];
uniform int  uSkinned;

out vec3 vColor;
out vec2 vUV;
out vec3 vNormal;

void main()
{
    vec4 p = vec4(aPosition, 1.0);
    vec3 n = aNormal;

    if (uSkinned != 0 && dot(aWeights, vec4(1.0)) > 0.0)
    {
        mat4 skin = aWeights.x * uJoints[int(aJoints.x)]
                  + aWeights.y * uJoints[int(aJoints.y)]
                  + aWeights.z * uJoints[int(aJoints.z)]
                  + aWeights.w * uJoints[int(aJoints.w)];
        p = skin * p;
        n = mat3(skin) * n;      
    }

    vColor  = aColor;
    vUV     = aUV;
    vNormal = mat3(uModel) * n;
    gl_Position = uViewProjection * uModel * p;
}