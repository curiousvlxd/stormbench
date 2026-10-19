import { Clock, type Material, type Mesh, type Object3D, type PerspectiveCamera, type Scene, Vector3 } from 'three';
import type { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import type { Assets } from './assets/load';
import { createPalette } from './assets/materials';
import { glowTexture, stripeTexture, waterNormalTexture } from './assets/textures';
import { type Anchor, CameraDirector, type Tween } from './camera/CameraDirector';
import { TANK_CLEAR, TANK_R } from './config';
import { installFilmMode } from './film/FilmMode';
import { townRandom } from './math/random';
import { Effects } from './render/Effects';
import { Stage } from './render/Stage';
import { Xray } from './render/Xray';
import type { SceneContext } from './scene/context';
import { ControlRoom } from './scene/ControlRoom';
import { Gates } from './scene/Gates';
import { buildGround } from './scene/Ground';
import { buildPlaces } from './scene/Places';
import { Outfalls } from './scene/Outfalls';
import { Plant } from './scene/Plant';
import { Rivers } from './scene/Rivers';
import { buildManholes } from './scene/Sewers';
import { Sky } from './scene/Sky';
import { Streets } from './scene/Streets';
import { Tanks } from './scene/Tanks';
import { buildTown } from './scene/Town';
import { Weather } from './scene/Weather';
import { Weirs } from './scene/Weirs';
import type { DotNetObject, LiveFrame, NetworkDescription } from './types';
import { Labels } from './ui/Labels';
import { Picking } from './ui/Picking';
import { Selection } from './ui/Selection';
import { type Circle, Obstacles, type Segment } from './world/Obstacles';
import type { WaterHit } from './world/water';
import { WaterSurfaces } from './world/WaterSurfaces';
import { World } from './world/World';
import { StreetLights } from './scene/StreetLights';
import { daylight, lampsOn, twilight } from './world/daylight';

/** The hour the town shows before any storm has run, and how fast the light follows the clock (per second). */
const NOON = 12;
const DAY_EASE = 1.5;

const MAX_STEP = 0.1;

/**
 * The Live 3D view: builds the town from the network, takes each live frame from the page,
 * animates and renders it every animation frame, and flies the camera to what is picked.
 */
export class LiveScene {
  readonly scene: Scene;
  readonly camera: PerspectiveCamera;
  readonly controls: OrbitControls;
  time = 0;
  frame: LiveFrame | null = null;
  selected: string | null = null;

  readonly #host: HTMLElement;
  readonly #dotnet: DotNetObject;
  readonly #film: boolean;
  readonly #clock = new Clock();
  readonly #stage: Stage;
  readonly #effects: Effects;
  readonly #labels: Labels;
  readonly #picking: Picking;
  readonly #director: CameraDirector;
  readonly #world: World;
  readonly #surfaces = new WaterSurfaces();
  readonly #sky: Sky;
  readonly #rivers: Rivers;
  readonly #xray: Xray;
  readonly #tanks: Tanks;
  readonly #gates: Gates;
  readonly #weirs: Weirs;
  readonly #outfalls: Outfalls;
  readonly #plant: Plant;
  readonly #controlRoom: ControlRoom;
  readonly #weather: Weather;
  readonly #selection: Selection;
  readonly #resizeObserver: ResizeObserver;
  #manual = false;
  #dry = false;
  #day = 1;
  #warm = 0;
  readonly #streetLights: StreetLights | null;
  #disposed = false;
  #raf = 0;

  constructor(
    host: HTMLElement,
    readonly network: NetworkDescription,
    dotnet: DotNetObject,
    assets: Assets,
    film: boolean
  ) {
    this.#host = host;
    this.#dotnet = dotnet;
    this.#film = film;
    const rand = townRandom();
    const palette = createPalette();
    const textures = {
      waterNormals: assets.water ?? waterNormalTexture(),
      glow: glowTexture(),
      stripes: stripeTexture()
    };

    this.#stage = new Stage(host, film);
    this.scene = this.#stage.scene;
    this.camera = this.#stage.camera;
    this.controls = this.#stage.controls;
    this.#labels = new Labels(host, id => this.pick(id));
    this.#xray = new Xray(this.#stage.fog, this.#stage.sunDir, this.scene.environment);
    this.#effects = new Effects(this.#stage.renderer, this.scene, this.camera, this.#xray);
    this.#director = new CameraDirector(this.camera, this.controls, film);
    this.#picking = new Picking(
      this.#stage.renderer.domElement,
      this.camera,
      id => this.pick(id),
      () => this.#director.touch()
    );

    const world = new World(network);
    this.#world = world;
    const ctx: SceneContext = {
      scene: this.scene,
      xray: this.#xray,
      world,
      palette,
      textures,
      kits: assets.kits,
      structures: assets.structures,
      labels: this.#labels,
      picking: this.#picking,
      surfaces: this.#surfaces,
      rand
    };
    this.#sky = new Sky(this.scene, world.bounds, rand);
    buildGround(this.scene, world);
    this.#rivers = new Rivers(this.scene, world.waters, this.#stage.sunDir, this.#picking);
    buildPlaces(ctx);
    this.#tanks = new Tanks(ctx);
    this.#gates = new Gates(ctx, this.#tanks.entries);
    this.#weirs = new Weirs(ctx);
    this.#plant = new Plant(ctx);
    this.#outfalls = new Outfalls(ctx, this.#plant.outlet);
    this.#controlRoom = new ControlRoom(ctx, this.#gates.entries);
    const obstacles = this.obstacles();
    const streets = new Streets(ctx, obstacles);
    this.#streetLights = streets.lights;
    buildManholes(ctx, streets.straights);
    buildTown(ctx, obstacles, streets.runs, streets.roads);
    this.#weather = new Weather(this.scene, world.bounds, textures.glow, rand);
    this.#selection = new Selection(this.scene);
    this.overview(true);

    this.#resizeObserver = new ResizeObserver(() => this.resize());
    this.#resizeObserver.observe(host);
    this.resize();
    this.loop();
    if (film) installFilmMode(this);
  }

  get tanks() {
    return this.#tanks.entries;
  }

  get gates() {
    return this.#gates.entries;
  }

  get outfalls() {
    return this.#outfalls.entries;
  }

  get pos(): Map<string, Vector3> {
    return this.#world.pos;
  }

  get tween(): Tween | null {
    return this.#director.tween;
  }

  set tween(tween: Tween | null) {
    this.#director.tween = tween;
  }

  /** The nearest point on the bank of any water. */
  nearestWater(q: Vector3): WaterHit {
    return this.#world.nearestWater(q);
  }

  /** Flies the camera to look at `target` from `position`. */
  flyTo(target: Vector3, position: Vector3): void {
    this.#director.flyTo(target, position);
  }

  /** Takes a live frame from the page: levels, gates, spills, rain, the link and the PLC. */
  update(frame: LiveFrame): void {
    if (this.#dry) return;
    this.frame = frame;
    this.#tanks.update(frame);
    this.#gates.update(frame);
    this.#weirs.update(frame);
    this.#outfalls.update(frame);
    this.#controlRoom.update(frame);
  }

  /** Selects a structure by id and flies to it; null clears the selection. */
  focus(id: string | null): void {
    this.selected = id;
    const anchor = id ? this.anchorOf(id) : null;
    if (anchor && anchor.r > 0 && !this.#film) this.#selection.show(anchor.p, anchor.r);
    else this.#selection.hide();
    if (anchor) this.#director.focus(anchor);
  }

  /** Back to the whole town, at once or in a flight. */
  overview(instant = false): void {
    this.selected = null;
    this.#selection.hide();
    this.#director.overview(this.#world.bounds, instant);
  }

  /** Advances everything by dt seconds and renders a frame. */
  tick(dt: number): void {
    this.time += dt;
    const t = this.time;
    const f = this.frame;
    const running = !!f?.running;
    const rain = f?.rain ?? 0;
    const k = 1 - Math.exp(-dt * 5);
    const hour = f?.hour ?? NOON;
    this.#day += (daylight(hour) - this.#day) * (1 - Math.exp(-dt * DAY_EASE));
    this.#warm += (twilight(hour) - this.#warm) * (1 - Math.exp(-dt * DAY_EASE));
    const day = this.#day;

    this.#director.tick(dt, running);
    const storm = this.#weather.tick(dt, t, rain, this.#stage, day, this.#warm);
    this.#sky.tick(dt, storm, day, this.#warm);
    this.#streetLights?.light(lampsOn(day));
    this.#xray.light(day);
    this.#surfaces.tick(t);
    this.#tanks.tick(t, k, this.camera);
    this.#weirs.tick(t);
    this.#gates.tick(t, k);
    this.#plant.tick(dt, t, running);
    this.#rivers.tick(t, this.#stage.fog, 0.32 + 0.68 * day);
    this.#outfalls.tick(t, k, this.#rivers);
    this.#controlRoom.tick(dt, t, f);
    this.#selection.tick(t);

    this.#effects.render();
    this.#labels.place(this.camera, this.selected);
  }

  /** Film: the clock stops and the recording advances it frame by frame. */
  setManual(on: boolean): void {
    this.#manual = on;
    this.#clock.getDelta();
  }

  /** Film: start low over the river, rise to the whole town. */
  openingShot(): void {
    const w = this.#world.waters[0];
    if (!w) return;
    this.#director.opening(w.pts[Math.floor(w.pts.length * 0.3)]!, this.#world.bounds);
  }

  /** Film: straight down on the town, held until the reveal. */
  topDown(): void {
    this.#director.topDown(this.#world.bounds);
  }

  /** Film: let go of the top-down view and fly to the overview. */
  reveal(): void {
    this.#director.filmHold = false;
    this.overview();
  }

  /** Film: a dry town (empty tanks, no spills, no alarms) that ignores the live frames while on. */
  dry(on: boolean): void {
    this.#dry = on;
    if (!on) return;
    this.frame = null;
    for (const tank of this.#tanks.entries.values()) {
      Object.assign(tank, { target: 0, fill: 0, sensorFill: null, overflowing: false, alarm: null });
    }
    for (const gate of this.#gates.entries.values()) if (gate.controlled) gate.alarm = null;
    for (const o of this.#outfalls.entries.values()) Object.assign(o, { amount: 0, active: 0 });
  }

  /** Film: where the rain gauges are on screen, in CSS pixels. */
  projectGauges(): { id: string; x: number; y: number }[] {
    const r = this.#stage.renderer.domElement.getBoundingClientRect();
    return this.#world.gauges.map(g => {
      const v = g.at.clone().project(this.camera);
      return { id: g.id, x: ((v.x + 1) / 2) * r.width, y: ((1 - v.y) / 2) * r.height };
    });
  }

  /** Film: the place names and the subcatchments with where they are. */
  placeList(): { name: string; at: number[] }[] {
    const world = this.#world;
    return world.places
      .map(pl => ({ name: pl.name, at: pl.at.toArray() }))
      .concat(
        world.catchments.map(c => ({
          name: c.id,
          at: c.poly
            .reduce((a, q) => a.add(q), new Vector3())
            .divideScalar(c.poly.length)
            .toArray()
        }))
      );
  }

  dispose(): void {
    this.#disposed = true;
    cancelAnimationFrame(this.#raf);
    this.#resizeObserver.disconnect();
    this.#picking.dispose();
    this.controls.dispose();
    const release = (o: Object3D) => {
      const mesh = o as Partial<Mesh>;
      mesh.geometry?.dispose();
      const m = mesh.material as Material | Material[] | undefined;
      if (Array.isArray(m)) m.forEach(x => x.dispose());
      else m?.dispose();
    };
    this.scene.traverse(release);
    this.#xray.scene.traverse(release);
    this.#effects.dispose();
    this.#stage.renderer.dispose();
    this.#host.replaceChildren();
  }

  /** Tells the page what was picked (a structure id, or null for nothing). */
  private pick(id: string | null): void {
    this.#director.touch();
    this.#dotnet.invokeMethodAsync('Pick', id).catch(() => {});
  }

  private readonly loop = (): void => {
    if (this.#disposed) return;
    this.#raf = requestAnimationFrame(this.loop);
    if (!this.#manual) this.tick(Math.min(this.#clock.getDelta(), MAX_STEP));
  };

  private resize(): void {
    const w = this.#host.clientWidth || 1;
    const h = this.#host.clientHeight || 1;
    this.#stage.resize(w, h);
    this.#effects.resize(w, h);
  }

  /** Where the camera goes for a structure, how wide its ring is, and how far to stand back. */
  private anchorOf(id: string): Anchor | null {
    const tank = this.#tanks.entries.get(id);
    if (tank) return { p: tank.group.position, r: TANK_R + 1.4, d: 24 };
    const gate = this.#gates.entries.get(id);
    if (gate) return { p: gate.group.position, r: 1.6, d: 20 };
    const weir = this.#weirs.entries.get(id);
    if (weir) return { p: weir.group.position, r: 1.8, d: 14 };
    if (id === 'wwtp' && this.#plant.centre) return { p: this.#plant.centre, r: 8.5, d: 32 };
    if (id === 'plc') return { p: this.#controlRoom.plcAt, r: 1.6, d: 18 };
    if (id === 'center') return { p: this.#controlRoom.roomAt, r: 4, d: 26 };
    const river = this.#world.waters[0];
    if (id === 'river' && river) return { p: river.pts[Math.floor(river.pts.length / 2)]!, r: 0, d: 60 };
    return null;
  }

  /** What houses, trees and streets keep clear of: structures as circles, the sewers under the streets as segments. */
  private obstacles(): Obstacles {
    const world = this.#world;
    const net = world.network;
    const circle = (p: Vector3 | undefined, r: number): Circle[] => (p ? [[p, r]] : []);
    const circles: Circle[] = [
      ...net.tanks.flatMap(id => circle(world.pos.get(id), TANK_CLEAR)),
      ...net.overflowPoints.filter(id => !world.tankSet.has(id)).flatMap(id => circle(world.pos.get(id), 1.8)),
      ...[...this.#gates.entries.values()].map((g): Circle => [g.group.position, 1.6]),
      [this.#controlRoom.plcAt, 1.8],
      [this.#controlRoom.roomAt, 4.2]
    ];
    const footprint = this.#plant.footprint;
    if (footprint) circles.push([footprint.center, footprint.radius]);
    const segments: Segment[] = net.links.flatMap(l => {
      const a = world.pos.get(l.from);
      const b = world.pos.get(l.to);
      return a && b ? [[a, b, 0.6] as const] : [];
    });
    for (const o of this.#outfalls.entries.values()) circles.push([o.at, 1.4]);
    for (const g of world.gauges) circles.push([g.at, 1.0]);
    return new Obstacles(circles, segments, world.waters);
  }
}
