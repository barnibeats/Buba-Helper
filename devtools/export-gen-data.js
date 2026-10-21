#!/usr/bin/env node
/* Выгружает данные генератора (каталоги, твики, пресеты) в src/Gen/gendata.json - его вшивает в exe build.cmd.
   Источник - репозиторий generator_XML_Windows (исходники в его src/).

     node devtools/export-gen-data.js <путь к generator_XML_Windows> [src/Gen/gendata.json]

   Логику пресетов (функции apply) этот файл не переносит - она написана на C# в src/Gen/GenPresets.cs
   и сверяется с оригиналом скриптом devtools/gen-compare.js. */
'use strict';
const fs = require('fs');
const path = require('path');
const Module = require('module');

const repo = process.argv[2];
const out = process.argv[3] || path.join(__dirname, '..', 'src', 'Gen', 'gendata.json');
if (!repo) { console.error('Использование: node devtools/export-gen-data.js <путь к generator_XML_Windows> [out.json]'); process.exit(2); }

const {script} = require(path.join(path.resolve(repo), 'build.js'));
const names = ['APP_VERSION', 'LANGS', 'KBDS', 'TIMEZONES', 'EDITIONS', 'GVLK', 'DAYS', 'REG_TYPES', 'FEATURES', 'BLOAT', 'PACKAGES', 'PKG_ID_RE', 'TWEAKS', 'PRESETS', 'STD_BLOAT'];
const mod = new Module('data');
mod._compile(script() + `\n;module.exports = {${names.join(', ')}};`, 'data.js');
const d = mod.exports;

const data = {
  version: d.APP_VERSION,
  langs: d.LANGS, kbds: d.KBDS, timezones: d.TIMEZONES, editions: d.EDITIONS, gvlk: d.GVLK, days: d.DAYS, regTypes: d.REG_TYPES,
  features: d.FEATURES, bloat: d.BLOAT, packages: d.PACKAGES, pkgIdRe: d.PKG_ID_RE.source, stdBloat: d.STD_BLOAT,
  tweaks: d.TWEAKS.map(g => ({g: g.g, items: g.items.map(it => ({id: it.id, label: it.label, hint: it.hint || '', warn: it.warn || '', rec: !!it.rec, os: it.os || '', lines: it.lines}))})),
  presets: d.PRESETS.map(p => ({id: p.id, title: p.title, desc: p.desc}))
};
fs.mkdirSync(path.dirname(out), {recursive: true});
fs.writeFileSync(out, JSON.stringify(data), 'utf8');
console.log(`${out}: ${(fs.statSync(out).size / 1024).toFixed(1)} КБ, версия генератора ${data.version}`);
