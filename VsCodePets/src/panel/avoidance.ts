import {
    IPetType,
    isStateFlying,
    maxBottomInWindow,
    maxLeftInWindow,
    States,
} from './states';

const STEER_DISTANCE_RATIO = 0.6;
const PUSH_DISTANCE_RATIO = 0.5;
const MAX_PUSH = 2;

// 壁にいる・掴まれている・動かないペットは押さない(押すと壁から剥がれたり、手から離れたりするため)
function canBePushed(pet: IPetType): boolean {
    if (!pet.isMoving || pet.isDragged) {
        return false;
    }
    switch (pet.currentStateEnum) {
        case States.climbWallLeft:
        case States.wallHangLeft:
        case States.wallDigLeft:
        case States.wallNap:
            return false;
    }
    return true;
}

function push(pet: IPetType, dx: number, dy: number) {
    if (!canBePushed(pet)) {
        return;
    }
    pet.positionLeft(
        Math.max(0, Math.min(maxLeftInWindow(pet), pet.left + dx)),
    );
    // 地上は横にだけ、飛行中は縦にも押す
    if (isStateFlying(pet.currentStateEnum)) {
        pet.positionBottom(
            Math.max(
                pet.floor,
                Math.min(maxBottomInWindow(pet), pet.bottom + dy),
            ),
        );
    }
}

// 全ペットの組で毎 tick: 近ければ進路を変え、重なっていれば押し離す
export function keepApart(pets: IPetType[]) {
    for (let i = 0; i < pets.length; i++) {
        for (let j = i + 1; j < pets.length; j++) {
            const a = pets[i];
            const b = pets[j];
            const width = Math.max(a.width, b.width, 1);
            const dx = b.left - a.left;
            const dy = b.bottom - a.bottom;
            if (
                Math.abs(dx) < width * STEER_DISTANCE_RATIO &&
                Math.abs(dy) < width * STEER_DISTANCE_RATIO
            ) {
                a.steerAwayFrom(b);
                b.steerAwayFrom(a);
            }
            const d = Math.hypot(dx, dy);
            const limit = width * PUSH_DISTANCE_RATIO;
            if (d < limit) {
                // 近いほど強く押す。完全に重なったら向きはランダム
                let ux: number;
                let uy: number;
                if (d < 0.001) {
                    const angle = Math.random() * Math.PI * 2;
                    ux = Math.cos(angle);
                    uy = Math.sin(angle);
                } else {
                    ux = dx / d;
                    uy = dy / d;
                }
                const strength = (MAX_PUSH * (limit - d)) / limit;
                push(a, -ux * strength, -uy * strength);
                push(b, ux * strength, uy * strength);
            }
        }
    }
}
