import { Injectable, effect, signal } from '@angular/core';

const STORAGE_KEY = 'jev-theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly mode = signal<'light' | 'dark'>(readInitialTheme());

  constructor() {
    effect(() => {
      const mode = this.mode();
      document.documentElement.setAttribute('data-theme', mode);
      document.documentElement.style.colorScheme = mode;
    });

    const media = window.matchMedia('(prefers-color-scheme: dark)');
    media.addEventListener('change', (event) => {
      if (!localStorage.getItem(STORAGE_KEY)) {
        this.mode.set(event.matches ? 'dark' : 'light');
      }
    });
  }

  toggle(): void {
    const next = this.mode() === 'dark' ? 'light' : 'dark';
    localStorage.setItem(STORAGE_KEY, next);
    this.mode.set(next);
  }
}

function readInitialTheme(): 'light' | 'dark' {
  const stored = localStorage.getItem(STORAGE_KEY);
  if (stored === 'light' || stored === 'dark') {
    return stored;
  }
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}
