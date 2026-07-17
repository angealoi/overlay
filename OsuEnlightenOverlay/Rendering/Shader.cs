using System;
using System.Text;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace OsuEnlightenOverlay.Rendering
{
    /// <summary>
    /// GLSL 셰이더 래퍼 — osu! stable Graphics/Shaders/ 포팅.
    /// </summary>
    internal class Shader : IDisposable
    {
        int programId;
        int vertexShaderId;
        int fragmentShaderId;

        public int ProgramId { get { return programId; } }
        public bool IsValid { get { return programId > 0; } }

        // 전역 uniform 위치 캐시
        int projMatrixLoc = -1;
        int textureLoc = -1;
        int colourLoc = -1;

        // attribute 위치 캐시 — generic vertex attributes (glVertexAttribPointer)
        int positionLoc = -1;
        int texCoordLoc = -1;
        int colourAttrLoc = -1;

        public int PositionLoc { get { return positionLoc; } }
        public int TexCoordLoc { get { return texCoordLoc; } }
        public int ColourAttrLoc { get { return colourAttrLoc; } }

        public Shader(string vertexSource, string fragmentSource)
        {
            vertexShaderId = CompileShader(ShaderType.VertexShader, vertexSource);
            fragmentShaderId = CompileShader(ShaderType.FragmentShader, fragmentSource);

            programId = GL.CreateProgram();
            GL.AttachShader(programId, vertexShaderId);
            GL.AttachShader(programId, fragmentShaderId);
            GL.LinkProgram(programId);

            int linkStatus;
            GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out linkStatus);
            if (linkStatus == 0)
            {
                string log = GL.GetProgramInfoLog(programId);
                // overlay.log에 남긴다 (F10) — 예전엔 Debug.WriteLine이라 릴리스/로그에 안 남고
                // 조용히 fixed-function 폴백으로 그려져 셰이더 실패 원인 추적이 불가했다.
                System.Console.WriteLine("[Shader] Link failed (fixed-function 폴백으로 그려짐): " + log);
                GL.DeleteProgram(programId);
                programId = 0;
            }

            if (IsValid)
            {
                projMatrixLoc = GL.GetUniformLocation(programId, "g_ProjMatrix");
                textureLoc = GL.GetUniformLocation(programId, "g_Texture");
                colourLoc = GL.GetUniformLocation(programId, "g_Colour");

                // attribute location 캐싱
                positionLoc = GL.GetAttribLocation(programId, "a_Position");
                texCoordLoc = GL.GetAttribLocation(programId, "a_TexCoord");
                colourAttrLoc = GL.GetAttribLocation(programId, "a_Colour");
            }
        }

        int CompileShader(ShaderType type, string source)
        {
            int shaderId = GL.CreateShader(type);
            GL.ShaderSource(shaderId, source);
            GL.CompileShader(shaderId);

            int compileStatus;
            GL.GetShader(shaderId, ShaderParameter.CompileStatus, out compileStatus);
            if (compileStatus == 0)
            {
                string log = GL.GetShaderInfoLog(shaderId);
                // overlay.log에 남긴다 (F10) — 조용한 실패 방지
                System.Console.WriteLine("[Shader] Compile failed (" + type + "): " + log);
                GL.DeleteShader(shaderId);
                return 0;
            }
            return shaderId;
        }

        public void Begin()
        {
            if (!IsValid) return;
            GL.UseProgram(programId);
        }

        public void End()
        {
            GL.UseProgram(0);
        }

        public void SetProjectionMatrix(ref Matrix4 matrix)
        {
            if (projMatrixLoc >= 0)
                GL.UniformMatrix4(projMatrixLoc, false, ref matrix);
        }

        public void SetTexture(int textureUnit)
        {
            if (textureLoc >= 0)
                GL.Uniform1(textureLoc, textureUnit);
        }

        public void SetColour(System.Drawing.Color colour)
        {
            if (colourLoc >= 0)
                GL.Uniform4(colourLoc, colour.R / 255f, colour.G / 255f, colour.B / 255f, colour.A / 255f);
        }

        public void Dispose()
        {
            if (programId > 0)
            {
                GL.DeleteProgram(programId);
                programId = 0;
            }
            if (vertexShaderId > 0)
            {
                GL.DeleteShader(vertexShaderId);
                vertexShaderId = 0;
            }
            if (fragmentShaderId > 0)
            {
                GL.DeleteShader(fragmentShaderId);
                fragmentShaderId = 0;
            }
        }
    }

    /// <summary>
    /// 셰이더 소스 — osu! stable OsuVertexShader/OsuFragmentShader 포팅.
    /// </summary>
    internal static class ShaderSources
    {
        // TextureShader2D — 기본 2D 텍스처 (hitobject, hitburst, spinner 등)
        public const string TextureVertex = @"
            attribute vec2 a_Position;
            attribute vec2 a_TexCoord;
            attribute vec4 a_Colour;
            uniform mat4 g_ProjMatrix;
            varying vec2 v_TexCoord;
            varying vec4 v_Colour;
            void main()
            {
                gl_Position = g_ProjMatrix * vec4(a_Position, 0.0, 1.0);
                v_TexCoord = a_TexCoord;
                v_Colour = a_Colour;
            }
        ";

        public const string TextureFragment = @"
            varying vec2 v_TexCoord;
            varying vec4 v_Colour;
            uniform sampler2D g_Texture;
            uniform vec4 g_Colour;
            void main()
            {
                vec4 texColor = texture2D(g_Texture, v_TexCoord);
                gl_FragColor = texColor * v_Colour * g_Colour;
            }
        ";

        // ColourShader2D — 색상 전용 (슬라이더 텍스처 생성, 배경)
        public const string ColourVertex = @"
            attribute vec2 a_Position;
            attribute vec4 a_Colour;
            uniform mat4 g_ProjMatrix;
            varying vec4 v_Colour;
            void main()
            {
                gl_Position = g_ProjMatrix * vec4(a_Position, 0.0, 1.0);
                v_Colour = a_Colour;
            }
        ";

        public const string ColourFragment = @"
            varying vec4 v_Colour;
            uniform vec4 g_Colour;
            void main()
            {
                gl_FragColor = v_Colour * g_Colour;
            }
        ";

        // TextureShader3D — 3D 텍스처 (슬라이더 바디). vec3 position + depth buffer.
        // osu! stable sh_Texture3D.vs 포팅.
        public const string Texture3DVertex = @"
            attribute vec3 a_Position;
            attribute vec2 a_TexCoord;
            attribute vec4 a_Colour;
            uniform mat4 g_ProjMatrix;
            varying vec2 v_TexCoord;
            varying vec4 v_Colour;
            void main()
            {
                gl_Position = g_ProjMatrix * vec4(a_Position, 1.0);
                v_TexCoord = a_TexCoord;
                v_Colour = a_Colour;
            }
        ";

        public const string Texture3DFragment = @"
            varying vec2 v_TexCoord;
            varying vec4 v_Colour;
            uniform sampler2D g_Texture;
            uniform vec4 g_Colour;
            void main()
            {
                vec4 texColor = texture2D(g_Texture, v_TexCoord);
                gl_FragColor = texColor * v_Colour * g_Colour;
            }
        ";
    }

    /// <summary>
    /// 셰이더 관리자 — 셰이더 로드/캐싱.
    /// </summary>
    internal class ShaderManager : IDisposable
    {
        public Shader TextureShader2D;
        public Shader ColourShader2D;
        public Shader TextureShader3D;

        public void LoadAll()
        {
            TextureShader2D = new Shader(ShaderSources.TextureVertex, ShaderSources.TextureFragment);
            ColourShader2D = new Shader(ShaderSources.ColourVertex, ShaderSources.ColourFragment);
            TextureShader3D = new Shader(ShaderSources.Texture3DVertex, ShaderSources.Texture3DFragment);
        }

        public void Dispose()
        {
            if (TextureShader2D != null) TextureShader2D.Dispose();
            if (ColourShader2D != null) ColourShader2D.Dispose();
            if (TextureShader3D != null) TextureShader3D.Dispose();
        }
    }
}