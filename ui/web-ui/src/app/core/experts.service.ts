import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export interface Expert {
  name: string; title: string; affiliation: string; category: string;
  focus: string; profile_url: string; image_url: string;
}

export interface ExpertCatMeta { bucket: string; label: string; color: string; bg: string; avatar: string; }

const CAT_STYLES: Record<string, { color: string; bg: string; avatar: string }> = {
  'Academic':        { color: 'var(--accent)', bg: 'var(--accent-soft)', avatar: 'var(--accent-fill)' },
  'Think tank':      { color: 'var(--gold)',   bg: 'var(--surface-2)',   avatar: 'var(--gold)' },
  'Research center': { color: 'var(--text-2)', bg: 'var(--surface-2)',   avatar: 'var(--inverse)' },
  'Non-profit':      { color: 'var(--ok)',     bg: 'var(--ok-bg)',       avatar: 'var(--ok)' },
  'Other':           { color: 'var(--text-3)', bg: 'var(--surface-2)',   avatar: 'var(--inverse)' },
};

@Injectable({ providedIn: 'root' })
export class ExpertsService {
  readonly experts = signal<Expert[]>([]);

  constructor(private http: HttpClient) {
    this.http.get<{ experts: Expert[] }>('data/experts.json').subscribe({
      next: (d) => { if (d && d.experts) this.experts.set(d.experts); },
      error: () => {},
    });
  }

  initials(name: string): string {
    const parts = (name || '').replace(/[^A-Za-z\s'-]/g, '').trim().split(/\s+/);
    if (!parts.length || !parts[0]) return '?';
    const first = parts[0][0] || '';
    const last = parts.length > 1 ? parts[parts.length - 1][0] : '';
    return (first + last).toUpperCase();
  }

  catMeta(category: string): ExpertCatMeta {
    const bucket = (category || '').indexOf('Non-profit') === 0 ? 'Non-profit'
      : (category === 'Academic' ? 'Academic'
      : (category === 'Think tank' ? 'Think tank'
      : (category === 'Research center' ? 'Research center' : 'Other')));
    return { bucket, label: bucket, ...CAT_STYLES[bucket] };
  }
}
