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
