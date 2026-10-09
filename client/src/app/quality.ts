/**
 * Adaptive quality for the 3D views (rubric v2.2 3D budget): frame time p95 at most 22.2 ms on desktop and 33.3 ms on a
 * phone. Frames are timed only while the view is animating (spin, damping or a drag). When the p95 of the last 60 frames
 * is over budget the pixel ratio steps down (cap, 1.25, 1; never below 1); after 180 frames well under budget it steps back up.
 * Phone frames (under 600px) cap the pixel ratio at 1.5, desktop at 2.
 */
export class AdaptiveQuality {
  private readonly times: number[] = [];
  private last = 0;
  private level = 0;
  private calm = 0;
  p95 = 0;

  constructor(
    private readonly host: HTMLElement,
    private readonly apply: (pixelRatio: number) => void,
  ) {}

  phone(): boolean {
    return window.innerWidth < 600 || window.matchMedia('(pointer: coarse)').matches;
  }

  budget(): number {
    return this.phone() ? 33.3 : 22.2;
  }

  cap(): number {
    return Math.min(window.devicePixelRatio || 1, window.innerWidth < 600 ? 1.5 : 2);
  }

  /** The pixel ratio for the current level. */
  ratio(): number {
    const steps = this.steps();
    return steps[Math.min(this.level, steps.length - 1)];
  }

  private steps(): number[] {
    return [this.cap(), 1.25, 1].filter((v, i, all) => i === 0 || v < all[0]);
  }

  /** Call once per rendered animation frame. */
  frame(now = performance.now()): void {
    if (this.last && now - this.last < 250) {
      this.times.push(now - this.last);
      if (this.times.length > 60) this.times.shift();
    }
    this.last = now;
    if (this.times.length < 30) return;
    const sorted = [...this.times].sort((x, y) => x - y);
    this.p95 = sorted[Math.floor(sorted.length * 0.95)];
    this.host.dataset['p95'] = this.p95.toFixed(1);
    const budget = this.budget();
    if (this.p95 > budget && this.level < this.steps().length - 1) {
      this.level++;
      this.calm = 0;
      this.times.length = 0;
      this.push();
    } else if (this.p95 < budget * 0.6 && this.level > 0) {
      if (++this.calm > 180) {
        this.level--;
        this.calm = 0;
        this.times.length = 0;
        this.push();
      }
    } else this.calm = 0;
  }

  /** The animation stopped: the next frame starts a new run of timings. */
  idle(): void {
    this.last = 0;
  }

  push(): void {
    const r = this.ratio();
    this.host.dataset['quality'] = String(this.level);
    this.host.dataset['pixelRatio'] = r.toFixed(2);
    this.apply(r);
  }
}

/** Quiet WebGL probe (three logs console errors when it cannot create a context); the probe context is released. */
export function webglAvailable(): boolean {
  try {
    const probe = document.createElement('canvas');
    const gl = (probe.getContext('webgl2') || probe.getContext('webgl')) as WebGLRenderingContext | null;
    if (!gl) return false;
    gl.getExtension('WEBGL_lose_context')?.loseContext();
    return true;
  } catch {
    return false;
  }
}
