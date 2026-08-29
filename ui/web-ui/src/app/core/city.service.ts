import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { API_BASE } from './legislation.service';

/* Municipal legislation feed (GET /city-matters) — housing-relevant
   ordinances, resolutions, and motions synchronized from each configured
   city's Granicus Legistar system into Postgres. */

export interface CityMatter {
  city_matter_id: string;
  client: string;
  city_name: string | null;
  matter_id: number;
  matter_file: string | null;
  matter_type: string | null;
  title: string | null;
  status: string | null;
  body_name: string | null;
  intro_date: string | null;
  passed_date: string | null;
  tracking_status: string;
  tags: string[] | null;
  display_date: string | null;
  pinned: boolean;
  has_text: boolean;
}

export interface CityMatterDetail extends CityMatter {
  matter_name: string | null;
  agenda_date: string | null;
  enactment_number: string | null;
  last_modified: string | null;
  text_content: string | null;
}

export interface CityClient {
  key: string;
  name: string;
  jurisdiction: string;
}

export interface CitySyncResult {
  client: string;
  listed: number;
  stored: number;
  texts_pulled: number;
  stored_ids: string[];
}

/* Deep link to the matter's public Legistar page. The gateway resolves the
   API MatterId to the InSite LegislationDetail page (whose ID/GUID pair is a
   separate keyspace the API does not expose). */
export function legistarMatterUrl(m: CityMatter): string {
  return `https://${m.client}.legistar.com/gateway.aspx?M=L&ID=${m.matter_id}`;
}

/* Legistar status strings are free-form per city; bucket them for the pill. */
export function statusBucket(status: string | null): 'ok' | 'warn' | 'neutral' {
  const s = (status || '').toLowerCase();
  if (/(pass|approve|adopt|enact|sign)/.test(s)) return 'ok';
  if (/(pending|committee|unfinished|agenda|referred)/.test(s)) return 'warn';
  return 'neutral';
}

@Injectable({ providedIn: 'root' })
export class CityService {
  readonly matters = signal<CityMatter[]>([]);
  readonly cities = signal<CityClient[]>([]);
  readonly live = signal(false);

  constructor(private http: HttpClient) {
    this.reload();
  }

  reload(): void {
    this.http.get<CityClient[]>(`${API_BASE}/cities`).subscribe({
      next: (rows) => this.cities.set(rows ?? []),
      error: () => this.cities.set([]),
    });
    this.http.get<CityMatter[]>(`${API_BASE}/city-matters?limit=500`).subscribe({
      next: (rows) => {
        this.matters.set(rows ?? []);
        this.live.set(true);
      },
      error: () => this.matters.set([]),
    });
  }

  get(cityMatterId: string) {
    return this.http.get<CityMatterDetail>(`${API_BASE}/city-matters/${cityMatterId}`);
  }

  // --- admin API ------------------------------------------------------------

  adminList(q = '') {
    const query = q.trim() ? `&q=${encodeURIComponent(q.trim())}` : '';
    return this.http.get<CityMatter[]>(`${API_BASE}/city-matters?view=admin&limit=500${query}`);
  }

  setPin(cityMatterId: string, pinned: boolean) {
    return this.http.post<{ city_matter_id: string; pinned: boolean }>(
      `${API_BASE}/admin/cities/matters/${cityMatterId}/pin`, { pinned },
    );
  }

  sync(client: string, days: number) {
    return this.http.post<CitySyncResult>(`${API_BASE}/admin/cities/${client}/sync?days=${days}`, {});
  }

  setDisplay(cityMatterId: string, displayed: boolean) {
    return this.http.post<{ city_matter_id: string; display_date: string | null }>(
      `${API_BASE}/admin/cities/matters/${cityMatterId}/display`, { displayed },
    );
  }
}
