import { Component, computed, inject, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService, api, errorMessage } from './api.service';
import { DensityChart, PathsChart } from './charts';
import { CalendarResponse, Direction, Drift, MarketSnapshot, OddsRequest, OddsResponse, TickerInfo, VolWindow } from './models';
import { ThemeService } from './theme.service';

interface Preset {
  label: string;
  ticker: string;
  percent: number;
  direction: Direction;
  date: string;
}

@Component({
  selector: 'app-home',
  imports: [DensityChart, PathsChart],
  templateUrl: './home.html',
})
export class Home {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly title = inject(Title);
  readonly theme = inject(ThemeService);

  readonly ticker = signal('SPY');
  readonly percent = signal(5);
  readonly direction = signal<Direction>('up');
  readonly targetDate = signal('2027-01-15');
  readonly volWindow = signal<VolWindow>('60');
  readonly volOverride = signal('');
  readonly drift = signal<Drift>('zero');
  readonly suggestions = signal<TickerInfo[]>([]);
  readonly suggestOpen = signal(false);
  readonly highlight = signal(0);
  readonly preview = signal<MarketSnapshot | null>(null);
  readonly previewNote = signal('');
  readonly calendar = signal<CalendarResponse | null>(null);
  readonly result = signal<OddsResponse | null>(null);
  readonly shownClose = signal(0);
  readonly shownTouch = signal(0);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly copied = signal(false);

  readonly presets: Preset[] = [
    { label: 'SPY up 5% by Jan 15, 2027', ticker: 'SPY', percent: 5, direction: 'up', date: '2027-01-15' },
    { label: 'NVDA down 10% by Apr 16, 2027', ticker: 'NVDA', percent: 10, direction: 'down', date: '2027-04-16' },
    { label: 'AAPL up or down 8% by Jun 18, 2027', ticker: 'AAPL', percent: 8, direction: 'either', date: '2027-06-18' },
  ];

  readonly freeEntry = computed(() => {
    const symbol = this.ticker().trim().toUpperCase();
    if (!/^[A-Z][A-Z0-9.-]{0,11}$/.test(symbol)) {
      return '';
    }
    if (this.suggestions().some((item) => item.symbol.toUpperCase() === symbol)) {
      return '';
    }
    return symbol;
  });

  readonly choiceCount = computed(() => this.suggestions().length + (this.freeEntry() ? 1 : 0));

  readonly stale = computed(() => {
    const result = this.result();
    if (!result || !this.appliedKey) {
      return false;
    }
    return this.appliedKey !== JSON.stringify(this.requestBody());
  });

  private appliedKey = '';
  private previewToken = 0;
  private oddsSub: Subscription | null = null;
  private timer = 0;
  private bootstrapped = false;

  constructor() {
    this.route.queryParamMap.subscribe((map) => this.onParams(map));
  }

  onTicker(value: string): void {
    this.ticker.set(value.toUpperCase());
    this.suggestOpen.set(true);
    this.highlight.set(0);
    window.clearTimeout(this.timer);
    this.timer = window.setTimeout(() => this.refreshSideData(), 250);
  }

  onTickerKey(event: KeyboardEvent): void {
    if (!this.suggestOpen()) {
      return;
    }
    const count = this.choiceCount();
    if (event.key === 'ArrowDown' && count > 0) {
      event.preventDefault();
      this.highlight.update((index) => Math.min(count - 1, index + 1));
    } else if (event.key === 'ArrowUp' && count > 0) {
      event.preventDefault();
      this.highlight.update((index) => Math.max(0, index - 1));
    } else if (event.key === 'Enter' && count > 0) {
      event.preventDefault();
      this.pickHighlighted();
    } else if (event.key === 'Escape') {
      this.suggestOpen.set(false);
    }
  }

  onTickerBlur(): void {
    window.setTimeout(() => this.suggestOpen.set(false), 140);
  }

  pick(symbol: string): void {
    this.ticker.set(symbol.toUpperCase());
    this.suggestOpen.set(false);
    this.refreshSideData();
  }

  onPercent(value: string): void {
    this.percent.set(Number(value));
  }

  onOverride(value: string): void {
    this.volOverride.set(value);
  }

  onDate(value: string): void {
    this.targetDate.set(value);
    this.loadCalendar();
  }

  setDirection(direction: Direction): void {
    this.direction.set(direction);
  }

  setVol(windowName: VolWindow): void {
    this.volWindow.set(windowName);
  }

  setDrift(drift: Drift): void {
    this.drift.set(drift);
  }

  applyPreset(preset: Preset): void {
    this.ticker.set(preset.ticker);
    this.percent.set(preset.percent);
    this.direction.set(preset.direction);
    this.targetDate.set(preset.date);
    this.suggestOpen.set(false);
    this.submit();
  }

  submit(): void {
    const message = this.validate();
    if (message) {
      this.error.set(message);
      return;
    }
    this.error.set('');
    const params = this.queryParams();
    if (this.paramsMatch(this.route.snapshot.queryParamMap, params)) {
      this.fetchOdds();
      return;
    }
    void this.router.navigate([], { queryParams: params, replaceUrl: true });
  }

  async copyLink(): Promise<void> {
    try {
      const path = this.router.url.replace(/^\//, '');
      await navigator.clipboard.writeText(new URL(path, document.baseURI).toString());
      this.copied.set(true);
      window.setTimeout(() => this.copied.set(false), 1600);
    } catch {
      this.error.set('Could not copy the link. The address bar already holds it.');
    }
  }

  downloadPdf(): void {
    const request = this.lastRequest;
    if (!request) {
      return;
    }
    const params = new URLSearchParams({
      ticker: request.ticker,
      percent: String(request.percent),
      direction: request.direction,
      targetDate: request.targetDate,
      volWindow: request.volWindow,
      drift: request.drift,
    });
    if (request.volOverridePercent != null) {
      params.set('volOverridePercent', String(request.volOverridePercent));
    }
    const anchor = document.createElement('a');
    anchor.href = api(`api/odds/pdf?${params.toString()}`);
    anchor.rel = 'noopener';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  }

  phrase(result: OddsResponse): string {
    const move = `${trimNumber(result.percent)}%`;
    if (result.direction === 'down') {
      return `down ${move}`;
    }
    if (result.direction === 'either') {
      return `up or down ${move}`;
    }
    return `up ${move}`;
  }

  originLabel(origin: string, provider: string): string {
    const source = provider === 'yahoo' ? 'Yahoo Finance' : provider === 'stooq' ? 'Stooq' : provider === 'seed' ? 'sample' : provider;
    if (origin === 'live') {
      return `Live, ${source}`;
    }
    if (provider === 'seed') {
      return 'Cached sample';
    }
    return `Cached, ${source}`;
  }

  pretty(iso: string): string {
    const [year, month, day] = iso.split('-').map(Number);
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    if (!year || !month || !day) {
      return iso;
    }
    return `${months[month - 1]} ${day}, ${year}`;
  }

  money(value: number): string {
    return value.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
  }

  volPct(value: number | null | undefined): string {
    if (value == null || !Number.isFinite(value)) {
      return '';
    }
    return `${(value * 100).toFixed(1)}%`;
  }

  signedPct(value: number | null | undefined): string {
    if (value == null || !Number.isFinite(value)) {
      return '';
    }
    const text = `${(value * 100).toFixed(1)}%`;
    return value > 0 ? `+${text}` : text;
  }

  probText(probability: number): string {
    const value = probability * 100;
    if (value > 0 && value < 0.05) {
      return '<0.1';
    }
    if (value >= 99.95) {
      return '>99.9';
    }
    return value.toFixed(1);
  }

  railWidth(probability: number): number {
    return Math.max(0, Math.min(100, probability * 100));
  }

  private lastRequest: OddsRequest | null = null;

  private onParams(map: ParamMap): void {
    const ticker = map.get('ticker');
    if (!ticker) {
      if (!this.bootstrapped) {
        this.bootstrapped = true;
        this.refreshSideData();
      }
      return;
    }
    this.bootstrapped = true;
    this.ticker.set(ticker.toUpperCase());
    const percent = Number(map.get('percent'));
    if (Number.isFinite(percent) && percent > 0) {
      this.percent.set(percent);
    }
    const direction = map.get('direction');
    if (direction === 'up' || direction === 'down' || direction === 'either') {
      this.direction.set(direction);
    }
    const date = map.get('date');
    if (date) {
      this.targetDate.set(date);
    }
    const vol = map.get('vol');
    if (vol === '20' || vol === '60' || vol === '252' || vol === 'blend') {
      this.volWindow.set(vol);
    }
    const drift = map.get('drift');
    if (drift === 'zero' || drift === 'historical') {
      this.drift.set(drift);
    }
    this.volOverride.set(map.get('override') ?? '');
    this.refreshSideData();
    this.fetchOdds();
  }

  private pickHighlighted(): void {
    const index = this.highlight();
    const items = this.suggestions();
    if (index < items.length) {
      this.pick(items[index].symbol);
      return;
    }
    if (this.freeEntry()) {
      this.pick(this.freeEntry());
    }
  }

  private refreshSideData(): void {
    const symbol = this.ticker().trim();
    const token = ++this.previewToken;
    this.api.tickers(symbol).subscribe({
      next: (rows) => {
        if (token === this.previewToken) {
          this.suggestions.set(rows);
        }
      },
      error: () => {
        if (token === this.previewToken) {
          this.suggestions.set([]);
        }
      },
    });
    if (!/^[A-Za-z][A-Za-z0-9.-]{0,11}$/.test(symbol)) {
      this.preview.set(null);
      this.calendar.set(null);
      return;
    }
    this.api.market(symbol).subscribe({
      next: (snapshot) => {
        if (token !== this.previewToken) {
          return;
        }
        this.preview.set(snapshot);
        this.previewNote.set('');
        this.loadCalendar();
      },
      error: (err: unknown) => {
        if (token !== this.previewToken) {
          return;
        }
        this.preview.set(null);
        this.calendar.set(null);
        this.previewNote.set(errorMessage(err));
      },
    });
  }

  private loadCalendar(): void {
    const asOf = this.preview()?.asOf;
    const target = this.targetDate();
    if (!asOf || !target) {
      this.calendar.set(null);
      return;
    }
    this.api.calendar(asOf, target).subscribe({
      next: (calendar) => this.calendar.set(calendar),
      error: () => this.calendar.set(null),
    });
  }

  private fetchOdds(): void {
    const body = this.requestBody();
    const key = JSON.stringify(body);
    this.loading.set(true);
    this.error.set('');
    this.lastRequest = body;
    this.oddsSub?.unsubscribe();
    this.oddsSub = this.api.odds(body).subscribe({
      next: (result) => {
        this.loading.set(false);
        this.appliedKey = key;
        this.result.set(result);
        this.playCount(result.analyticClose, result.analyticTouch);
        this.title.setTitle(`${result.ticker} ${this.phrase(result)} · Jev Odds`);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.result.set(null);
        this.playCount(0, 0);
        this.appliedKey = '';
        this.error.set(errorMessage(err));
      },
    });
  }

  private playCount(close: number, touch: number): void {
    this.shownClose.set(close);
    this.shownTouch.set(touch);
  }

  private requestBody(): OddsRequest {
    const override = this.volOverride().trim();
    return {
      ticker: this.ticker().trim(),
      percent: Number(this.percent()),
      direction: this.direction(),
      targetDate: this.targetDate(),
      volWindow: this.volWindow(),
      volOverridePercent: override ? Number(override) : null,
      drift: this.drift(),
    };
  }

  private queryParams(): Record<string, string> {
    const params: Record<string, string> = {
      ticker: this.ticker().trim().toUpperCase(),
      percent: String(this.percent()),
      direction: this.direction(),
      date: this.targetDate(),
      vol: this.volWindow(),
      drift: this.drift(),
    };
    const override = this.volOverride().trim();
    if (override) {
      params['override'] = override;
    }
    return params;
  }

  private paramsMatch(map: ParamMap, params: Record<string, string>): boolean {
    const keys = ['ticker', 'percent', 'direction', 'date', 'vol', 'drift', 'override'];
    return keys.every((key) => {
      const fromUrl = map.get(key) ?? '';
      const next = params[key] ?? '';
      if (key === 'ticker') {
        return fromUrl.toUpperCase() === next.toUpperCase();
      }
      if (key === 'percent') {
        return Number(fromUrl) === Number(next);
      }
      return fromUrl === next;
    });
  }

  private validate(): string {
    const symbol = this.ticker().trim();
    if (!/^[A-Za-z][A-Za-z0-9.-]{0,11}$/.test(symbol)) {
      return 'Enter a ticker such as SPY or AAPL.';
    }
    const percent = this.percent();
    if (!Number.isFinite(percent) || percent <= 0) {
      return 'Enter a percent move greater than 0.';
    }
    if ((this.direction() === 'down' || this.direction() === 'either') && percent > 90) {
      return 'For down or either, use a move of 90 percent or less.';
    }
    if (percent > 400) {
      return 'Use a move of 400 percent or less.';
    }
    if (!this.targetDate()) {
      return 'Pick a target date.';
    }
    const override = this.volOverride().trim();
    if (override) {
      const value = Number(override);
      if (!Number.isFinite(value) || value <= 0 || value > 400) {
        return 'Volatility override must be greater than 0 and at most 400.';
      }
    }
    return '';
  }
}

function trimNumber(value: number): string {
  return Number.isInteger(value) ? String(value) : String(value);
}
