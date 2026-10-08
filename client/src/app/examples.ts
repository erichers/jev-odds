import { Direction } from './models';

export interface ExampleQuestion {
  ticker: string;
  percent: number;
  direction: Direction;
  date: string;
  question: string;
}

export const examples: ExampleQuestion[] = [
  {
    ticker: 'NVDA',
    percent: 10,
    direction: 'up',
    date: '2027-04-16',
    question: 'Will NVIDIA close at least 10% higher by April 16, 2027?',
  },
  {
    ticker: 'SPY',
    percent: 5,
    direction: 'up',
    date: '2027-01-15',
    question: 'Will the S&P 500 ETF close at least 5% higher by January 15, 2027?',
  },
  {
    ticker: 'TSLA',
    percent: 15,
    direction: 'down',
    date: '2027-03-19',
    question: 'Will Tesla close at least 15% lower by March 19, 2027?',
  },
  {
    ticker: 'AAPL',
    percent: 8,
    direction: 'either',
    date: '2027-06-18',
    question: 'Will Apple close 8% higher or lower by June 18, 2027?',
  },
  {
    ticker: 'META',
    percent: 12,
    direction: 'up',
    date: '2026-12-18',
    question: 'Will Meta close at least 12% higher by December 18, 2026?',
  },
  {
    ticker: 'QQQ',
    percent: 6,
    direction: 'down',
    date: '2027-02-12',
    question: 'Will the Nasdaq 100 ETF close at least 6% lower by February 12, 2027?',
  },
  {
    ticker: 'AMZN',
    percent: 10,
    direction: 'up',
    date: '2027-05-21',
    question: 'Will Amazon close at least 10% higher by May 21, 2027?',
  },
  {
    ticker: 'IWM',
    percent: 7,
    direction: 'either',
    date: '2027-01-15',
    question: 'Will the Russell 2000 ETF close 7% higher or lower by January 15, 2027?',
  },
];

export function exampleParams(example: ExampleQuestion): Record<string, string> {
  return {
    ticker: example.ticker,
    percent: String(example.percent),
    direction: example.direction,
    date: example.date,
    vol: '60',
    drift: 'zero',
  };
}
