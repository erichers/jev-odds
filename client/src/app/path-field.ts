import { Component, ElementRef, OnDestroy, computed, effect, input, signal, viewChild } from '@angular/core';
import type { Group, PerspectiveCamera, Scene, WebGLRenderer } from 'three';
import type { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { DensityPoint } from './models';

@Component({
  selector: 'app-path-field',
  template: `
    <div class="path-field" [class.is-live]="live()" #frame>
      <svg class="field-still" viewBox="0 0 680 280" role="img" aria-label="Still drawing of the Monte Carlo paths">
        @for (line of lines(); track $index) {
          <polyline [attr.points]="line" [attr.opacity]="opacity($index)" />
        }
      </svg>
      <canvas #canvas aria-hidden="true"></canvas>
    </div>
  `,
})
export class PathField implements OnDestroy {
  readonly paths = input.required<number[][]>();
  readonly density = input.required<DensityPoint[]>();
  readonly upper = input<number | null>(null);
  readonly lower = input<number | null>(null);
  readonly theme = input.required<string>();

  readonly live = signal(false);
  readonly lines = computed(() => fieldLines(this.paths()));

  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly frame = viewChild<ElementRef<HTMLElement>>('frame');
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
      const allow = this.allowMotion();
      if (!canvas || !host || paths.length === 0 || !theme || !allow) {
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

  opacity(index: number): number {
    const count = this.lines().length;
    if (count <= 1) {
      return 0.9;
    }
    return 0.28 + (index / (count - 1)) * 0.7;
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
      return;
    }
    if (this.dead || generation !== this.generation || !this.allowMotion()) {
      return;
    }

    this.disposeView();

    let renderer: WebGLRenderer;
    try {
      renderer = new THREE.WebGLRenderer({
        canvas,
        alpha: true,
        antialias: true,
        preserveDrawingBuffer: true,
      });
    } catch {
      return;
    }
    if (!renderer.getContext()) {
      renderer.dispose();
      return;
    }
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setClearColor(0x000000, 0);

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(38, 1, 0.1, 40);
    camera.position.set(0.35, 1.15, 3.15);

    const controls = new Orbit.OrbitControls(camera, canvas);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;
    controls.enablePan = false;
    controls.autoRotate = true;
    controls.autoRotateSpeed = 0.55;
    controls.minDistance = 1.5;
    controls.maxDistance = 6;
    controls.target.set(0, 0.02, 0);
    controls.addEventListener('start', () => {
      controls.autoRotate = false;
    });
    controls.addEventListener('end', () => {
      if (this.allowMotion()) {
        controls.autoRotate = true;
      }
    });

    scene.add(new THREE.AmbientLight(0xffffff, 0.72));
    const key = new THREE.DirectionalLight(0xffffff, 1.15);
    key.position.set(3.2, 4.2, 2.4);
    scene.add(key);

    const group = new THREE.Group();
    scene.add(group);
    this.build(THREE, group, paths, density, upper, lower);

    this.renderer = renderer;
    this.scene = scene;
    this.camera = camera;
    this.controls = controls;
    this.content = group;

    this.observer = new IntersectionObserver((entries) => {
      this.visible = entries.some((entry) => entry.isIntersecting);
      this.syncLoop();
    }, { threshold: 0.08 });
    this.observer.observe(host);
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(host);

    this.resize();
    this.tick();
    if (this.dead || generation !== this.generation) {
      return;
    }
    this.live.set(true);
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
    const gridColor = new THREE.Color(cssColor('--muted', '#8a8a8a'));

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
      group.add(new THREE.Mesh(geometry, new THREE.MeshStandardMaterial({
        color: lime,
        emissive: lime,
        emissiveIntensity: 0.22,
        metalness: 0.06,
        roughness: 0.4,
        transparent: true,
        opacity: 0.42,
        side: THREE.DoubleSide,
      })));
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
      group.add(new THREE.Line(geometry, new THREE.LineBasicMaterial({ color, transparent: true, opacity: 0.75 })));
    };
    if (upper != null) {
      barrier(upper, lime);
    }
    if (lower != null) {
      barrier(lower, down);
    }

    const helper = new THREE.GridHelper(2.6, 8, gridColor, gridColor);
    helper.position.y = -0.84;
    const materials = Array.isArray(helper.material) ? helper.material : [helper.material];
    for (const material of materials) {
      material.transparent = true;
      material.opacity = 0.35;
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
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setSize(width, height, false);
    camera.aspect = width / height;
    camera.updateProjectionMatrix();
  }

  private tick(): void {
    this.controls?.update();
    if (this.renderer && this.scene && this.camera) {
      this.renderer.render(this.scene, this.camera);
    }
  }

  private syncLoop(): void {
    const renderer = this.renderer;
    if (!renderer) {
      return;
    }
    const run = this.visible && document.visibilityState !== 'hidden' && this.allowMotion() && this.live();
    if (run && !this.looping) {
      renderer.setAnimationLoop(() => this.tick());
      this.looping = true;
    } else if (!run && this.looping) {
      renderer.setAnimationLoop(null);
      this.looping = false;
    }
  }

  private disposeView(): void {
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

function fieldLines(paths: number[][]): string[] {
  let min = Infinity;
  let max = -Infinity;
  for (const path of paths) {
    for (const price of path) {
      if (price < min) {
        min = price;
      }
      if (price > max) {
        max = price;
      }
    }
  }
  if (!Number.isFinite(min) || !Number.isFinite(max)) {
    return [];
  }
  const span = Math.max(max - min, 1e-6);
  return paths.map((path, index) => {
    const depth = paths.length <= 1 ? 0 : index / (paths.length - 1);
    return path.map((price, step) => {
      const along = path.length <= 1 ? 0 : step / (path.length - 1);
      const x = 28 + along * 560 + depth * 42;
      const y = 22 + (1 - (price - min) / span) * 214 + depth * 18;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    }).join(' ');
  });
}

function cssColor(name: string, fallback: string): string {
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || fallback;
}
