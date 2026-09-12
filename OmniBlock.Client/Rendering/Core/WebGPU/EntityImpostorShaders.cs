namespace OmniBlock.Client.Rendering.Core.WebGPU;

internal static class EntityImpostorShaders
{
    public const string Capture = """
        struct Uniforms { matrix: mat4x4<f32> }
        @group(0) @binding(0) var<uniform> u: Uniforms;
        @group(1) @binding(0) var skin: texture_2d<f32>;
        @group(1) @binding(1) var skinSampler: sampler;
        struct Out { @builtin(position) position: vec4<f32>, @location(0) uv: vec2<f32>, @location(1) shade: f32 }
        @vertex fn vs_main(@location(0) p: vec3<f32>, @location(1) uv: vec2<f32>, @location(2) n: vec3<f32>) -> Out {
            var o: Out;
            o.position = u.matrix * vec4(p, 1.0);
            o.uv = uv;
            o.shade = min(1.0, 0.4 + 0.6 * max(0.0, dot(n, normalize(vec3(0.2,1.0,-0.7))))
                + 0.6 * max(0.0, dot(n, normalize(vec3(-0.2,1.0,0.7)))));
            return o;
        }
        @fragment fn fs_main(o: Out) -> @location(0) vec4<f32> {
            let c = textureSample(skin, skinSampler, o.uv);
            if c.a < 0.1 { discard; }
            return vec4(c.rgb * o.shade, c.a);
        }
        """;

    public const string Present = """
        struct Uniforms {
            view: mat4x4<f32>, projection: mat4x4<f32>, fogColor: vec4<f32>, fog: vec4<f32>
        }
        struct Instance { center: vec4<f32>, right: vec4<f32>, up: vec4<f32>, uv: vec4<f32> }
        @group(0) @binding(0) var<uniform> u: Uniforms;
        @group(1) @binding(0) var<storage,read> instances: array<Instance>;
        @group(2) @binding(0) var atlas: texture_2d<f32>;
        @group(2) @binding(1) var atlasSampler: sampler;
        struct Out { @builtin(position) position: vec4<f32>, @location(0) uv: vec2<f32>,
            @location(1) light: f32, @location(2) distance: f32 }
        @vertex fn vs_main(@builtin(vertex_index) vertex: u32, @builtin(instance_index) instance: u32) -> Out {
            var corners = array<vec2<f32>,6>(vec2(-1.0,-1.0),vec2(1.0,-1.0),vec2(1.0,1.0),
                vec2(-1.0,-1.0),vec2(1.0,1.0),vec2(-1.0,1.0));
            let c = corners[vertex]; let i = instances[instance];
            let eye = u.view * vec4(i.center.xyz + c.x * i.right.xyz + c.y * i.up.xyz,1.0);
            var o: Out;
            o.position = u.projection * eye;
            o.uv = i.uv.xy + vec2(c.x * 0.5 + 0.5, 0.5 - c.y * 0.5) * i.uv.zw;
            o.light = i.center.w; o.distance = abs(eye.z);
            return o;
        }
        @fragment fn fs_main(o: Out) -> @location(0) vec4<f32> {
            let c = textureSample(atlas, atlasSampler, o.uv);
            if c.a < 0.1 { discard; }
            var visibility = 1.0;
            if u.fog.w > 0.0 {
                if u.fog.w == 1.0 { visibility = (u.fog.y - o.distance) / max(0.0001,u.fog.y-u.fog.x); }
                else if u.fog.w == 2.0 { visibility = exp(-u.fog.z * o.distance); }
                else { visibility = exp(-pow(u.fog.z * o.distance, 2.0)); }
            }
            return vec4(mix(u.fogColor.rgb, c.rgb * o.light, clamp(visibility,0.0,1.0)),1.0);
        }
        """;
}
