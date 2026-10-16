import { defaults, parsePreferences, preferenceKey, resolveTheme, translate } from './core.js';

let preferences;
try { preferences = parsePreferences(localStorage.getItem(preferenceKey)); } catch { preferences = { ...defaults }; }
const media = window.matchMedia('(prefers-color-scheme: dark)');
const textSources = new WeakMap();
const attributeSources = new WeakMap();
export const t = value => translate(value, preferences.language);
const excluded = element => element.closest('script,style,[data-ui-raw],[data-ui-controls],.thread-body');
function updateText(node) {
  if (!node.parentElement || excluded(node.parentElement) || node.parentElement.closest('textarea')) return;
  const previous = textSources.get(node);
  const source = previous && node.nodeValue === previous.rendered ? previous.source : node.nodeValue;
  const rendered = t(source);
  textSources.set(node, { source, rendered });
  if (node.nodeValue !== rendered) node.nodeValue = rendered;
}
function updateElement(element) {
  if (excluded(element)) return;
  const records = attributeSources.get(element) || {};
  for (const attribute of ['placeholder','aria-label','title','alt']) {
    const value = element.getAttribute(attribute);
    if (value === null) continue;
    const previous = records[attribute];
    const source = previous && value === previous.rendered ? previous.source : value;
    const rendered = t(source);
    records[attribute] = { source, rendered };
    if (rendered !== value) element.setAttribute(attribute, rendered);
  }
  attributeSources.set(element, records);
}
function visit(node) {
  if (node.nodeType === Node.TEXT_NODE) { updateText(node); return; }
  if (node.nodeType !== Node.ELEMENT_NODE || excluded(node)) return;
  updateElement(node);
  if (node.tagName === 'TEXTAREA') return;
  for (const child of node.childNodes) visit(child);
}
const controls = document.createElement('div');
controls.className = 'ui-preferences'; controls.dataset.uiControls = ''; controls.setAttribute('role','group'); controls.setAttribute('aria-label','Language and appearance');
controls.innerHTML = '<label for="ui-language"><span data-language-label></span><select id="ui-language" aria-label="Language / Ngôn ngữ"><option value="vi">Tiếng Việt</option><option value="en">English</option></select></label><label for="ui-theme"><span data-theme-label></span><select id="ui-theme" aria-label="Appearance / Giao diện"><option value="system"></option><option value="light"></option><option value="dark"></option></select></label>';
document.body.prepend(controls);
function apply() {
  document.documentElement.lang = preferences.language;
  document.documentElement.dataset.theme = resolveTheme(preferences.theme, media.matches);
  controls.querySelector('[data-language-label]').textContent = preferences.language === 'en' ? 'Language' : 'Ngôn ngữ';
  controls.querySelector('[data-theme-label]').textContent = preferences.language === 'en' ? 'Appearance' : 'Giao diện';
  controls.querySelector('#ui-language').value = preferences.language;
  const theme = controls.querySelector('#ui-theme'); theme.value = preferences.theme;
  [...theme.options].forEach((option, index) => { option.textContent = (preferences.language === 'en' ? ['System','Light','Dark'] : ['Theo thiết bị','Sáng','Tối'])[index]; });
  visit(document.body);
  try { localStorage.setItem(preferenceKey, JSON.stringify(preferences)); } catch { /* Keep in memory. */ }
}
controls.querySelector('#ui-language').onchange = event => { preferences.language = event.target.value; apply(); };
controls.querySelector('#ui-theme').onchange = event => { preferences.theme = event.target.value; apply(); };
media.addEventListener('change', apply);
new MutationObserver(records => {
  for (const record of records) {
    if (record.type === 'childList') record.addedNodes.forEach(visit);
    else if (record.type === 'attributes') updateElement(record.target);
    else updateText(record.target);
  }
}).observe(document.body, {subtree:true, childList:true, characterData:true, attributes:true, attributeFilter:['placeholder','aria-label','title','alt']});
apply();
