import { Component } from '@angular/core';

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
      <text class="diagram-label" x="28" y="84">Barrier</text>
      <polyline
        class="diagram-path"
        pathLength="1"
        points="36,170 96,164 150,146 198,122 248,92 292,74 338,64 392,78 446,58 508,46 608,34"
      />
      <circle class="diagram-spot" cx="36" cy="170" r="4" />
      <text class="diagram-label" x="28" y="214">Spot</text>
      <circle class="diagram-mark" cx="248" cy="92" r="4.5" />
      <text class="diagram-label" x="214" y="78">Touch</text>
      <circle class="diagram-mark diagram-close" cx="608" cy="34" r="4.5" />
      <text class="diagram-label diagram-close-label" x="560" y="26">Close</text>
    </svg>
  `,
})
export class BarrierDiagram {}
