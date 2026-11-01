import { IPetType } from './states';

// 確認用の一時値(9匹で約10秒に1個)。本来値は 0.0001(1匹あたり約17分に1回)
export const EGG_CHANCE_PER_TICK = 0.0011;
export const POOP_CHANCE = 1 / 3;
export const MAX_EGGS = 5;
export const EGG_SIZE_RATIO = 1 / 2;
export const FALL_SPEED = 2.4;
export const HATCH_TICKS = 200;
export const WOBBLE_TICKS = 30;
export const CHICK_TICKS = 30;

const EGG = '🥚';
const POOP = '💩';
const CHICK = '🐣';

class Egg {
    el: HTMLDivElement;
    left: number;
    bottom: number;
    size: number;
    poop: boolean;
    groundTicks = 0;
    hatched = false;
    chickTicks = 0;
    floor: number;

    constructor(
        container: HTMLElement,
        left: number,
        bottom: number,
        size: number,
        poop: boolean,
        floor: number,
    ) {
        this.left = left;
        this.bottom = bottom;
        this.size = size;
        this.poop = poop;
        this.floor = floor;
        this.el = document.createElement('div');
        this.el.className = poop ? 'egg poop' : 'egg';
        this.el.textContent = poop ? POOP : EGG;
        this.el.style.fontSize = `${size}px`;
        this.el.style.width = `${size}px`;
        this.el.style.height = `${size}px`;
        this.el.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            this.onClick();
        });
        container.appendChild(this.el);
        this.place();
    }

    place() {
        // パネルのリサイズ後もパネル内に収める
        this.left = Math.max(
            0,
            Math.min(window.innerWidth - this.size, this.left),
        );
        this.bottom = Math.max(
            this.floor,
            Math.min(window.innerHeight - this.size, this.bottom),
        );
        this.el.style.left = `${this.left}px`;
        this.el.style.bottom = `${this.bottom}px`;
    }

    onClick() {
        if (this.poop) {
            this.groundTicks = Number.MAX_SAFE_INTEGER;
        } else if (!this.hatched) {
            this.hatch();
        }
    }

    hatch() {
        this.hatched = true;
        this.el.classList.remove('wobble');
        this.el.textContent = CHICK;
    }

    // 消してよければ true
    nextFrame(): boolean {
        if (this.bottom > this.floor) {
            this.bottom = Math.max(this.floor, this.bottom - FALL_SPEED);
            this.place();
            return false;
        }
        this.place();
        if (this.hatched) {
            this.chickTicks++;
            return this.chickTicks >= CHICK_TICKS;
        }
        this.groundTicks++;
        if (this.poop) {
            return this.groundTicks >= HATCH_TICKS;
        }
        if (this.groundTicks >= HATCH_TICKS) {
            this.hatch();
        } else if (this.groundTicks >= HATCH_TICKS - WOBBLE_TICKS) {
            this.el.classList.add('wobble');
        }
        return false;
    }
}

// 卵と💩の生成・落下・孵化。状態は保存しない(リロードで消える)
export class EggCollection {
    private eggs: Egg[] = [];

    nextFrame(pets: IPetType[], container: HTMLElement) {
        for (const pet of pets) {
            if (pet.takeLaidEgg()) {
                this.lay(pet, container);
            }
        }
        for (const pet of pets) {
            // 画面上の数＋産卵に向かっているペット数が上限に達したら生まない
            if (
                this.eggs.length + pets.filter((p) => p.isLayingEgg).length >=
                MAX_EGGS
            ) {
                break;
            }
            if (
                pet.isMoving &&
                !pet.isLayingEgg &&
                !pet.isDragged &&
                Math.random() < EGG_CHANCE_PER_TICK
            ) {
                pet.startLayingEgg();
            }
        }
        this.eggs = this.eggs.filter((egg) => {
            if (egg.nextFrame()) {
                egg.el.remove();
                return false;
            }
            return true;
        });
    }

    // ペットの中央の真下に生む
    private lay(pet: IPetType, container: HTMLElement) {
        const size = Math.max(4, Math.round(pet.width * EGG_SIZE_RATIO));
        const poop = Math.random() < POOP_CHANCE;
        const left = pet.left + pet.width / 2 - size / 2;
        this.eggs.push(
            new Egg(container, left, pet.bottom - size, size, poop, pet.floor),
        );
        pet.showSpeechBubble(poop ? `${POOP}!` : `${EGG}!`, 1500);
    }

    get count(): number {
        return this.eggs.length;
    }
}
