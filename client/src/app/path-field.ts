import { Component, ElementRef, OnDestroy, computed, effect, input, signal, viewChild } from '@angular/core';
import type { Group, PerspectiveCamera, Scene, Vector3, WebGLRenderer } from 'three';
import { fitResting } from './camera-fit';
import { installAccentMask } from './accent-mask';
import { AdaptiveQuality, webglAvailable } from './quality';
import { paintStill } from './scene-still';
import type { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { DensityPoint } from './models';

@Component({
  selector: 'app-path-field',
  template: `
    @if (live()) {
      <div class="field-bar">
        <button type="button" class="orbit-toggle" [attr.aria-pressed]="orbit()" (click)="toggleOrbit()">{{ orbit() ? 'Done' : 'Rotate' }}</button>
      </div>
    }
    <div class="path-field" [class.is-live]="live()" [class.is-orbit]="orbit()" [class.is-fallback]="still()" #frame>
      <svg #stillSvg class="field-still" role="img" aria-label="Still of the Monte Carlo paths from the resting camera" [attr.aria-hidden]="still() ? null : 'true'"></svg>
      <canvas #canvas aria-hidden="true" data-engine="three"></canvas>
    </div>
  `,
})
export class PathField implements OnDestroy {
  readonly paths = input.required<number[][]>();
  readonly density = input.required<DensityPoint[]>();
  readonly upper = input<number | null>(null);
  readonly lower = input<number | null>(null);
  readonly theme = input.required<string>();

  /** The WebGL view is up. With reduced motion it is the same scene at its resting camera, drawn only on change. */
  readonly live = signal(false);
  /** WebGL could not start: the still of the same scene shows. */
  readonly fallback = signal(false);
  /** The WebGL context was lost (iOS drops it in background tabs): the still shows until it is restored. */
  readonly lost = signal(false);
  /** The still (same scene, same camera, projected to SVG) is showing; the caption and controls follow it. */
  readonly still = computed(() => this.fallback() || this.lost());
  /** Drag-to-rotate is off by default so a swipe over the field scrolls the page; the Rotate button turns it on. */
  readonly orbit = signal(false);

  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly frame = viewChild<ElementRef<HTMLElement>>('frame');
  private readonly stillSvg = viewChild<ElementRef<SVGSVGElement>>('stillSvg');
  private three: typeof import('three') | null = null;
  private quality: AdaptiveQuality | null = null;
  private stillObserver: ResizeObserver | null = null;
  private readonly onLost = (event: Event): void => this.contextLost(event);
  private readonly onRestored = (): void => this.contextRestored();
  private readonly motion = window.matchMedia('(prefers-reduced-motion: reduce)');
  private readonly allowMotion = signal(!this.motion.matches);

  private renderer: WebGLRenderer | null = null;
  private scene: Scene | null = null;
  private camera: PerspectiveCamera | null = null;
  private controls: OrbitControls | null = null;
  private content: Group | null = null;
  private observer: IntersectionObserver | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private looping = false;
  private visible = false;
  private dead = false;
  private generation = 0;
  private dirty = true;
  private raf = 0;
  private fitPoints: Vector3[] = [];
  private rest: { azimuth: number; target: Vector3 } | null = null;

  constructor() {
    this.motion.addEventListener('change', this.onMotion);
    document.addEventListener('visibilitychange', this.onVisibility);
    effect(() => {
      const generation = ++this.generation;
      const canvas = this.canvas()?.nativeElement;
      const host = this.frame()?.nativeElement;
      const paths = this.paths();
      const density = this.density();
      const upper = this.upper();
      const lower = this.lower();
      const theme = this.theme();
      this.allowMotion();
      if (!canvas || !host || paths.length === 0 || !theme) {
        this.live.set(false);
        this.disposeView();
        return;
      }
      void this.mount(generation, canvas, host, paths, density, upper, lower);
    });
  }

  ngOnDestroy(): void {
    this.dead = true;
    this.generation += 1;
    this.motion.removeEventListener('change', this.onMotion);
    document.removeEventListener('visibilitychange', this.onVisibility);
    this.disposeView();
  }

  toggleOrbit(): void {
    const next = !this.orbit();
    this.orbit.set(next);
    if (this.controls) {
      this.controls.enabled = next;
      this.controls.autoRotate = next && this.allowMotion();
    }
    const canvas = this.canvas()?.nativeElement;
    if (canvas) {
      canvas.style.touchAction = next ? 'none' : 'pan-y';
    }
    this.dirty = true;
    this.syncLoop();
    if (!next) {
      // Rotate off: back to the resting camera (the R6 still pose), one frame, then idle
      this.resize();
    }
  }

  /** On demand: one frame on the next animation frame unless the Rotate loop is already running. */
  private request(): void {
    if (this.looping || this.raf) {
      return;
    }
    this.raf = requestAnimationFrame(() => {
      this.raf = 0;
      this.tick();
      if (this.dirty) {
        this.request();
      }
    });
  }

  private readonly onMotion = (): void => {
    this.allowMotion.set(!this.motion.matches);
  };

  private readonly onVisibility = (): void => {
    this.syncLoop();
  };

  private async mount(
    generation: number,
    canvas: HTMLCanvasElement,
    host: HTMLElement,
    paths: number[][],
    density: DensityPoint[],
    upper: number | null,
    lower: number | null,
  ): Promise<void> {
    let THREE: typeof import('three');
    let Orbit: typeof import('three/examples/jsm/controls/OrbitControls.js');
    try {
      [THREE, Orbit] = await Promise.all([
        import('three'),
        import('three/examples/jsm/controls/OrbitControls.js'),
      ]);
    } catch {
      this.fallback.set(true);
      return;
    }
    if (this.dead || generation !== this.generation) {
      return;
    }
    const spin = this.allowMotion();

    this.disposeView();
    this.three = THREE;

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(38, 1, 0.1, 40);
    camera.position.set(0.9, 0.6, 3.15);
    scene.add(new THREE.AmbientLight(0xffffff, 0.72));
    const key = new THREE.DirectionalLight(0xffffff, 1.15);
    key.position.set(3.2, 4.2, 2.4);
    scene.add(key);

    const group = new THREE.Group();
    scene.add(group);
    this.build(THREE, group, paths, density, upper, lower);
    // Subject points for the resting-camera fit (every vertex of the paths, density sheet, targets and grid).
    const points: Vector3[] = [];
    group.updateMatrixWorld(true);
    group.traverse((object) => {
      const geometry = (object as { geometry?: import('three').BufferGeometry }).geometry;
      const position = geometry?.getAttribute('position');
      if (!position) {
        return;
      }
      const step = Math.max(1, Math.floor(position.count / 400));
      for (let i = 0; i < position.count; i += step) {
        points.push(new THREE.Vector3().fromBufferAttribute(position, i).applyMatrix4(object.matrixWorld));
      }
    });
    this.fitPoints = points;
    const restTarget = new THREE.Vector3(0.1, -0.24, 0);
    this.rest = { azimuth: Math.atan2(camera.position.x - 0.1, camera.position.z), target: restTarget.clone() };
    this.scene = scene;
    this.camera = camera;
    this.content = group;

    let renderer: WebGLRenderer | null = null;
    try {
      if (webglAvailable()) {
        renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true, preserveDrawingBuffer: true });
        if (!renderer.getContext()) {
          renderer.dispose();
          renderer = null;
        }
      }
    } catch {
      renderer = null;
    }
    if (!renderer) {
      // WebGL could not start: the still is the same scene from the same resting camera.
      this.fallback.set(true);
      this.paintStill();
      this.stillObserver = new ResizeObserver(() => this.paintStill());
      this.stillObserver.observe(host);
      return;
    }
    const quality = new AdaptiveQuality(host, (ratio) => {
      renderer.setPixelRatio(ratio);
      renderer.setSize(Math.max(2, host.clientWidth), Math.max(2, host.clientHeight), false);
      this.dirty = true;
      if (!this.looping) {
        this.tick();
      }
    });
    this.quality = quality;
    renderer.setPixelRatio(quality.ratio());
    renderer.setClearColor(0x000000, 0);
    canvas.addEventListener('webglcontextlost', this.onLost);
    canvas.addEventListener('webglcontextrestored', this.onRestored);

    const controls = new Orbit.OrbitControls(camera, canvas);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;
    controls.enablePan = false;
    // MOTION 3.8: never auto-rotate on load. The slow turn (motion on) runs only while Rotate is pressed.
    controls.autoRotate = spin && this.orbit();
    controls.autoRotateSpeed = 0.55;
    controls.minDistance = 1.5;
    controls.maxDistance = 6;
    controls.target.copy(restTarget);
    controls.enabled = this.orbit();
    canvas.style.touchAction = this.orbit() ? 'none' : 'pan-y';
    controls.addEventListener('start', () => {
      controls.autoRotate = false;
    });
    controls.addEventListener('end', () => {
      controls.autoRotate = this.allowMotion() && this.orbit();
    });
    controls.addEventListener('change', () => {
      this.dirty = true;
      this.request();
    });

    this.renderer = renderer;
    this.controls = controls;
    installAccentMask(THREE, renderer, scene, camera);

    this.observer = new IntersectionObserver((entries) => {
      this.visible = entries.some((entry) => entry.isIntersecting);
      this.syncLoop();
    }, { threshold: 0.08 });
    this.observer.observe(host);
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(host);

    this.resize();
    quality.push();
    this.tick();
    if (this.dead || generation !== this.generation) {
      return;
    }
    this.live.set(true);
    this.fallback.set(false);
    this.syncLoop();
  }

  /** Paint the still: the same scene through the resting camera fitted to this frame. */
  private paintStill(): void {
    const THREE = this.three;
    const svg = this.stillSvg()?.nativeElement;
    const host = this.frame()?.nativeElement;
    if (!THREE || !svg || !host || !this.scene || !this.camera || !this.rest) {
      return;
    }
    const width = host.clientWidth;
    const height = host.clientHeight;
    if (width < 2 || height < 2) {
      return;
    }
    const camera = this.camera;
    camera.aspect = width / height;
    camera.updateProjectionMatrix();
    const target = this.rest.target.clone();
    fitResting(camera, target, this.fitPoints, { azimuth: this.rest.azimuth, minEl: 6, maxEl: 40, margin: 0.95 });
    paintStill(THREE, svg, this.scene, camera, width, height);
  }

  private contextLost(event: Event): void {
    event.preventDefault();
    this.renderer?.setAnimationLoop(null);
    this.looping = false;
    this.orbit.set(false);
    if (this.controls) {
      this.controls.enabled = false;
    }
    const canvas = this.canvas()?.nativeElement;
    if (canvas) {
      canvas.style.touchAction = 'pan-y';
    }
    this.lost.set(true);
    this.live.set(false);
    this.paintStill();
    if (this.controls && this.rest) {
      this.controls.target.copy(this.rest.target);
    }
  }

  private contextRestored(): void {
    this.lost.set(false);
    this.live.set(true);
    this.dirty = true;
    this.resize();
    this.tick();
    this.syncLoop();
  }

  private build(
    THREE: typeof import('three'),
    group: Group,
    paths: number[][],
    density: DensityPoint[],
    upper: number | null,
    lower: number | null,
  ): void {
    const samples: number[] = [];
    for (const path of paths) {
      samples.push(...path);
    }
    for (const point of density) {
      samples.push(point.price);
    }
    if (upper != null) {
      samples.push(upper);
    }
    if (lower != null) {
      samples.push(lower);
    }
    const min = Math.min(...samples);
    const max = Math.max(...samples);
    const span = Math.max(max - min, 1e-6);
    const yOf = (price: number) => ((price - min) / span) * 1.6 - 0.8;
    const lime = new THREE.Color(cssColor('--chart', '#4f7a00'));
    const down = new THREE.Color(cssColor('--down', '#8e3535'));
    // Grid floor at 3:1 or better on the page: #8c8c86 on #f5f5f3 (3.2:1), #6b6b6b on #111111 (3.5:1).
    const gridColor = new THREE.Color(this.theme() === 'dark' ? 0x6b6b6b : 0x8c8c86);

    paths.forEach((path, index) => {
      const z = paths.length <= 1 ? 0 : (index / (paths.length - 1) - 0.5) * 1.7;
      const points = path.map((price, step) => {
        const x = path.length <= 1 ? 0 : (step / (path.length - 1) - 0.5) * 2.4;
        return new THREE.Vector3(x, yOf(price), z);
      });
      const geometry = new THREE.BufferGeometry().setFromPoints(points);
      const material = new THREE.LineBasicMaterial({
        color: lime,
        transparent: true,
        opacity: 0.34 + (index / Math.max(paths.length - 1, 1)) * 0.62,
      });
      material.userData['token'] = 'accent';
      group.add(new THREE.Line(geometry, material));
    });

    if (density.length > 1) {
      const peak = Math.max(...density.map((point) => point.density), 1e-6);
      const positions: number[] = [];
      const indices: number[] = [];
      density.forEach((point, index) => {
        const depth = (point.density / peak) * 0.72;
        positions.push(1.24, yOf(point.price), -depth, 1.24, yOf(point.price), depth);
        if (index < density.length - 1) {
          const base = index * 2;
          indices.push(base, base + 1, base + 2, base + 1, base + 3, base + 2);
        }
      });
      const geometry = new THREE.BufferGeometry();
      geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
      geometry.setIndex(indices);
      geometry.computeVertexNormals();
      const sheet = new THREE.MeshStandardMaterial({
        color: lime,
        emissive: lime,
        emissiveIntensity: 0.22,
        metalness: 0.06,
        roughness: 0.4,
        transparent: true,
        opacity: 0.42,
        side: THREE.DoubleSide,
      });
      sheet.userData['token'] = 'accent';
      group.add(new THREE.Mesh(geometry, sheet));
    }

    const barrier = (price: number, color: import('three').Color) => {
      const y = yOf(price);
      const geometry = new THREE.BufferGeometry().setFromPoints([
        new THREE.Vector3(-1.25, y, -0.72),
        new THREE.Vector3(1.25, y, -0.72),
        new THREE.Vector3(1.25, y, 0.72),
        new THREE.Vector3(-1.25, y, 0.72),
        new THREE.Vector3(-1.25, y, -0.72),
      ]);
      const line = new THREE.LineBasicMaterial({ color, transparent: true, opacity: 0.75 });
      if (color === lime) {
        line.userData['token'] = 'accent';
      }
      group.add(new THREE.Line(geometry, line));
    };
    if (upper != null) {
      barrier(upper, lime);
    }
    if (lower != null) {
      barrier(lower, down);
    }

    // White vertex colors so the material color is the grid color on screen (opaque, no blending toward the page).
    const helper = new THREE.GridHelper(2.6, 8, 0xffffff, 0xffffff);
    helper.position.y = -0.84;
    const materials = Array.isArray(helper.material) ? helper.material : [helper.material];
    for (const material of materials) {
      (material as import('three').LineBasicMaterial).color.copy(gridColor);
    }
    group.add(helper);
  }

  private resize(): void {
    const host = this.frame()?.nativeElement;
    const renderer = this.renderer;
    const camera = this.camera;
    if (!host || !renderer || !camera) {
      return;
    }
    const width = host.clientWidth;
    const height = host.clientHeight;
    if (width < 2 || height < 2) {
      return;
    }
    renderer.setPixelRatio(this.quality?.ratio() ?? Math.min(window.devicePixelRatio || 1, 2));
    renderer.setSize(width, height, false);
    camera.aspect = width / height;
    camera.updateProjectionMatrix();
    if (this.rest && this.controls && !this.controls.enabled) {
      // back to the resting camera, fitted to this frame
      this.controls.target.copy(this.rest.target);
      fitResting(camera, this.controls.target, this.fitPoints, { azimuth: this.rest.azimuth, minEl: 6, maxEl: 40, margin: 0.95 });
      // keep the zoom range around the fitted distance so the controls do not clamp the resting camera
      const fitted = camera.position.distanceTo(this.controls.target);
      this.controls.maxDistance = Math.max(6, fitted * 1.6);
      this.controls.minDistance = Math.min(1.5, fitted * 0.5);
      this.controls.update();
    }
    this.dirty = true;
    if (!this.looping) {
      this.tick();
    }
  }

  private tick(): void {
    if (this.lost()) {
      return;
    }
    if (this.controls?.update()) {
      this.dirty = true;
    }
    if (!this.dirty) {
      this.quality?.idle();
      return;
    }
    if (this.looping) {
      this.quality?.frame();
    }
    this.dirty = false;
    if (this.renderer && this.scene && this.camera) {
      this.renderer.render(this.scene, this.camera);
      const host = this.frame()?.nativeElement;
      if (host && !this.orbit()) {
        host.dataset['3d'] = 'settled';
      } else if (host) {
        host.dataset['3d'] = 'orbit';
      }
    }
  }

  private syncLoop(): void {
    const renderer = this.renderer;
    if (!renderer) {
      return;
    }
    // No loop at rest in either mode (render on demand); the loop runs only while Rotate is pressed.
    const run = this.visible && document.visibilityState !== 'hidden' && this.live() && this.orbit();
    if (run && !this.looping) {
      renderer.setAnimationLoop(() => this.tick());
      this.looping = true;
    } else if (!run && this.looping) {
      renderer.setAnimationLoop(null);
      this.looping = false;
    }
  }

  private disposeView(): void {
    cancelAnimationFrame(this.raf);
    this.raf = 0;
    const canvas = this.canvas()?.nativeElement;
    canvas?.removeEventListener('webglcontextlost', this.onLost);
    canvas?.removeEventListener('webglcontextrestored', this.onRestored);
    this.stillObserver?.disconnect();
    this.stillObserver = null;
    this.quality = null;
    this.lost.set(false);
    this.renderer?.setAnimationLoop(null);
    this.looping = false;
    this.observer?.disconnect();
    this.resizeObserver?.disconnect();
    this.observer = null;
    this.resizeObserver = null;
    this.controls?.dispose();
    this.content?.traverse((object) => {
      const mesh = object as { geometry?: { dispose(): void }; material?: { dispose(): void } | Array<{ dispose(): void }> };
      mesh.geometry?.dispose();
      if (Array.isArray(mesh.material)) {
        mesh.material.forEach((item) => item.dispose());
      } else {
        mesh.material?.dispose();
      }
    });
    this.scene?.clear();
    this.renderer?.dispose();
    this.renderer = null;
    this.scene = null;
    this.camera = null;
    this.controls = null;
    this.content = null;
    this.visible = false;
  }
}

function cssColor(name: string, fallback: string): string {
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || fallback;
}
