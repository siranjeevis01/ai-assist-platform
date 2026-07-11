import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface ABTest {
  name: string;
  variants: string[];
  weights?: number[];
  startDate: string;
  endDate?: string;
}

interface ABAssignment {
  test: string;
  variant: string;
  assignedAt: string;
}

@Injectable({ providedIn: 'root' })
export class ABTestService {
  private assignments: Map<string, ABAssignment> = new Map();
  private storageKey = 'ai_agent_ab_tests';

  private experiments: ABTest[] = [
    {
      name: 'landing_hero_cta',
      variants: ['Get Started Free', 'Start Building Now', 'Try It Free'],
      startDate: '2025-01-01'
    },
    {
      name: 'dashboard_layout',
      variants: ['cards', 'list'],
      startDate: '2025-01-01'
    },
    {
      name: 'voice_ui_style',
      variants: ['waveform', 'pulse', 'bars'],
      startDate: '2025-01-01'
    }
  ];

  constructor(private http: HttpClient) {
    this.loadAssignments();
  }

  private loadAssignments(): void {
    try {
      const stored = localStorage.getItem(this.storageKey);
      if (stored) {
        const parsed = JSON.parse(stored) as ABAssignment[];
        parsed.forEach(a => this.assignments.set(a.test, a));
      }
    } catch {
      // ignore
    }
  }

  private saveAssignments(): void {
    const arr = Array.from(this.assignments.values());
    localStorage.setItem(this.storageKey, JSON.stringify(arr));
  }

  private hashCode(str: string): number {
    let hash = 0;
    for (let i = 0; i < str.length; i++) {
      const char = str.charCodeAt(i);
      hash = ((hash << 5) - hash) + char;
      hash = hash & hash;
    }
    return Math.abs(hash);
  }

  getVariant(testName: string): string {
    const existing = this.assignments.get(testName);
    if (existing) return existing.variant;

    const test = this.experiments.find(t => t.name === testName);
    if (!test) return 'control';

    const userId = this.getAnonymousId();
    const hash = this.hashCode(`${userId}_${testName}`);
    const variantIndex = hash % test.variants.length;
    const variant = test.variants[variantIndex];

    const assignment: ABAssignment = {
      test: testName,
      variant,
      assignedAt: new Date().toISOString()
    };
    this.assignments.set(testName, assignment);
    this.saveAssignments();

    this.trackEvent(testName, 'assigned', variant).catch(() => {});
    return variant;
  }

  isVariant(testName: string, variant: string): boolean {
    return this.getVariant(testName) === variant;
  }

  trackEvent(testName: string, event: string, variant?: string): Promise<any> {
    const v = variant || this.getVariant(testName);
    const userId = this.getAnonymousId();
    return this.http.post(`${environment.apiUrl}/api/analytics/track`, {
      test: testName,
      event,
      variant: v,
      userId,
      timestamp: new Date().toISOString(),
      url: window.location.href,
      userAgent: navigator.userAgent
    }).toPromise().catch(() => {});
  }

  trackConversion(testName: string, value?: number): Promise<any> {
    return this.trackEvent(testName, 'conversion', undefined);
  }

  private getAnonymousId(): string {
    let id = localStorage.getItem('ai_agent_anonymous_id');
    if (!id) {
      id = 'anon_' + Math.random().toString(36).substring(2, 15);
      localStorage.setItem('ai_agent_anonymous_id', id);
    }
    return id;
  }

  getAllAssignments(): ABAssignment[] {
    return Array.from(this.assignments.values());
  }

  reset(): void {
    this.assignments.clear();
    localStorage.removeItem(this.storageKey);
  }
}
