// node --test test/ で実行する(依存なし)
'use strict';

const test = require('node:test');
const assert = require('node:assert');
const s = require('../server.js');

test('秘密のマスク: キーの後ろ・Bearer・ランダムな長い語・32字以上の16進', () => {
  assert.strictEqual(s.mask('token=abc123'), 'token=••••');
  assert.strictEqual(s.mask('PASSWORD: hunter2'), 'PASSWORD: ••••');
  assert.strictEqual(s.mask('api_key/xyz'), 'api_key/••••');
  assert.strictEqual(s.mask('Authorization: Bearer abc.def'), 'Authorization: Bearer ••••');
  assert.strictEqual(s.mask('key aB3dE5gH7jK9mN1pQ3sT5vX'), 'key ••••');
  assert.strictEqual(s.mask(`hash ${'a1'.repeat(16)}`), 'hash ••••');
  // 普通の長い英単語やパスは残す
  assert.strictEqual(s.mask('internationalization_configuration'), 'internationalization_configuration');
  assert.strictEqual(s.mask('D:/GitHub/stable/Essentials/ClaudeOffice'), 'D:/GitHub/stable/Essentials/ClaudeOffice');
});

test('人の依頼の判定', () => {
  const u = (content, extra = {}) => ({ type: 'user', message: { content }, ...extra });
  assert.strictEqual(s.humanPrompt(u('こんにちは')), 'こんにちは');
  assert.strictEqual(s.humanPrompt(u('x', { isMeta: true })), null);
  assert.strictEqual(s.humanPrompt(u([{ type: 'tool_result', content: 'r' }])), null);
  assert.strictEqual(s.humanPrompt(u('x', { origin: { kind: 'task-notification' } })), null);
  assert.strictEqual(s.humanPrompt(u('<command-name>/review</command-name><command-args>123</command-args>')), '/review 123');
  assert.strictEqual(s.humanPrompt(u('<system-reminder>隠す</system-reminder>本文')), '本文');
  assert.strictEqual(s.humanPrompt(u('[Request interrupted by user]')), null);
  assert.strictEqual(s.humanPrompt(u('<local-command-stdout>x</local-command-stdout>')), null);
});

test('ツールの表示名はコマンド本文を出さない', () => {
  assert.strictEqual(s.toolLabel('Bash', { command: 'rm -rf x', description: '掃除' }), '掃除');
  assert.strictEqual(s.toolLabel('Edit', { file_path: 'C:\\a\\b\\app.js' }), 'app.js');
  assert.strictEqual(s.toolLabel('WebFetch', { url: 'https://example.com/a?b' }), 'example.com');
  assert.strictEqual(s.toolLabel('TodoWrite', {}), 'TODO更新');
});

test('返事待ちの判定(末尾の絵文字・記号を除いて ?/？)', () => {
  assert.ok(s.endsWithQuestion('どうしますか？🔥'));
  assert.ok(s.endsWithQuestion('OK?\n'));
  assert.ok(s.endsWithQuestion('1行目\nこれでいい？✨🌸'));
  assert.ok(!s.endsWithQuestion('できたよ🎉'));
});

test('transcript の解析と progress', () => {
  const rec = s.newRecord('11111111-1111-1111-1111-111111111111', '', 'd--GitHub-Proj');
  const t0 = Date.parse('2026-10-31T01:00:00Z');
  s.parseLine(rec, { type: 'user', timestamp: new Date(t0).toISOString(), cwd: 'D:\\GitHub\\Proj', gitBranch: 'main', message: { content: 'お願い' } });
  s.parseLine(rec, { type: 'assistant', timestamp: new Date(t0 + 1000).toISOString(), message: { model: 'm', content: [{ type: 'tool_use', name: 'Read', input: { file_path: 'x/y.txt' } }] } });
  s.parseLine(rec, { type: 'user', timestamp: new Date(t0 + 2000).toISOString(), message: { content: [{ type: 'tool_result' }] }, toolUseResult: { isAsync: true, agentId: 'ag1' } });
  s.parseLine(rec, { type: 'assistant', timestamp: new Date(t0 + 30 * 60000).toISOString(), message: { content: [{ type: 'text', text: '終わった？' }] } });
  assert.strictEqual(rec.prompts, 1);
  assert.strictEqual(rec.toolCalls, 1);
  assert.strictEqual(rec.rootCwd, 'D:\\GitHub\\Proj');
  assert.strictEqual(s.projectName(rec), 'Proj');
  // 10分を超えた間は新しい区間
  assert.strictEqual(rec.segments.length, 2);
  const live = { status: 'idle', startedAt: t0 - 1000 };
  assert.strictEqual(s.progressOf(rec, live, false).k, 'background');
  s.parseLine(rec, { type: 'queue-operation', operation: 'enqueue', content: '<task-id>ag1</task-id><status>completed</status>' });
  assert.strictEqual(s.progressOf(rec, live, false).k, 'question');
  assert.strictEqual(s.progressOf(rec, { status: 'busy', startedAt: 0 }, false).k, 'working');
});

test('notify.json の正規化と日またぎの時間帯', () => {
  const n = s.normalizeNotify({ doneText: 'x'.repeat(200), sameMinutes: 999, quietFrom: '25:00', duration: 'bad' });
  assert.strictEqual(n.doneText.length, 120);
  assert.strictEqual(n.sameMinutes, 240);
  assert.strictEqual(n.quietFrom, '22:00');
  assert.strictEqual(n.duration, 'short');
  const q = { ...n, quiet: true, quietFrom: '22:00', quietTo: '08:00' };
  assert.ok(s.inQuiet(q, new Date(2026, 0, 1, 23, 0)));
  assert.ok(s.inQuiet(q, new Date(2026, 0, 1, 7, 59)));
  assert.ok(!s.inQuiet(q, new Date(2026, 0, 1, 12, 0)));
});

test('JSONC(コメント・末尾カンマ)を読める', () => {
  const j = JSON.parse(s.stripJsonc('{\n // c\n "folders": [{"path": "a//b"},], /* x */ }'));
  assert.strictEqual(j.folders[0].path, 'a//b');
});

// ---------------------------------------------------------------- 会話ログ

const fs = require('fs');
const os = require('os');
const path = require('path');

// ANSI の色を外して中身だけ比べる
const strip = (t) => String(t).replace(/\x1b\[[0-9;?]*[A-Za-z]/g, '');

test('会話ログ: 記録1行の描き方', () => {
  const ts = '2026-11-25T01:02:00.000Z';
  const hm = `${String(new Date(ts).getHours()).padStart(2, '0')}:02`;
  const u = (content, extra = {}) => ({ type: 'user', uuid: 'u', timestamp: ts, message: { content }, ...extra });
  assert.strictEqual(strip(s.renderLogEntry(u('こんにちは\n2行目'))), `\n${hm} › こんにちは\n  2行目\n`);
  assert.match(strip(s.renderLogEntry(u([{ type: 'image' }, { type: 'text', text: '見て' }]))), /› \[画像\]\n {2}見て/);
  assert.match(strip(s.renderLogEntry(u('やあ', { origin: { kind: 'peer' } }))), /📨 別のセッションから › やあ/);
  assert.strictEqual(strip(s.renderLogEntry(u('要約', { isCompactSummary: true }))), '\n── 会話が長くなったので要約して続けたよ ──\n\n');
  assert.strictEqual(s.renderLogEntry(u('x', { isMeta: true })), null);
  assert.strictEqual(s.renderLogEntry(u([{ type: 'tool_result', content: '結果' }])), null);
  assert.strictEqual(strip(s.renderLogEntry(u('[Request interrupted by user]'))), '■ 中断したよ\n');
  assert.strictEqual(strip(s.renderLogEntry(u('<command-name>/clear</command-name><command-args></command-args>'))), '› /clear\n');
  assert.strictEqual(s.renderLogEntry(u('<local-command-stdout>x</local-command-stdout>')), null);
  assert.strictEqual(strip(s.renderLogEntry(u('<task-notification>x</task-notification>', { origin: { kind: 'task-notification' } }))), '🔔 裏の作業から知らせが届いたよ\n');
  assert.strictEqual(strip(s.renderLogEntry(u('x', { turnOrigin: 'task_notification' }))), '🔔 裏の作業から知らせが届いたよ\n');
  // VSCode が添えたタグは外して本人の発言だけ出す
  assert.match(strip(s.renderLogEntry(u('<ide_opened_file>a.js</ide_opened_file>直して'))), /› 直して\n$/);
  // 秘密はマスク、制御文字は外す、長文は切る
  assert.match(strip(s.renderLogEntry(u('token=abc123 \x1b[31m赤'))), /token=•••• \[31m赤/);
  assert.match(strip(s.renderLogEntry(u('あ'.repeat(20010)))), /…（長いので省略）/);
  const a = { type: 'assistant', uuid: 'a', message: { content: [
    { type: 'thinking', thinking: '考え中の中身' },
    { type: 'text', text: 'できたよ' },
    { type: 'tool_use', name: 'Bash', input: { command: 'rm -rf secret', description: 'ファイルを消す' } },
    { type: 'tool_use', name: 'Read', input: { file_path: 'D:/x/y.txt' } },
  ] } };
  const out = strip(s.renderLogEntry(a));
  assert.strictEqual(out, '\n● できたよ\n⎿ Bash ファイルを消す\n⎿ Read y.txt\n');
  assert.ok(!out.includes('rm -rf') && !out.includes('考え中'));
  assert.strictEqual(s.renderLogEntry({ type: 'system', uuid: 's' }), null);
});

function writeLog(lines) {
  const file = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'co-log-')), 'x.jsonl');
  fs.writeFileSync(file, lines.map((l) => JSON.stringify(l)).join('\n') + '\n');
  return file;
}

const U = (uuid, parent, text, extra = {}) => ({ type: 'user', uuid, parentUuid: parent, message: { content: text }, ...extra });
const A = (uuid, parent, text) => ({ type: 'assistant', uuid, parentUuid: parent, message: { content: [{ type: 'text', text }] } });

test('会話ログ: 枝分かれした記録は最後の発言側の1本だけ・要約の境目は logicalParentUuid', () => {
  const file = writeLog([
    U('1', null, '最初'),
    A('2', '1', '返事'),
    U('3a', '2', '古い枝'),
    A('4a', '3a', '古い枝の返事'),
    U('3b', '2', '巻き戻した枝'),
    { type: 'attachment', uuid: 'x', parentUuid: '3b' },
    A('4b', 'x', '新しい返事'),
    { type: 'system', subtype: 'compact_boundary', uuid: 'cb', parentUuid: null, logicalParentUuid: '4b' },
    U('5', 'cb', '要約', { isCompactSummary: true }),
    U('side', '5', 'サブ', { isSidechain: true }),
    U('6', '5', 'つづき'),
  ]);
  const c = s.newLogCache(file);
  assert.ok(s.readLogInto(c));
  const texts = s.logPieces(c).map((p) => strip(p.s).trim());
  assert.deepStrictEqual(s.logPieces(c).map((p) => p.u), ['1', '2', '3b', '4b', '5', '6']);
  assert.ok(texts.some((t) => t.includes('巻き戻した枝')));
  assert.ok(!texts.some((t) => t.includes('古い枝') || t.includes('サブ')));
  // 追記分だけ読む(改行なしの断片・マルチバイトの途中切れは持ち越す)
  const tail = Buffer.from(`${JSON.stringify(A('7', '6', '日本語の返事'))}\n`);
  fs.appendFileSync(file, tail.subarray(0, 20));
  assert.ok(s.readLogInto(c));
  assert.strictEqual(s.logPieces(c).length, 6);
  fs.appendFileSync(file, tail.subarray(20));
  s.readLogInto(c);
  assert.strictEqual(s.logPieces(c).at(-1).u, '7');
  assert.match(strip(s.logPieces(c).at(-1).s), /日本語の返事/);
  // 壊れた行は飛ばす
  fs.appendFileSync(file, '{壊れた\n');
  s.readLogInto(c);
  assert.strictEqual(s.logPieces(c).length, 7);
});

test('会話ログ: n/h が今の道と合えば差分、合わなければ reset で最後の 1500 件', () => {
  const pieces = Array.from({ length: 1600 }, (_, i) => ({ u: `u${i}`, s: `${i}` }));
  const d = s.sliceLog(pieces.slice(0, 10), 8, 'u7');
  assert.deepStrictEqual([d.reset, d.total, d.pieces.map((p) => p.u)], [false, 10, ['u8', 'u9']]);
  const same = s.sliceLog(pieces.slice(0, 10), 10, 'u9');
  assert.deepStrictEqual([same.reset, same.pieces.length], [false, 0]);
  const moved = s.sliceLog(pieces.slice(0, 10), 8, 'other');
  assert.deepStrictEqual([moved.reset, moved.skipped, moved.pieces.length], [true, 0, 10]);
  const first = s.sliceLog(pieces, 0, '');
  assert.deepStrictEqual([first.reset, first.skipped, first.pieces.length, first.pieces[0].u], [true, 100, 1500, 'u100']);
});

test('Host が 127.0.0.1/localhost 以外の /api/ 要求は 403', async () => {
  const call = async (host, url) => {
    const res = { code: 0, writeHead(c) { this.code = c; }, end() {}, write() {} };
    await s.handle({ method: 'GET', url, headers: { host } }, res);
    return res.code;
  };
  const port = Number(process.env.PORT) || 8765;
  assert.strictEqual(await call('evil.example', '/api/terms'), 403);
  assert.strictEqual(await call(`evil.example:${port}`, '/api/session/00000000-0000-0000-0000-000000000000/log'), 403);
  assert.strictEqual(await call(`localhost:${port}`, '/api/session/00000000-0000-0000-0000-000000000000/log'), 404);
});
