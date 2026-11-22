// LINE への通知(Messaging API の push)。チャネルアクセストークンと送り先のユーザーIDは、line-setup.ps1 が
// Windows の DPAPI(この PC・この Windows ユーザーだけが戻せる暗号)で DATA_DIR/line.dpapi に保存したものを、
// 送るときだけ powershell で戻して使う。戻した値はログにもエラーの文にも出さない。
// 環境変数 STOCK_RADAR_LINE_DRY_RUN=1 なら送らずにログに出すだけ(設定が読めるかは確かめる)。
'use strict';

const { execFile } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const PUSH_URL = 'https://api.line.me/v2/bot/message/push';
const MAX_TEXT = 5000;
const TO_PATTERN = /^[UCR][0-9a-f]{32}$/;

// line-setup.ps1 と同じ entropy(暗号のときに混ぜる値。合わないと戻せない)
const UNPROTECT = [
  "$ErrorActionPreference = 'Stop'",
  'Add-Type -AssemblyName System.Security',
  '$raw = [IO.File]::ReadAllText($env:STOCK_RADAR_LINE_FILE).Trim()',
  "$bytes = [Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($raw), [Text.Encoding]::UTF8.GetBytes('StockRadar.LINE'), 'CurrentUser')",
  '[Console]::Out.Write([Convert]::ToBase64String($bytes))',
].join('; ');

const lineFile = (dataDir) => path.join(dataDir, 'line.dpapi');
const isDryRun = () => process.env.STOCK_RADAR_LINE_DRY_RUN === '1';

function isConfigured(dataDir) {
  return fs.existsSync(lineFile(dataDir));
}

// 保存した { token, to } を戻す。失敗したときは中身に触れない文だけを返す
function readSecret(dataDir) {
  const file = lineFile(dataDir);
  if (!fs.existsSync(file)) {
    return Promise.reject(new Error('LINE の設定がありません(line-setup.ps1 で登録してください)'));
  }
  return new Promise((resolve, reject) => {
    execFile('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', UNPROTECT], {
      env: { ...process.env, STOCK_RADAR_LINE_FILE: file },
      windowsHide: true,
      timeout: 30000,
      maxBuffer: 64 * 1024,
    }, (err, stdout) => {
      if (err) {
        reject(new Error('LINE の設定を戻せませんでした(別の Windows ユーザーで登録したときなど。line-setup.ps1 で登録し直してください)'));
        return;
      }
      let secret;
      try {
        secret = JSON.parse(Buffer.from(String(stdout).trim(), 'base64').toString('utf8'));
      } catch {
        reject(new Error('LINE の設定の形が違います(line-setup.ps1 で登録し直してください)'));
        return;
      }
      if (!secret || typeof secret.token !== 'string' || secret.token.length < 20 || /\s/.test(secret.token) || !TO_PATTERN.test(String(secret.to))) {
        reject(new Error('LINE の設定の形が違います(line-setup.ps1 で登録し直してください)'));
        return;
      }
      resolve({ token: secret.token, to: secret.to });
    });
  });
}

// LINE の応答の文から、念のためトークン・ユーザーIDを消す
function redact(text, secret) {
  let out = String(text || '').slice(0, 200);
  for (const v of [secret.token, secret.to]) {
    if (v) {
      out = out.split(v).join('***');
    }
  }
  return out;
}

const STATUS_HINT = {
  400: '送り先のユーザーIDが正しくないかもしれません',
  401: 'チャネルアクセストークンが正しくないか、期限切れです',
  403: 'このチャネルでは送れません',
  429: '送信数の上限(無料プランは月200通)に達したか、短い間に送りすぎました',
};

// text を1通送る。log はサーバのログ関数(値は渡さない)
async function pushText(dataDir, text, log = () => {}) {
  const body = String(text).slice(0, MAX_TEXT);
  if (isDryRun()) {
    if (isConfigured(dataDir)) {
      const s = await readSecret(dataDir);
      log(`LINE(送らないモード) 設定は読めました(トークン ${s.token.length} 文字・送り先 ${s.to.length} 文字)`);
    } else {
      log('LINE(送らないモード) 設定はまだありません');
    }
    log(`LINE(送らないモード) 送るはずだった文:\n${body}`);
    return { dryRun: true };
  }
  const secret = await readSecret(dataDir);
  let res;
  try {
    res = await fetch(PUSH_URL, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${secret.token}`,
        'Content-Type': 'application/json',
        'X-Line-Retry-Key': crypto.randomUUID(),
      },
      body: JSON.stringify({ to: secret.to, messages: [{ type: 'text', text: body }] }),
      signal: AbortSignal.timeout(15000),
    });
  } catch (e) {
    throw new Error(`LINE につながりませんでした(${e.name === 'TimeoutError' ? '時間切れ' : 'ネットワーク'})`);
  }
  if (!res.ok) {
    let detail = '';
    try {
      detail = redact((await res.json()).message, secret);
    } catch {
      // 応答が JSON でなければ状態コードだけ
    }
    const hint = STATUS_HINT[res.status];
    throw new Error(`LINE に送れませんでした(${res.status}${hint ? `: ${hint}` : ''}${detail ? ` / ${detail}` : ''})`);
  }
  return { dryRun: false };
}

module.exports = { isConfigured, isDryRun, pushText, redact };
