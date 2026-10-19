import { type PerspectiveCamera, Vector3 } from 'three';
import { type LabelValue, labelRank, plainName } from './labelText';

export interface Label {
  readonly id: string;
  readonly kind: string;
  readonly pick: string | null;
  readonly el: HTMLDivElement;
  readonly value: HTMLSpanElement;
  readonly anchor: Vector3;
  text: string;
  state: string;
  shown: boolean | null;
  size: { w: number; h: number } | null;
  sizedText: string | null;
  lift: number | null;
}

interface Box {
  readonly x0: number;
  readonly x1: number;
  readonly y0: number;
  readonly y1: number;
}

const MARGIN = 6;
const FAR = 170;
const GLIDE = 0.3;

/** The HTML labels over the scene: a name and a live value each, kept on screen and out of each other's way. */
export class Labels {
  private readonly layer: HTMLDivElement;
  private readonly items: Label[] = [];
  private readonly v = new Vector3();

  constructor(
    private readonly host: HTMLElement,
    private readonly onPick: (id: string | null) => void
  ) {
    this.layer = document.createElement('div');
    this.layer.className = 'live-labels';
    host.appendChild(this.layer);
  }

  /** A label `height` above `anchor`; `classes` style it (the first one is its kind), a click picks `pick`. */
  add(id: string, anchor: Vector3, height: number, classes: string, pick: string | null = id): Label {
    const el = document.createElement('div');
    el.className = `l3d ${classes}`;
    const name = document.createElement('span');
    name.className = 'l3d-name';
    name.textContent = plainName(id);
    const value = document.createElement('span');
    value.className = 'l3d-value';
    el.append(name, value);
    el.addEventListener('click', e => {
      e.stopPropagation();
      this.onPick(pick);
    });
    this.layer.appendChild(el);
    const item: Label = {
      id,
      kind: classes.split(' ')[0] ?? classes,
      pick,
      el,
      value,
      anchor: anchor.clone().setY(anchor.y + height),
      text: '',
      state: '',
      shown: null,
      size: null,
      sizedText: null,
      lift: null
    };
    this.items.push(item);
    return item;
  }

  /** Shows `text` as the label's value and `state` as its data-state, touching the DOM only on a change. */
  set(item: Label, { text, state }: LabelValue): void {
    if (item.text !== text) {
      item.value.textContent = text;
      item.text = text;
    }
    if (item.state !== state) {
      item.el.dataset['state'] = state;
      item.state = state;
    }
  }

  /**
   * Places every label over its anchor. Gate labels show only in alarm or when selected; a label
   * that would cover a more important one moves up above it, gliding rather than jumping. Labels
   * follow their anchors exactly, at fractions of a pixel, so they do not step as the camera moves.
   */
  place(camera: PerspectiveCamera, selected: string | null): void {
    const w = this.host.clientWidth;
    const h = this.host.clientHeight;
    const v = this.v;
    const visible: { item: Label; x: number; y: number; rank: number }[] = [];
    for (const item of this.items) {
      v.copy(item.anchor).project(camera);
      const distance = camera.position.distanceTo(item.anchor);
      const wanted = item.kind !== 'gate' || item.state.startsWith('alarm') || item.pick === selected;
      if (!(wanted && v.z < 1 && Math.abs(v.x) < 1.1 && Math.abs(v.y) < 1.1 && distance < FAR)) {
        if (item.shown !== false) {
          item.el.style.display = 'none';
          item.shown = false;
        }
        continue;
      }
      if (item.shown !== true) {
        item.el.style.display = '';
        item.shown = true;
        item.size = null;
        item.lift = null;
      }
      if (!item.size || item.text !== item.sizedText) {
        item.size = { w: item.el.offsetWidth, h: item.el.offsetHeight };
        item.sizedText = item.text;
      }
      visible.push({
        item,
        x: (v.x * 0.5 + 0.5) * w,
        y: (-v.y * 0.5 + 0.5) * h,
        rank: labelRank(item.kind, item.state)
      });
    }
    visible.sort((a, b) => a.rank - b.rank || b.y - a.y);
    const placed: Box[] = [];
    for (const { item, x, y } of visible) {
      const { w: lw, h: lh } = item.size ?? { w: 0, h: 0 };
      const cx = Math.min(Math.max(x, lw / 2 + MARGIN), w - lw / 2 - MARGIN);
      let top = y - lh;
      for (let tries = 0; tries < 4; tries++) {
        const hit = placed.find(r => cx - lw / 2 < r.x1 && cx + lw / 2 > r.x0 && top < r.y1 && top + lh > r.y0);
        if (!hit) break;
        top = hit.y0 - lh - 2;
      }
      placed.push({ x0: cx - lw / 2, x1: cx + lw / 2, y0: top, y1: top + lh });
      const lift = top - (y - lh);
      item.lift = item.lift === null ? lift : item.lift + (lift - item.lift) * GLIDE;
      item.el.style.transform = `translate3d(${(cx - lw / 2).toFixed(2)}px, ${(y - lh + item.lift).toFixed(2)}px, 0)`;
      item.el.classList.toggle('selected', item.pick === selected);
    }
  }
}
