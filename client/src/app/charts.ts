import { Component, ElementRef, effect, input, viewChild } from '@angular/core';
import {
  CategoryScale,
  Chart,
  Filler,
  LinearScale,
  LineController,
  LineElement,
  PointElement,
  type ChartConfiguration,
  type Plugin,
} from 'chart.js';
import ChartAnnotation from 'chartjs-plugin-annotation';
import { DensityPoint } from './models';

let chartsReady = false;

function ensureCharts(): void {
  if (chartsReady) {
    return;
  }
  Chart.register(
    LineController,
    LineElement,
    PointElement,
    LinearScale,
    CategoryScale,
    Filler,
    ChartAnnotation as Plugin,
  );
  chartsReady = true;
}

function cssVar(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

function moneyTick(value: string | number): string {
  const amount = Number(value);
  if (!Number.isFinite(amount)) {
    return '';
  }
  return amount >= 1000 ? `$${Math.round(amount).toLocaleString('en-US')}` : `$${Math.round(amount)}`;
}

@Component({
  selector: 'app-density-chart',
  template: '<canvas #canvas></canvas>',
})
export class DensityChart {
  readonly points = input.required<DensityPoint[]>();
  readonly upper = input<number | null>(null);
  readonly lower = input<number | null>(null);
  readonly spot = input.required<number>();
  readonly theme = input.required<string>();

  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('canvas');
  private chart: Chart | null = null;

  constructor() {
    ensureCharts();
    effect(() => {
      const host = this.canvas();
      const points = this.points();
      const theme = this.theme();
      const upper = this.upper();
      const lower = this.lower();
      const spot = this.spot();
      if (!host || points.length === 0 || !theme) {
        return;
      }
      this.draw(host.nativeElement, points, upper, lower, spot);
    });
  }

  private draw(canvas: HTMLCanvasElement, points: DensityPoint[], upper: number | null, lower: number | null, spot: number): void {
    const muted = cssVar('--muted');
    const line = cssVar('--line');
    const accent = cssVar('--chart');
    const up = cssVar('--up');
    const down = cssVar('--down');
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const upShade = points.map((point) => (upper != null && point.price >= upper ? point.density : null));
    const downShade = points.map((point) => (lower != null && point.price <= lower ? point.density : null));
    const annotations: Record<string, object> = {
      spot: {
        type: 'line',
        scaleID: 'x',
        value: spot,
        borderColor: cssVar('--spot'),
        borderWidth: 1,
        borderDash: [4, 4],
      },
    };
    if (upper != null) {
      annotations['upper'] = { type: 'line', scaleID: 'x', value: upper, borderColor: up, borderWidth: 1.5 };
    }
    if (lower != null) {
      annotations['lower'] = { type: 'line', scaleID: 'x', value: lower, borderColor: down, borderWidth: 1.5 };
    }

    const config: ChartConfiguration<'line'> = {
      type: 'line',
      data: {
        datasets: [
          {
            data: points.map((point) => ({ x: point.price, y: point.density })),
            parsing: false,
            borderColor: accent,
            borderWidth: 2,
            pointRadius: 0,
            tension: 0.25,
          },
          {
            data: points.map((point, index) => ({ x: point.price, y: upShade[index] })),
            parsing: false,
            borderWidth: 0,
            pointRadius: 0,
            fill: 'origin',
            backgroundColor: colorAlpha(up, 0.28),
            spanGaps: false,
          },
          {
            data: points.map((point, index) => ({ x: point.price, y: downShade[index] })),
            parsing: false,
            borderWidth: 0,
            pointRadius: 0,
            fill: 'origin',
            backgroundColor: colorAlpha(down, 0.28),
            spanGaps: false,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: reduce ? false : { duration: 400, easing: 'easeOutQuart' },
        animations: reduce ? undefined : drawAcross({
          duration: (index) => (index === 0 ? 400 : 320),
          delay: (index) => (index === 0 ? 0 : 400),
        }),
        plugins: {
          legend: { display: false },
          tooltip: { enabled: false },
          annotation: { annotations },
        },
        scales: {
          x: {
            type: 'linear',
            ticks: {
              color: muted,
              maxTicksLimit: 5,
              font: { family: 'JetBrains Mono, ui-monospace, monospace', size: 11 },
              callback: (value) => moneyTick(value),
            },
            grid: { color: line },
            border: { display: false },
          },
          y: { display: false },
        },
      },
    };

    this.chart?.destroy();
    this.chart = new Chart(canvas, config);
  }
}

@Component({
  selector: 'app-paths-chart',
  template: '<canvas #canvas></canvas>',
})
export class PathsChart {
  readonly paths = input.required<number[][]>();
  readonly upper = input<number | null>(null);
  readonly lower = input<number | null>(null);
  readonly theme = input.required<string>();

  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('canvas');
  private chart: Chart | null = null;

  constructor() {
    ensureCharts();
    effect(() => {
      const host = this.canvas();
      const paths = this.paths();
      const theme = this.theme();
      const upper = this.upper();
      const lower = this.lower();
      if (!host || paths.length === 0 || !theme) {
        return;
      }
      this.draw(host.nativeElement, paths, upper, lower);
    });
  }

  private draw(canvas: HTMLCanvasElement, paths: number[][], upper: number | null, lower: number | null): void {
    const muted = cssVar('--muted');
    const line = cssVar('--line');
    const accent = cssVar('--chart');
    const up = cssVar('--up');
    const down = cssVar('--down');
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const annotations: Record<string, object> = {};
    if (upper != null) {
      annotations['upper'] = { type: 'line', scaleID: 'y', value: upper, borderColor: up, borderWidth: 1.5 };
    }
    if (lower != null) {
      annotations['lower'] = { type: 'line', scaleID: 'y', value: lower, borderColor: down, borderWidth: 1.5 };
    }

    this.chart?.destroy();
    this.chart = new Chart(canvas, {
      type: 'line',
      data: {
        labels: paths[0].map((_, index) => index),
        datasets: paths.map((path) => ({
          data: path,
          borderColor: colorAlpha(accent, 0.55),
          borderWidth: 1.25,
          pointRadius: 0,
          tension: 0.15,
        })),
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: reduce ? false : {
          duration: 400,
          easing: 'easeOutQuart',
          delay: (ctx: DrawContext) => (ctx.type === 'data' ? ctx.datasetIndex * 28 : 0),
        },
        animations: reduce ? undefined : drawAcross({
          duration: () => 400,
          delay: (index) => index * 28,
        }),
        plugins: {
          legend: { display: false },
          tooltip: { enabled: false },
          annotation: { annotations },
        },
        scales: {
          x: { display: false },
          y: {
            ticks: {
              color: muted,
              maxTicksLimit: 5,
              font: { family: 'JetBrains Mono, ui-monospace, monospace', size: 11 },
              callback: (value) => moneyTick(value),
            },
            grid: { color: line },
            border: { display: false },
          },
        },
      },
    });
  }
}

interface DrawContext {
  type: string;
  datasetIndex: number;
  chart: Chart;
}

function drawAcross(timing: {
  duration: (index: number) => number;
  delay: (index: number) => number;
}): Record<string, object> {
  return {
    x: {
      type: 'number',
      easing: 'easeOutQuart',
      duration: (ctx: DrawContext) => (ctx.type === 'data' ? timing.duration(ctx.datasetIndex) : 400),
      delay: (ctx: DrawContext) => (ctx.type === 'data' ? timing.delay(ctx.datasetIndex) : 0),
      from: (ctx: DrawContext) => {
        if (ctx.type !== 'data') {
          return undefined;
        }
        const scale = ctx.chart.scales['x'];
        const min = scale?.min;
        if (!scale || typeof min !== 'number' || !Number.isFinite(min)) {
          return undefined;
        }
        return scale.getPixelForValue(min);
      },
    },
  };
}

function colorAlpha(color: string, alpha: number): string {
  const hex = color.replace('#', '');
  if (hex.length !== 6) {
    return color;
  }
  const r = Number.parseInt(hex.slice(0, 2), 16);
  const g = Number.parseInt(hex.slice(2, 4), 16);
  const b = Number.parseInt(hex.slice(4, 6), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}
