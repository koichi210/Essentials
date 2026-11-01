import { IPetType } from './states';

export const CHAT_CHANCE_PER_TICK = 0.01;
export const CHAT_DURATION_MS = 3000;

// 時間帯は [開始, 終了) で、日付またぎ(20〜翌5時)も書ける
const PHRASES: Array<{ from: number; to: number; lines: string[] }> = [
    { from: 5, to: 10, lines: ['おはよ', '今日もがんばろ！'] },
    { from: 11, to: 13, lines: ['おひるごはんなに食べる？'] },
    { from: 17, to: 20, lines: ['そろそろ定時？'] },
    {
        from: 20,
        to: 5,
        lines: ['遅くまでおつかれさま', '夜ふかしはほどほどにね'],
    },
];

const ANYTIME = [
    'おなかすいた',
    'ねむい…',
    'おやつまだ？',
    'あそぼ！',
    'なでて〜',
    'がんばってるね！',
    'ひとやすみしよ？',
    '水分補給してね',
    'のびのび〜',
    'コーヒーのむ？',
    'ビルド通った？',
    'テスト全部PASSしますように',
    'それバグじゃなくて仕様？',
    'ちゃんと保存した？',
    'いい感じだね！',
];

function inRange(hour: number, from: number, to: number): boolean {
    return from <= to ? hour >= from && hour < to : hour >= from || hour < to;
}

export function phrasesFor(hour: number): string[] {
    const list = [...ANYTIME];
    for (const p of PHRASES) {
        if (inRange(hour, p.from, p.to)) {
            list.push(...p.lines);
        }
    }
    return list;
}

// 各ペットが約10秒に1回、いまの時刻に合うフレーズを話す。産卵中・掴まれている間は話さない
export function maybeChat(pets: IPetType[], now: Date = new Date()) {
    for (const pet of pets) {
        if (pet.isLayingEgg || pet.isDragged) {
            continue;
        }
        if (Math.random() < CHAT_CHANCE_PER_TICK) {
            const list = phrasesFor(now.getHours());
            pet.showSpeechBubble(
                list[Math.floor(Math.random() * list.length)],
                CHAT_DURATION_MS,
            );
        }
    }
}
