import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { CalendarResponse, HistoryItem, MarketSnapshot, OddsRequest, OddsResponse, TickerInfo } from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  tickers(query: string) {
    return this.http.get<TickerInfo[]>(api('api/tickers'), { params: { q: query } });
  }

  market(ticker: string) {
    return this.http.get<MarketSnapshot>(api(`api/market/${encodeURIComponent(ticker)}`));
  }

  calendar(from: string, to: string) {
    return this.http.get<CalendarResponse>(api('api/calendar'), { params: { from, to } });
  }

  odds(body: OddsRequest) {
    return this.http.post<OddsResponse>(api('api/odds'), body);
  }

  history(limit = 8) {
    return this.http.get<HistoryItem[]>(api('api/history'), { params: { limit } });
  }
}

export function api(path: string): string {
  return new URL(path, document.baseURI).toString();
}

export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const message = error.error?.message;
    if (typeof message === 'string' && message.trim()) {
      return message;
    }
    if (error.status === 0) {
      return 'The API did not respond.';
    }
  }
  return 'The calculation did not complete.';
}
