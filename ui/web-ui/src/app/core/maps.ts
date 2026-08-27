/* Display maps ported from the prototype's maps() — confidence, recommendation,
   bill status, and legislative-stage labels. Colors are CSS custom-property refs. */

export interface Badge { label: string; color: string; bg?: string; }

export const CONF: Record<string, Badge> = {
  strong:   { label: 'Strong',   color: 'var(--ok)' },
  mod_high: { label: 'Mod–High', color: 'var(--ok)' },
  moderate: { label: 'Moderate', color: 'var(--warn)' },
  low_mod:  { label: 'Low–Mod',  color: 'var(--alert)' },
  low:      { label: 'Low',      color: 'var(--alert)' },
};

export const REC: Record<string, Badge> = {
  endorse:         { label: 'Endorse Analysis', color: 'var(--ok)',     bg: 'var(--ok-bg)' },
  minor_revisions: { label: 'Minor revisions',  color: 'var(--warn)',   bg: 'var(--warn-bg)' },
  major_revisions: { label: 'Major revisions',  color: 'var(--alert)',  bg: 'var(--alert-bg)' },
  reject:          { label: 'Reject',           color: 'var(--reject)', bg: 'var(--neutral-bg)' },
};

export const BILL_STATUS: Record<string, Badge> = {
  introduced:      { label: 'Introduced',    color: 'var(--text-3)', bg: 'var(--neutral-bg)' },
  in_committee:    { label: 'In Committee',  color: 'var(--warn)',   bg: 'var(--warn-bg)' },
  passed_house:    { label: 'Passed House',  color: 'var(--warn)',   bg: 'var(--warn-bg)' },
  passed_senate:   { label: 'Passed Senate', color: 'var(--warn)',   bg: 'var(--warn-bg)' },
  senate_passed_awaiting_concurrence: { label: 'Senate Passed · Awaiting Concurrence', color: 'var(--warn)', bg: 'var(--warn-bg)' },
  to_president:    { label: 'To President',  color: 'var(--ok)',     bg: 'var(--ok-bg)' },
  enacted:         { label: 'Enacted',       color: 'var(--ok)',     bg: 'var(--ok-bg)' },
  failed:          { label: 'Failed',        color: 'var(--reject)', bg: 'var(--neutral-bg)' },
};

export const STAGE: Record<string, string> = {
  introduced: 'Introduced in House',
  referred_committee: 'Referred to committee',
  passed_house: 'Passed House',
  passed_senate: 'Passed Senate',
  house_concurrence: 'Awaiting House concurrence',
  to_president: 'Sent to President',
  enacted: 'Enacted',
};

export function conf(key: string): Badge {
  return CONF[key] ?? { label: key, color: 'var(--warn)' };
}

export function rec(key: string): Badge {
  return REC[key] ?? { label: key, color: 'var(--warn)', bg: 'var(--warn-bg)' };
}

export function billStatus(key: string): Badge {
  return BILL_STATUS[key] ?? { label: key, color: 'var(--warn)', bg: 'var(--warn-bg)' };
}
