import { CanvasTexture, RepeatWrapping, Vector3 } from 'three';
import { lehmer } from '../math/random';

/** A canvas of the given size, drawn by `draw`. */
function canvas(width: number, height: number, draw: (g: CanvasRenderingContext2D) => void): HTMLCanvasElement {
  const c = document.createElement('canvas');
  c.width = width;
  c.height = height;
  const g = c.getContext('2d');
  if (g) draw(g);
  return c;
}

/** A canvas texture that repeats in both directions. */
function repeating(c: HTMLCanvasElement): CanvasTexture {
  const texture = new CanvasTexture(c);
  texture.wrapS = texture.wrapT = RepeatWrapping;
  return texture;
}

/** One bright band per repeat: the flow running along pipes and the radio link. */
export function stripeTexture(): CanvasTexture {
  return repeating(
    canvas(4, 64, g => {
      const gradient = g.createLinearGradient(0, 0, 0, 64);
      gradient.addColorStop(0, '#000');
      gradient.addColorStop(0.55, '#000');
      gradient.addColorStop(0.8, '#fff');
      gradient.addColorStop(1, '#000');
      g.fillStyle = gradient;
      g.fillRect(0, 0, 4, 64);
    })
  );
}

/** A white cross: the mark where the link to the control room is broken. */
export function crossTexture(): CanvasTexture {
  return new CanvasTexture(
    canvas(64, 64, g => {
      g.strokeStyle = '#fff';
      g.lineWidth = 9;
      g.lineCap = 'round';
      g.beginPath();
      g.moveTo(14, 14);
      g.lineTo(50, 50);
      g.moveTo(50, 14);
      g.lineTo(14, 50);
      g.stroke();
    })
  );
}

/** A soft round glow for beacons, lights, foam and splashes. */
export function glowTexture(): CanvasTexture {
  return new CanvasTexture(
    canvas(64, 64, g => {
      const gradient = g.createRadialGradient(32, 32, 0, 32, 32, 32);
      gradient.addColorStop(0, 'rgba(255,255,255,1)');
      gradient.addColorStop(0.3, 'rgba(255,255,255,0.55)');
      gradient.addColorStop(1, 'rgba(255,255,255,0)');
      g.fillStyle = gradient;
      g.fillRect(0, 0, 64, 64);
    })
  );
}

/** A tileable normal map of small waves from a sum of sines, used when the water normals image does not load. */
export function waterNormalTexture(): CanvasTexture {
  const size = 256;
  const rand = lehmer(7);
  const waves = Array.from({ length: 18 }, (_, i) => ({
    kx: Math.round((rand() - 0.5) * 16),
    ky: Math.round((rand() - 0.5) * 16),
    a: 0.6 / (1 + i * 0.35),
    p: rand() * Math.PI * 2
  }));
  return repeating(
    canvas(size, size, g => {
      const img = g.createImageData(size, size);
      const n = new Vector3();
      for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) {
          let dx = 0;
          let dy = 0;
          const u = (x / size) * Math.PI * 2;
          const v = (y / size) * Math.PI * 2;
          for (const w of waves) {
            const c = Math.cos(w.kx * u + w.ky * v + w.p) * w.a;
            dx += c * w.kx;
            dy += c * w.ky;
          }
          n.set(-dx * 0.05, -dy * 0.05, 1).normalize();
          const o = (y * size + x) * 4;
          img.data[o] = (n.x * 0.5 + 0.5) * 255;
          img.data[o + 1] = (n.y * 0.5 + 0.5) * 255;
          img.data[o + 2] = (n.z * 0.5 + 0.5) * 255;
          img.data[o + 3] = 255;
        }
      }
      g.putImageData(img, 0, 0);
    })
  );
}
