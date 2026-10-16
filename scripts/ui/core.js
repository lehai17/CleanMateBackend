import { english } from './catalog';

export const preferenceKey = 'cleanmate_ui_preferences';
export const defaults = { language: 'vi', theme: 'system' };
export function parsePreferences(value) {
  try {
    const parsed = typeof value === 'string' ? JSON.parse(value) : value;
    return { language: parsed?.language === 'en' ? 'en' : 'vi', theme: ['light', 'dark', 'system'].includes(parsed?.theme) ? parsed.theme : 'system' };
  } catch { return { ...defaults }; }
}
export function resolveTheme(theme, systemDark) { return theme === 'system' ? (systemDark ? 'dark' : 'light') : theme; }
export function translate(value, language = 'vi') {
  if (typeof value !== 'string' || language !== 'en') return value;
  const normalized = value.replace(/\s+/g, ' ').trim();
  const result = Object.prototype.hasOwnProperty.call(english, normalized) ? english[normalized] : undefined;
  if (result) return value.match(/^\s*/)[0] + result + value.match(/\s*$/)[0];
  const punctuated = normalized.match(/^([·•→]\s*)?(.*?)(\s*[·•→:])?$/);
  if (punctuated && Object.prototype.hasOwnProperty.call(english,punctuated[2])) return `${punctuated[1] || ''}${english[punctuated[2]]}${punctuated[3] || ''}`;
  // Only whole, explicitly authored templates. Never translate arbitrary substrings.
  const match = normalized.match(/^(\d+) thông báo chưa đọc$/);
  if (match) return `${match[1]} unread notifications`;
  const templates = [
    [/^Trang (\d+) · (\d+) yêu cầu$/, 'Page $1 · $2 requests'],
    [/^Đã gửi yêu cầu HT(\d+)\.$/, 'Support request HT$1 sent.'],
    [/^Phân công HT(\d+)$/, 'Assign support request HT$1'],
    [/^Xử lý HT(\d+)$/, 'Handle support request HT$1'],
    [/^Trao đổi HT(\d+)$/, 'Support conversation HT$1'],
    [/^Thao tác quá nhiều lần\. Thử lại sau (\d+) giây\.$/, 'Too many attempts. Try again in $1 seconds.'],
    [/^Thử lại sau (\d+) giây\.$/, 'Try again in $1 seconds.']
  ];
  for (const [pattern, replacement] of templates) if (pattern.test(normalized)) return normalized.replace(pattern,replacement);
  return value;
}
export const palettes = {
  light: { background: '#f8fafc', surface: '#ffffff', text: '#172d2a', muted: '#52665e', border: '#cadbd5', accent: '#047857', danger: '#b91c1c' },
  dark: { background: '#101b19', surface: '#192a25', text: '#edf7f2', muted: '#b6cdc1', border: '#486359', accent: '#6ee7b7', danger: '#fca5a5' }
};
