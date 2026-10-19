import {
  HalfFloatType,
  type Material,
  type Object3D,
  type PerspectiveCamera,
  type Scene,
  type Sprite,
  type WebGLRenderer,
  WebGLRenderTarget
} from 'three';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { GTAOPass } from 'three/examples/jsm/postprocessing/GTAOPass.js';
import { OutputPass } from 'three/examples/jsm/postprocessing/OutputPass.js';
import { RenderPass } from 'three/examples/jsm/postprocessing/RenderPass.js';
import { SMAAPass } from 'three/examples/jsm/postprocessing/SMAAPass.js';
import { type Xray, XrayPass } from './Xray';

interface GtaoVisibility {
  _overrideVisibility(): void;
  _visibilityCache: Object3D[];
}

/** Whether ambient occlusion should leave o out: sprites and transparent surfaces would shade what lies behind them. */
function seeThrough(o: Object3D): boolean {
  const material = (o as Object3D & { material?: Material | Material[] }).material;
  return (o as Sprite).isSprite === true || (!!material && !Array.isArray(material) && material.transparent);
}

/**
 * GTAO hides points and lines while it renders its depth and normals; this hides sprites and
 * transparent surfaces as well, so glows, rain and water do not cast dark halos.
 */
function hideSeeThroughFromAo(gtao: GTAOPass, scene: Scene): void {
  const pass = gtao as unknown as GtaoVisibility;
  const hide = pass._overrideVisibility.bind(gtao);
  pass._overrideVisibility = () => {
    hide();
    scene.traverse(o => {
      if (o.visible && seeThrough(o)) {
        o.visible = false;
        pass._visibilityCache.push(o);
      }
    });
  };
}

const MSAA = 4;

/** The post-processing chain, multisampled and sharp: the x-ray under the ground, ambient occlusion, tone mapping, SMAA. */
export class Effects {
  private readonly composer: EffectComposer;
  private readonly gtao: GTAOPass;

  constructor(
    private readonly renderer: WebGLRenderer,
    scene: Scene,
    camera: PerspectiveCamera,
    xray: Xray
  ) {
    this.composer = new EffectComposer(
      renderer,
      new WebGLRenderTarget(1, 1, { type: HalfFloatType, stencilBuffer: true, samples: MSAA })
    );
    this.composer.addPass(new RenderPass(scene, camera));
    this.composer.addPass(new XrayPass(xray, camera));
    this.gtao = new GTAOPass(scene, camera, 256, 256);
    this.gtao.updateGtaoMaterial({ radius: 1.2, distanceExponent: 1.5, thickness: 2, scale: 1.2, samples: 12 });
    this.gtao.blendIntensity = 0.7;
    hideSeeThroughFromAo(this.gtao, scene);
    this.composer.addPass(this.gtao);
    this.composer.addPass(new OutputPass());
    this.composer.addPass(new SMAAPass());
  }

  /** Sizes every pass to w x h CSS pixels. */
  resize(w: number, h: number): void {
    const pr = this.renderer.getPixelRatio();
    this.composer.setPixelRatio(pr);
    this.composer.setSize(w, h);
    this.gtao.setSize(Math.round(w * pr), Math.round(h * pr));
  }

  render(): void {
    this.composer.render();
  }

  dispose(): void {
    this.composer.dispose();
  }
}
