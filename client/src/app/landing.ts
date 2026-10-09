import { Component, inject, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiService } from './api.service';
import { exampleParams, examples } from './examples';
import { HistoryItem } from './models';

@Component({
  selector: 'app-landing',
  imports: [RouterLink],
  templateUrl: './landing.html',
})
export class Landing {
  private readonly api = inject(ApiService);
  readonly examples = examples;
  readonly params = exampleParams;
  readonly history = signal<HistoryItem[]>([]);

  constructor() {
    inject(Title).setTitle('Jev Odds');
    const route = inject(ActivatedRoute);
    const router = inject(Router);
    if (route.snapshot.queryParamMap.get('ticker')) {
      void router.navigate(['/odds'], { queryParamsHandling: 'preserve', replaceUrl: true });
    }
    this.api.history(24).subscribe({
      // One row per distinct question; the newest run wins.
      next: (rows) => {
        const seen = new Set<string>();
        const unique = rows.filter((row) => {
          const key = [row.ticker, row.percent, row.direction, row.targetDate, row.volWindow, row.volOverridePercent, row.drift].join('|');
          if (seen.has(key)) {
            return false;
          }
          seen.add(key);
          return true;
        });
        this.history.set(unique.slice(0, 8));
      },
      error: () => this.history.set([]),
    });
  }

  percentLabel(value: number): string {
    return Number.isInteger(value) ? `${value}%` : `${value}%`;
  }

  phrase(item: HistoryItem): string {
    const name = item.name ?? item.ticker;
    const when = this.pretty(item.targetDate);
    const percent = this.percentLabel(item.percent);
    if (item.direction === 'down') {
      return `Will ${name} close at least ${percent} lower by ${when}?`;
    }
    if (item.direction === 'either') {
      return `Will ${name} close ${percent} higher or lower by ${when}?`;
    }
    return `Will ${name} close at least ${percent} higher by ${when}?`;
  }

  private pretty(iso: string): string {
    const [year, month, day] = iso.split('-').map(Number);
    const months = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
    if (!year || !month || !day) {
      return iso;
    }
    return `${months[month - 1]} ${day}, ${year}`;
  }
}
