import { RIVER_SPOTS } from '../config';

const WAVES = `
float wave(vec2 p, float t) {
    return sin(p.x * 0.9 + t * 1.3) * 0.5 + cos(p.y * 1.1 - t * 1.05) * 0.5 + sin((p.x + p.y) * 0.6 + t * 0.8) * 0.35;
}`;

export const riverVertex = `
uniform float uTime;
uniform float uAmp;
attribute vec2 aPhase;
varying vec2 vUv;
varying vec3 vWorld;
${WAVES}
void main() {
    vUv = uv;
    vec4 world = modelMatrix * vec4(position, 1.0);
    float banks = smoothstep(0.0, 0.18, min(uv.y, 1.0 - uv.y));
    float facets = 0.65 * sin(uTime * 1.6 + aPhase.x) + 0.35 * sin(uTime * 2.7 + aPhase.y);
    world.y += (facets + 0.35 * wave(world.xz, uTime)) * uAmp * banks;
    vWorld = world.xyz;
    gl_Position = projectionMatrix * viewMatrix * world;
}`;

export const riverFragment = `
uniform float uTime;
uniform vec2 uSpots[${RIVER_SPOTS}];
uniform vec2 uActive[${RIVER_SPOTS}];
uniform vec3 uDeep;
uniform vec3 uSky;
uniform vec3 uDirt;
uniform vec3 uFog;
uniform float uLight;
uniform vec3 uMoonDir;
uniform float uFogDensity;
varying vec2 vUv;
varying vec3 vWorld;
void main() {
    vec3 n = normalize(cross(dFdx(vWorld), dFdy(vWorld)));
    if (n.y < 0.0) n = -n;
    vec3 v = normalize(cameraPosition - vWorld);
    float lambert = max(dot(n, uMoonDir), 0.0);
    float bank = 1.0 - abs(vUv.y - 0.5) * 2.0;
    vec3 col = mix(uSky, uDeep, 0.55 + bank * 0.35) * (0.5 + 0.7 * lambert);
    float spec = pow(max(dot(reflect(-uMoonDir, n), v), 0.0), 24.0);
    col += vec3(1.0) * spec * 0.35;
    float dirt = 0.0;
    for (int i = 0; i < ${RIVER_SPOTS}; i++) {
        float amount = uSpots[i].y;
        if (amount <= 0.0) continue;
        float d = vUv.x - uSpots[i].x;
        float len = 0.006 + 0.09 * amount;
        float along = d >= 0.0 ? exp(-d / len) : exp(d / 0.003);
        float width = 0.3 + 0.7 * amount;
        float across = 1.0 - smoothstep(width * 0.6, width, abs(vUv.y - 0.5) * 2.0 * 0.8);
        float pulse = uActive[i].x * (0.15 + 0.1 * sin(uTime * 6.0));
        dirt = max(dirt, (amount * 0.9 + pulse) * along * across);
    }
    dirt = clamp(dirt, 0.0, 0.85);
    col = mix(col, uDirt * (0.75 + 0.35 * lambert), dirt) * uLight;
    float fogFactor = 1.0 - exp(-uFogDensity * uFogDensity * dot(vWorld - cameraPosition, vWorld - cameraPosition));
    gl_FragColor = vec4(mix(col, uFog, clamp(fogFactor, 0.0, 1.0)), 1.0);
    #include <colorspace_fragment>
}`;
