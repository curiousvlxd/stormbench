export const skyVertex = `
varying vec3 vDir;
void main() {
    vDir = normalize(position);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}`;

export const skyFragment = `
uniform vec3 uZenith;
uniform vec3 uHorizon;
uniform float uStorm;
uniform vec3 uNightZenith;
uniform vec3 uNightHorizon;
uniform vec3 uWarmHorizon;
uniform float uNight;
uniform float uWarm;
varying vec3 vDir;
void main() {
    float h = clamp(vDir.y, 0.0, 1.0);
    vec3 horizon = mix(mix(uHorizon, uWarmHorizon, uWarm * 0.8), uNightHorizon, uNight);
    vec3 zenith = mix(uZenith, uNightZenith, uNight);
    vec3 col = mix(horizon, zenith, pow(h, 0.55));
    col = mix(col, mix(vec3(0.42, 0.47, 0.53), vec3(0.11, 0.14, 0.2), uNight), uStorm * 0.85);
    gl_FragColor = vec4(col, 1.0);
    #include <colorspace_fragment>
}`;
