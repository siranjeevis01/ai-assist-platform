import { Injectable, signal, effect } from '@angular/core';

export type Theme = 'light' | 'dark' | 'system';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private storageKey = 'ai_agent_theme';
  currentTheme = signal<Theme>(this.loadTheme());

  constructor() {
    effect(() => {
      this.applyTheme(this.currentTheme());
    });
    this.applyTheme(this.currentTheme());
  }

  private loadTheme(): Theme {
    const stored = localStorage.getItem(this.storageKey) as Theme;
    return stored || 'system';
  }

  setTheme(theme: Theme): void {
    this.currentTheme.set(theme);
    localStorage.setItem(this.storageKey, theme);
  }

  private applyTheme(theme: Theme): void {
    const root = document.documentElement;
    root.classList.remove('light', 'dark');

    if (theme === 'system') {
      const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
      root.classList.add(prefersDark ? 'dark' : 'light');
    } else {
      root.classList.add(theme);
    }
  }

  get isDark(): boolean {
    return document.documentElement.classList.contains('dark');
  }

  toggleDark(): void {
    const current = this.currentTheme();
    if (current === 'dark') {
      this.setTheme('light');
    } else {
      this.setTheme('dark');
    }
  }
}
