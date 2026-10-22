#!/usr/bin/env node
/* Сверяет генератор на C# (WinInstaller.exe --gen) с оригиналом на JavaScript: XML, предупреждения и Start-Sysprep.cmd
   должны совпасть посимвольно. Прогоняет примеры, типовые и полностью заполненные состояния, пресеты и случайные
   («fuzz») состояния, включая неверные типы и значения вне диапазонов.

     node devtools/gen-compare.js <путь к generator_XML_Windows> <путь к WinInstaller.exe> [число случайных, по умолчанию 400] [seed]

   Код выхода 0 - всё совпало, 1 - есть расхождения (первые из них печатаются с различиями). */
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const {spawnSync} = require('child_process');

const [repoArg, exe, nArg, seedArg] = process.argv.slice(2);
if (!repoArg || !exe) { console.error('Использование: node devtools/gen-compare.js <generator_XML_Windows> <WinInstaller.exe> [N] [seed]'); process.exit(2); }
const repo = path.resolve(repoArg);
const {loadApp, fullState} = require(path.join(repo, 'tests', 'helpers.js'));
const app = loadApp();
const N = parseInt(nArg || '400', 10);
let seed = parseInt(seedArg || '12345', 10);
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'gencmp-'));

/* ---------- случайные состояния ---------- */
function rnd() { seed |= 0; seed = seed + 0x6D2B79F5 | 0; let t = Math.imul(seed ^ seed >>> 15, 1 | seed); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }
const pick = a => a[Math.floor(rnd() * a.length)];
const chance = p => rnd() < p;

const STR = ['', ' ', 'a', "it's", 'x&y<z>"q"', 'C:\\Tools\\', 'D:\\a b', '  pad  ', 'kms.example.com', '10.0.0.5', '256.1.1.1', '192.168.1.1', 'ABCDE-FGHIJ-KLMNO-PQRST-UVWXY',
  'abcde-fghij-klmno-pqrst-uvwxy', 'Ünï ‘q’', 'Администратор', 'Иван', 'HKLM\\SOFTWARE\\X', 'HKEY_CURRENT_USER\\Software\\Y\\', 'HKCU\\Software\\Z', "pass'word", 'Foo.Bar\nbad id;x\nFoo.Bar',
  'line1\r\nline2\n\nline3', 'a;b;c', '.iso\nvhdx\r\n', 'C:', 'd:', 'e', 'Z', 'x', '1', '03:00', '3:00', '25:99', 'Ethernet', 'PC', 'My PC', 'PC-01', '12345', 'corp.local', 'OU=a,DC=b',
  'powershell.exe', '-File "C:\\a b.ps1"', 'echo hi', 'msiexec /i a.msi /qn', 'Write-Host 1\nWrite-Host 2', 'a'.repeat(300), 'MSSQLSERVER;bad name', 'ssid\'s', '12345678', 'short', 'Pro', 'Home'];
const NUM = [0, 1, 2, 3, 5, 15, 16, 20, 24, 32, 33, 50, 99, 100, 101, 300, 500, 1000, 3389, 3390, 20000, 65535, 65536, 100000, 999999999, 1e9, -1, -5, 1.5, '12', ' 7 ', 'abc', '', null, true];
const BADS = [null, 5, 'yes', [], {}, 'x'];

const ENUM = {
  mode: ['clean', 'sysprep'], os: ['10', '11'], arch: ['amd64', 'x86', 'arm64'],
  'locale.ui': app.LANGS.map(x => x[0]), 'locale.user': app.LANGS.map(x => x[0]), 'locale.system': app.LANGS.map(x => x[0]),
  'locale.kbdDefault': app.KBDS.map(x => x[0]), 'locale.tz': app.TIMEZONES.map(x => x[0]),
  'install.editionMode': ['generic', 'custom', 'index', 'ask'], 'install.edition': app.EDITIONS.map(x => x[0]),
  'install.diskMode': ['uefi', 'bios', 'manual'], 'sys.netMode': ['workgroup', 'domain'], 'sys.netLoc': ['Work', 'Home', 'Other', ''],
  'sys.wifiAuth': ['WPA2PSK', 'open'], 'sysprep.rename': ['none', 'serial', 'mac'], 'sysprep.after': ['shutdown', 'reboot', 'quit'],
  'def.mode': ['keep', 'off', 'hard'], 'act.mode': ['none', 'kms', 'key'], 'act.edition': ['auto', ...app.GVLK.map(x => x[0])], 'apps.mgr': ['winget', 'choco'],
  'users[].group': ['Administrators', 'Users'], 'regs[].type': ['REG_SZ', 'REG_DWORD', 'REG_QWORD', 'REG_EXPAND_SZ', 'REG_MULTI_SZ', 'REG_BINARY'],
  'cmds[].phase': ['specialize', 'firstlogon'], 'cmds[].kind': ['ps', 'cmd', 'exe'],
  'tasks[].trigger': ['logon', 'startup', 'daily', 'weekly', 'once', 'interval'], 'tasks[].runAs': ['system', 'users', 'current', 'user']
};
const ITEMS = {
  users: () => ({name: '', display: '', pass: '', group: 'Administrators', desc: '', neverExp: true, hidden: false}),
  regs: () => ({key: 'HKLM\\SOFTWARE\\', name: '', type: 'REG_DWORD', data: ''}),
  cmds: () => ({desc: '', phase: 'firstlogon', kind: 'ps', body: ''}),
  tasks: () => ({name: '', program: '', args: '', workdir: '', trigger: 'logon', time: '03:00', days: ['Monday'], n: 15, runAs: 'system', user: '', pass: '', highest: true, runNow: false})
};

function mutate(tpl, p, path) {
  if (Array.isArray(tpl)) return tpl;
  if (tpl && typeof tpl === 'object') {
    const out = {};
    for (const k of Object.keys(tpl)) out[k] = mutate(tpl[k], p, path ? path + '.' + k : k);
    return out;
  }
  if (!chance(p)) return tpl;
  if (chance(0.04)) return pick(BADS);
  if (typeof tpl === 'boolean') return !tpl;
  if (typeof tpl === 'number') return pick(NUM);
  const en = ENUM[path];
  if (en) return chance(0.06) ? 'bogus' : pick(en);
  return pick(STR);
}

function fuzzState() {
  const p = pick([0.1, 0.25, 0.5, 0.8]);
  const s = mutate(app.DEFAULTS(), p, '');
  s.tweaks = {}; s.bloat = {}; s.apps.pkgs = {};
  const q = pick([0, 0.1, 0.4, 0.9]);
  app.TWEAKS.forEach(g => g.items.forEach(i => { if (chance(q)) s.tweaks[i.id] = true; }));
  app.FEATURES.forEach(f => { if (chance(q)) s.tweaks['ft_' + f[0]] = true; });
  app.BLOAT.forEach(b => { if (chance(q)) s.bloat[b[0]] = true; });
  app.PACKAGES.forEach(x => { if (chance(q)) s.apps.pkgs[x[0]] = true; });
  if (chance(0.03)) s.tweaks.bogusTweak = true;
  s.locale.kbd = app.KBDS.map(k => k[0]).filter(() => chance(0.4));
  if (chance(0.05)) s.locale.kbd = 'oops';
  for (const name of Object.keys(ITEMS)) {
    const cnt = pick([0, 0, 1, 2, 3, 6]);
    s[name] = Array.from({length: cnt}, () => {
      const it = mutate(ITEMS[name](), 0.45, name + '[]');
      if (name === 'tasks') it.days = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday', 'Bogus'].filter(() => chance(0.3));
      if (name === 'users' && chance(0.5)) it.name = pick(['User', 'bob', 'Admin', 'Администратор', 'a'.repeat(25), 'bad:name', "o'neil", 'Иван', 'bob']);
      if (name === 'regs' && chance(0.6)) it.key = pick(['HKLM\\SOFTWARE\\X', 'HKCU\\Software\\Y\\', 'HKEY_LOCAL_MACHINE\\A', 'HKU\\.DEFAULT\\B', 'bad', '', 'HKCR\\C']);
      if (chance(0.03)) return pick(BADS);
      return it;
    });
  }
  if (chance(0.5)) s.autologon.user = pick([-1, 0, 1, 2, 5, 'x']);
  if (chance(0.03)) s.install = pick(BADS);
  if (chance(0.02)) delete s.net;
  if (chance(0.02)) s.sysprep = {copyProfile: 'x'};
  return s;
}

/* ---------- набор случаев ---------- */
const cases = [];
/* Твик «Скрыть поле поиска» в Buba-Helper намеренно расширен (HideSearch.ps1 + Active Setup, см. src/Gen/GenExtras.cs) и
   с ним результат не совпадает с оригиналом, поэтому в сверке он выключается; остальное сравнивается посимвольно. */
const noSearch = s => { if (s && typeof s === 'object' && s.tweaks && typeof s.tweaks === 'object' && !Array.isArray(s.tweaks)) { s = JSON.parse(JSON.stringify(s)); s.tweaks.nosearchbox = false; } return s; };
const add = (name, state, preset) => cases.push({name, state: noSearch(state), preset});
for (const f of fs.readdirSync(path.join(repo, 'examples')).filter(f => f.endsWith('.json'))) add('example:' + f, JSON.parse(fs.readFileSync(path.join(repo, 'examples', f), 'utf8')));
for (const mode of ['clean', 'sysprep']) for (const o of ['10', '11']) {
  const d = app.DEFAULTS(); d.mode = mode; d.os = o;
  add(`defaults:${mode}:${o}`, d);
  add(`full:${mode}:${o}`, fullState(app, mode, o));
  for (const p of app.PRESETS) add(`preset:${p.id}:${mode}:${o}`, d, p.id);
}
for (let i = 0; i < N; i++) add('fuzz:' + i, fuzzState());

/* ---------- прогон ---------- */
/* Намеренные отличия Buba-Helper от оригинала. Они применяются к результату оригинала перед сверкой, чтобы всё остальное
   по-прежнему сравнивалось посимвольно (новые возможности по умолчанию выключены и в результат не попадают):
   1) команда извлечения скриптов ищет файл ответов и в Panther\Unattend\Unattend.xml (иначе при развёртывании образа
      через dism /Apply-Image скрипты не извлекались);
   2) RegSet скрывает вывод ошибок reg.exe (в окне при установке мелькал текст ошибки). */
const EXTRACT_OLD = String.raw`powershell -NoProfile -Command &quot;$x=New-Object Xml.XmlDocument;$x.Load('C:\Windows\Panther\unattend.xml');$x.SelectNodes('//*[@path]')|%{md (Split-Path $_.path) -Force|Out-Null;($_.InnerText.Trim()-split'\n')|sc $_.path -Enc UTF8}&quot;`;
const EXTRACT_NEW = String.raw`powershell -nop -c &quot;$x=[xml](gc -Raw -Enc UTF8 (gi C:\Windows\Panther\*nattend.xml,C:\Windows\Panther\*\*nattend.xml -ea 0)[0]);$x.SelectNodes('//*[@path]')|%{md (Split-Path $_.path) -Force|Out-Null;($_.InnerText.Trim()-split'\n')|sc $_.path -Enc UTF8}&quot;`;
const REGSET_OLD = '    reg.exe @a | Out-Null', REGSET_NEW = '    reg.exe @a 2>&1 | Out-Null';
const patched = xml => xml.split(EXTRACT_OLD).join(EXTRACT_NEW).split(REGSET_OLD).join(REGSET_NEW);

function js(state, preset) {
  const problems = [];
  const st = app.sanitize(JSON.parse(JSON.stringify(state)), problems);
  if (preset) app.PRESETS.find(x => x.id === preset).apply(st);
  if (st.tweaks && st.tweaks.nosearchbox) return {skip: true};   // пресет включил расширенный твик поиска - см. noSearch выше
  const res = app.generate(st);
  const log = problems.map(p => 'настройки: ' + p + '\n').join('') + res.warnings.map(w => '! ' + w.text + '\n').join('');
  return {xml: patched(res.xml), log, launcher: st.mode === 'sysprep' ? app.generateLauncher(st) : null};
}

function firstDiff(a, b) {
  const la = a.split('\n'), lb = b.split('\n');
  for (let i = 0; i < Math.max(la.length, lb.length); i++) if (la[i] !== lb[i]) return `строка ${i + 1}:\n   JS: ${JSON.stringify(la[i])}\n   C#: ${JSON.stringify(lb[i])}`;
  return 'различий по строкам нет (длина ' + a.length + ' / ' + b.length + ')';
}

let bad = 0, done = 0, skipped = 0;
for (const c of cases) {
  const inF = path.join(tmp, 'in.json'), outF = path.join(tmp, 'out.xml'), lcF = path.join(tmp, 'l.cmd');
  for (const f of [outF, outF + '.log', lcF]) try { fs.unlinkSync(f); } catch (e) {}
  const text = JSON.stringify(c.state === undefined ? null : c.state);
  fs.writeFileSync(inF, text, 'utf8');
  let exp;
  try { exp = js(c.state, c.preset); } catch (e) { exp = {error: String(e)}; }
  if (exp.skip) { skipped++; continue; }
  const args = ['--gen', inF, outF, ...(c.preset ? ['--preset', c.preset] : []), ...(exp.launcher != null ? ['--launcher', lcF] : [])];
  const r = spawnSync(exe, args, {encoding: 'utf8'});
  const problems = [];
  if (exp.error) {
    // JS упал - C# тоже обязан не создать файл без ошибки; такие случаи считаем информационными
    if (r.status === 0) problems.push('JS бросил исключение (' + exp.error.slice(0, 80) + '), а C# отработал');
  } else {
    if (r.status !== 0) problems.push('C# завершился с кодом ' + r.status + ': ' + (fs.existsSync(outF + '.log') ? fs.readFileSync(outF + '.log', 'utf8').slice(0, 400) : ''));
    else {
      const xml = fs.readFileSync(outF, 'utf8'), log = fs.readFileSync(outF + '.log', 'utf8');
      if (xml !== exp.xml) problems.push('XML отличается, ' + firstDiff(exp.xml, xml));
      if (log !== exp.log) problems.push('предупреждения отличаются, ' + firstDiff(exp.log, log));
      if (exp.launcher != null) {
        const lc = fs.readFileSync(lcF, 'utf8');
        if (lc !== exp.launcher) problems.push('Start-Sysprep.cmd отличается, ' + firstDiff(exp.launcher, lc));
      }
    }
  }
  done++;
  if (problems.length) {
    bad++;
    if (bad <= 8) { console.log(`✗ ${c.name}`); problems.forEach(p => console.log('   ' + p)); fs.writeFileSync(path.join(tmp, `fail-${bad}.json`), text, 'utf8'); }
  }
}
console.log(`\nСлучаев: ${done}, расхождений: ${bad}${bad ? `  (входные файлы упавших: ${tmp}\\fail-N.json)` : ''}`);
if (!bad) fs.rmSync(tmp, {recursive: true, force: true});
process.exit(bad ? 1 : 0);
