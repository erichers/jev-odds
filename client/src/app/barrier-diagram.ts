import { Component, DestroyRef, ElementRef, afterNextRender, inject, signal } from '@angular/core';

@Component({
  selector: 'app-barrier-diagram',
  template: `
    <svg viewBox="0 0 640 228" role="img" aria-labelledby="barrier-title barrier-desc">
      <title id="barrier-title">One price path against a barrier</title>
      <desc id="barrier-desc">
        The path starts at the spot, crosses the barrier, and finishes beyond it. Crossing is a touch. The finish is the close.
      </desc>
      <line class="diagram-axis" x1="28" y1="196" x2="616" y2="196" />
      <line class="diagram-barrier" x1="28" y1="92" x2="616" y2="92" />
      <text class="diagram-label" [attr.transform]="at(28, 84)">Barrier</text>
      <polyline
        class="diagram-path"
        pathLength="1"
        points="36,170 96,164 150,146 198,122 248,92 292,74 338,64 392,78 446,58 508,46 608,34"
      />
      <circle class="diagram-spot" cx="36" cy="170" r="4" />
      <text class="diagram-label" [attr.transform]="at(28, 214)">Spot</text>
      <circle class="diagram-mark" cx="248" cy="92" r="4.5" />
      <text class="diagram-label" [attr.transform]="at(236, 78)" text-anchor="end">Touch</text>
      <circle class="diagram-mark diagram-close" cx="608" cy="34" r="4.5" />
      <text class="diagram-label diagram-close-label" [attr.transform]="at(596, 22)" text-anchor="end">Close</text>
    </svg>
  `,
})
export class BarrierDiagram {
  /** viewBox units per CSS px. Labels are 14px text scaled by k about their anchor, so they draw at 14px at any width
   *  and their computed font-size stays 14px (on the type scale). */
  readonly k = signal(1);
  at(x: number, y: number): string { return `translate(${x} ${y}) scale(${this.k()})`; }

  constructor() {
    const host = inject(ElementRef<HTMLElement>).nativeElement as HTMLElement;
    const destroy = inject(DestroyRef);
    afterNextRender(() => {
      const fit = () => this.k.set(640 / Math.max(1, host.clientWidth || 640));
      fit();
      const ro = new ResizeObserver(fit);
      ro.observe(host);
      destroy.onDestroy(() => ro.disconnect());
    });
  }
}
