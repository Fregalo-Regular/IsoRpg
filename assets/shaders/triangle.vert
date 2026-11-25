#version 450 core

// location = N должен совпадать с N в VertexAttribPointer на стороне C#
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aColor;

out vec3 vColor;   // будет интерполирован растеризатором

void main()
{
    gl_Position = vec4(aPosition, 1.0);
    vColor = aColor;
}