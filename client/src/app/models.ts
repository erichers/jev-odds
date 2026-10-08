export interface DensityPoint {
  price: number;
  density: number;
}

export interface OddsRequest {
  ticker: string;
  percent: number;
  direction: Direction;
  targetDate: string;
  volWindow: VolWindow;
  volOverridePercent: number | null;
  drift: Drift;
}

export interface OddsResponse {
  ticker: string;
  name: string | null;
  direction: Direction;
  percent: number;
  spot: number;
  asOf: string;
  targetDate: string;
  effectiveDate: string;
  tradingDays: number;
  years: number;
  sigma: number;
  volSource: string;
  volSampleDays: number;
  vol20: number | null;
  vol60: number | null;
  vol252: number | null;
  volBlend: number | null;
  drift: Drift;
  mu: number;
  nu: number;
  upper: number | null;
  lower: number | null;
  analyticClose: number;
  analyticTouch: number;
  monteCarloClose: number;
  monteCarloTouch: number;
  monteCarloPaths: number;
  monteCarloSeed: number;
  expectedLow: number;
  expectedHigh: number;
  empiricalClose: number | null;
  empiricalTouch: number | null;
  empiricalSamples: number;
  origin: 'live' | 'cached';
  provider: string;
  density: DensityPoint[];
  paths: number[][];
  formula: string;
}

export interface MarketSnapshot {
  ticker: string;
  name: string | null;
  lastClose: number;
  asOf: string;
  origin: 'live' | 'cached';
  provider: string;
  bars: number;
  vol20: number | null;
  vol60: number | null;
  vol252: number | null;
  volBlend: number | null;
  logDriftAnnual: number | null;
}

export interface TickerInfo {
  symbol: string;
  name: string;
  seeded: boolean;
}

export interface HistoryItem {
  id: number;
  ticker: string;
  name: string | null;
  percent: number;
  direction: Direction;
  targetDate: string;
  volWindow: string;
  volOverridePercent: number | null;
  drift: Drift;
  analyticClose: number;
  analyticTouch: number;
  origin: string;
  createdAtUtc: string;
}

export interface CalendarResponse {
  from: string;
  to: string;
  tradingDays: number;
  effectiveDate: string | null;
}

export type Direction = 'up' | 'down' | 'either';
export type VolWindow = '20' | '60' | '252' | 'blend';
export type Drift = 'zero' | 'historical';
