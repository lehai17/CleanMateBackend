import React, { createContext, forwardRef, useContext, useEffect, useMemo, useRef, useState } from 'react';
import * as Native from 'react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { SafeAreaProvider, SafeAreaView as PreferenceSafeArea } from 'react-native-safe-area-context';
import { NavigationContainer as Container, DarkTheme, DefaultTheme } from '@react-navigation/native';
import { defaults, palettes, parsePreferences, preferenceKey, resolveTheme, translate } from './core';

const Context = createContext({ ...defaults, resolvedTheme:'light', palette:palettes.light });
export const useUiPreferences = () => useContext(Context);
let language = 'vi';
export const t = value => translate(value, language);
const childrenIn = (children, locale) => Array.isArray(children) ? children.map(child => childrenIn(child, locale)) : translate(children, locale);
export function Raw({ children }) { return children; }
const darkColors = {
  white:'#192a25', '#fff':'#192a25', '#ffffff':'#192a25', '#f9fafb':'#101b19', '#f8fafc':'#101b19', '#f3f4f6':'#101b19', '#f4f7f6':'#101b19', '#f1f5f9':'#101b19', '#eee':'#192a25', '#f0fdf4':'#154537', '#ecfdf5':'#154537', '#d1fae5':'#154537', '#bbf7d0':'#154537', '#a7f3d0':'#154537', '#eff6ff':'#1e3651', '#dbeafe':'#1e3651', '#fef2f2':'#421f25', '#fee2e2':'#421f25', '#fffbeb':'#413419', '#fff7ed':'#413419', '#fde68a':'#413419', '#cbd5e1':'#486359',
  black:'#edf7f2', '#000':'#edf7f2', '#111827':'#edf7f2', '#1f2937':'#edf7f2', '#374151':'#edf7f2', '#172d2a':'#edf7f2', '#6b7280':'#b6cdc1', '#4b5563':'#b6cdc1', '#64748b':'#b6cdc1', '#475569':'#b6cdc1', '#9ca3af':'#b6cdc1', '#d1d5db':'#486359', '#e5e7eb':'#486359', '#e2e8f0':'#486359', '#dce6e2':'#486359', '#dc2626':'#fca5a5', '#b91c1c':'#fca5a5', '#047857':'#6ee7b7', '#065f46':'#a7f3d0'
};
export function themedStyle(style, dark, kind) {
  const flat = Native.StyleSheet.flatten(style) || {};
  if (!dark) return flat;
  const result = { ...flat };
  for (const key of ['backgroundColor','color','borderColor','borderBottomColor','borderTopColor']) {
    const original = typeof result[key] === 'string' ? result[key].toLowerCase() : '';
    // Keep white text on brand-colored buttons and banners readable.
    if (key === 'color' && ['white','#fff','#ffffff'].includes(original)) continue;
    if (key === 'backgroundColor' && ['#047857','#065f46'].includes(original)) continue;
    if (key === 'backgroundColor' && /^rgba\(255,\s*255,\s*255,/.test(original)) result[key] = '#192a25';
    else if (key === 'color' && ['#059669','#16a34a','#10b981'].includes(original)) result[key] = '#6ee7b7';
    else if (key === 'color' && ['#92400e','#9a3412'].includes(original)) result[key] = '#fde68a';
    else if (darkColors[original]) result[key] = darkColors[original];
  }
  return result;
}
function primitive(name) {
  const Component = Native[name];
  return forwardRef(function LocalizedPrimitive({ children, style, ...props }, ref) {
    const { language:locale, resolvedTheme, palette } = useUiPreferences();
    const dark = resolvedTheme === 'dark';
    for (const key of ['placeholder','accessibilityLabel','accessibilityHint','title']) if (props[key]) props[key] = translate(props[key], locale);
    if (name === 'TextInput') { props.placeholderTextColor ||= palette.muted; props.keyboardAppearance = dark ? 'dark' : 'light'; }
    if (props.contentContainerStyle) props.contentContainerStyle = themedStyle(props.contentContainerStyle, dark, name);
    const base = name === 'Text' ? { color:palette.text } : name === 'TextInput' ? { color:palette.text, backgroundColor:palette.surface } : null;
    const styled = typeof style === 'function' ? state => [base,themedStyle(style(state),dark,name)] : [base,themedStyle(style,dark,name)];
    return <Component {...props} ref={ref} style={styled}>{children === undefined ? undefined : childrenIn(children, locale)}</Component>;
  });
}
export const Text=primitive('Text'), View=primitive('View'), TextInput=primitive('TextInput'), Button=primitive('Button'), Pressable=primitive('Pressable'), TouchableOpacity=primitive('TouchableOpacity'), ScrollView=primitive('ScrollView'), FlatList=primitive('FlatList'), KeyboardAvoidingView=primitive('KeyboardAvoidingView'), ActivityIndicator=primitive('ActivityIndicator'), SafeAreaView=primitive('SafeAreaView');
export const Alert = { ...Native.Alert, alert(title,message,buttons,options) { return Native.Alert.alert(t(title),t(message),buttons?.map(button => ({...button,text:t(button.text)})),options); } };
export function NavigationContainer({ children, ...props }) {
  const { resolvedTheme,palette } = useUiPreferences();
  const base = resolvedTheme === 'dark' ? DarkTheme : DefaultTheme;
  return <Container {...props} theme={{...base, colors:{...base.colors,background:palette.background,card:palette.surface,text:palette.text,border:palette.border,primary:palette.accent}}}>{children}</Container>;
}
export function UiPreferencesProvider({ children }) {
  const systemDark = Native.useColorScheme() === 'dark';
  const [preferences,setPreferences] = useState({ ...defaults });
  const [ready,setReady] = useState(false);
  const changed = useRef(false);
  const writes = useRef(Promise.resolve());
  useEffect(() => { let alive=true; AsyncStorage.getItem(preferenceKey).then(value => { if (alive && !changed.current) setPreferences(parsePreferences(value)); }).catch(() => {}).finally(() => { if(alive) setReady(true); }); return () => { alive=false; }; }, []);
  useEffect(() => { if (ready) writes.current = writes.current.catch(() => {}).then(() => AsyncStorage.setItem(preferenceKey,JSON.stringify(preferences))).catch(() => {}); }, [preferences,ready]);
  useEffect(() => {
    Native.Appearance.setColorScheme(preferences.theme === 'system' ? null : preferences.theme);
    return () => Native.Appearance.setColorScheme(null);
  }, [preferences.theme]);
  const resolvedTheme=resolveTheme(preferences.theme,systemDark);
  language=preferences.language;
  const palette=palettes[resolvedTheme];
  const value=useMemo(() => ({...preferences,resolvedTheme,palette}),[preferences,resolvedTheme,palette]);
  const choose = (key,value) => { changed.current=true; setPreferences(current => ({...current,[key]:value})); };
  const en=preferences.language==='en';
  return <Context.Provider value={value}><SafeAreaProvider><Native.View style={{flex:1,backgroundColor:palette.background}}>
    <Native.StatusBar barStyle={resolvedTheme==='dark'?'light-content':'dark-content'} backgroundColor={palette.surface} />
    <PreferenceSafeArea edges={['top']} accessibilityLabel="Language and appearance" style={{paddingHorizontal:12,paddingBottom:8,backgroundColor:palette.surface,borderBottomWidth:1,borderColor:palette.border}}>
      <Native.View style={{flexDirection:'row',alignItems:'center',flexWrap:'wrap',gap:6}}>
        {[['language','vi','VI'],['language','en','EN'],['theme','system',en?'System':'Thiết bị'],['theme','light',en?'Light':'Sáng'],['theme','dark',en?'Dark':'Tối']].map(([key,value,label]) => <Native.Pressable key={key+value} accessibilityRole="button" accessibilityLabel={`${key==='language'?'Language':'Appearance'}: ${label}`} accessibilityState={{selected:preferences[key]===value}} onPress={() => choose(key,value)} style={{paddingVertical:10,paddingHorizontal:12,borderRadius:8,borderWidth:1,borderColor:preferences[key]===value?palette.accent:palette.border}}><Native.Text style={{color:palette.text,fontWeight:preferences[key]===value?'700':'400'}}>{label}</Native.Text></Native.Pressable>)}
      </Native.View>
    </PreferenceSafeArea><SafeAreaProvider style={{flex:1}}>{children}</SafeAreaProvider>
  </Native.View></SafeAreaProvider></Context.Provider>;
}
