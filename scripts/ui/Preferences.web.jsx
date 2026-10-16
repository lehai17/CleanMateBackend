import React, { createContext, forwardRef, useContext, useEffect, useMemo, useState } from 'react';
import { defaults, parsePreferences, preferenceKey, resolveTheme, translate } from './core';
import './preferences.css';

const Context = createContext({ ...defaults, resolvedTheme: 'light' });
export const useUiPreferences = () => useContext(Context);
const mapChildren = (children, language) => Array.isArray(children) ? children.map(child => mapChildren(child, language)) : translate(children, language);
export const Ui = forwardRef(function Ui({ as: Tag = 'span', children, ...props }, ref) {
  const { language } = useUiPreferences();
  for (const key of ['placeholder', 'aria-label', 'title', 'alt']) if (props[key]) props[key] = translate(props[key], language);
  return <Tag {...props} ref={ref}>{children === undefined ? undefined : mapChildren(children, language)}</Tag>;
});
export function Raw({ children }) { return children; }
export function t(value) { return translate(value, document.documentElement.lang); }
export function UiPreferencesProvider({ children }) {
  const [preferences, setPreferences] = useState(() => { try { return parsePreferences(localStorage.getItem(preferenceKey)); } catch { return { ...defaults }; } });
  const [systemDark, setSystemDark] = useState(() => window.matchMedia?.('(prefers-color-scheme: dark)').matches || false);
  useEffect(() => {
    const media = window.matchMedia?.('(prefers-color-scheme: dark)');
    const change = event => setSystemDark(event.matches);
    media?.addEventListener('change', change);
    return () => media?.removeEventListener('change', change);
  }, []);
  const resolvedTheme = resolveTheme(preferences.theme, systemDark);
  useEffect(() => {
    document.documentElement.lang = preferences.language;
    document.documentElement.dataset.theme = resolvedTheme;
    try { localStorage.setItem(preferenceKey, JSON.stringify(preferences)); } catch { /* Session-only preferences when storage is unavailable. */ }
  }, [preferences, resolvedTheme]);
  const value = useMemo(() => ({ ...preferences, resolvedTheme }), [preferences, resolvedTheme]);
  return <Context.Provider value={value}>
    <div className="ui-preferences" role="group" aria-label="Language and appearance">
      <label htmlFor="ui-language">{preferences.language === 'en' ? 'Language' : 'Ngôn ngữ'}<select id="ui-language" aria-label="Language / Ngôn ngữ" value={preferences.language} onChange={event => setPreferences(current => ({ ...current, language: event.target.value }))}><option value="vi">Tiếng Việt</option><option value="en">English</option></select></label>
      <label htmlFor="ui-theme">{preferences.language === 'en' ? 'Appearance' : 'Giao diện'}<select id="ui-theme" aria-label="Appearance / Giao diện" value={preferences.theme} onChange={event => setPreferences(current => ({ ...current, theme: event.target.value }))}>{[['system','Theo thiết bị','System'],['light','Sáng','Light'],['dark','Tối','Dark']].map(([key,vi,en]) => <option key={key} value={key}>{preferences.language === 'en' ? en : vi}</option>)}</select></label>
    </div>{children}
  </Context.Provider>;
}
