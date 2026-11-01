import { PetColor, PetType } from '../common/types';

export interface IPetType {
    nextFrame(): void;

    // Special methods for actions
    canSwipe: boolean;
    canChase: boolean;
    swipe(): void;
    chase(ballState: BallState, canvas: HTMLCanvasElement): void;
    speed: number;
    climbSpeed: number;
    climbHeight: number;
    fallSpeed: number;
    isMoving: boolean;
    hello: string;

    // State API
    getState(): PetInstanceState;
    recoverState(state: PetInstanceState): void;
    recoverFriend(friend: IPetType): void;

    // Positioning
    bottom: number;
    left: number;
    positionBottom(bottom: number): void;
    positionLeft(left: number): void;
    width: number;
    floor: number;

    // Friends API
    name: string;
    emoji: string;
    hasFriend: boolean;
    friend: IPetType | undefined;
    makeFriendsWith(friend: IPetType): boolean;
    isPlaying: boolean;

    showSpeechBubble(message: string, duration: number): void;
    remove(): void;

    // 飛行改造で増えた API(マウスで掴む・産卵・ペット同士の回避)
    startDrag(): void;
    endDrag(): void;
    isLayingEgg: boolean;
    isDragged: boolean;
    startLayingEgg(): void;
    takeLaidEgg(): boolean;
    steerAwayFrom(other: IPetType): void;
    currentStateEnum: States;
    currentState: IState;
}

export class PetInstanceState {
    currentStateEnum: States | undefined;
}

export class PetElementState {
    petState: PetInstanceState | undefined;
    petType: PetType | undefined;
    petColor: PetColor | undefined;
    elLeft: string | undefined;
    elBottom: string | undefined;
    petName: string | undefined;
    petFriend: string | undefined;
}

export class PetPanelState {
    petStates: Array<PetElementState> | undefined;
    petCounter: number | undefined;
}

export enum HorizontalDirection {
    left,
    right,
    natural, // No change to current direction
}

export const enum States {
    sitIdle = 'sit-idle',
    walkRight = 'walk-right',
    walkLeft = 'walk-left',
    runRight = 'run-right',
    runLeft = 'run-left',
    lie = 'lie',
    wallHangLeft = 'wall-hang-left',
    wallDigLeft = 'wall-dig-left',
    wallNap = 'wall-nap',
    climbWallLeft = 'climb-wall-left',
    jumpDownLeft = 'jump-down-left',
    land = 'land',
    swipe = 'swipe',
    idleWithBall = 'idle-with-ball',
    chase = 'chase',
    chaseFriend = 'chase-friend',
    standRight = 'stand-right',
    standLeft = 'stand-left',
    // 飛行改造の追加ステート
    fly = 'fly',
    hover = 'hover',
    flyDown = 'fly-down',
    flyToLay = 'fly-to-lay',
    dragged = 'dragged',
}

export enum FrameResult {
    stateContinue,
    stateComplete,
    // Special states
    stateCancel,
}

export class BallState {
    cx: number;
    cy: number;
    vx: number;
    vy: number;
    paused: boolean;

    constructor(cx: number, cy: number, vx: number, vy: number) {
        this.cx = cx;
        this.cy = cy;
        this.vx = vx;
        this.vy = vy;
        this.paused = false;
    }
}

export function isStateAboveGround(state: States): boolean {
    return (
        state === States.climbWallLeft ||
        state === States.wallDigLeft ||
        state === States.wallNap ||
        state === States.jumpDownLeft ||
        state === States.land ||
        state === States.wallHangLeft ||
        // 飛行中と掴まれている間は、状態の復元時に床へ戻さない
        state === States.dragged ||
        isStateFlying(state)
    );
}

export function isStateFlying(state: States): boolean {
    return (
        state === States.fly ||
        state === States.hover ||
        state === States.flyDown ||
        state === States.flyToLay
    );
}

// スプライトは正方形として扱う。毎回 window を読むのでパネルのリサイズに即追従する
export function maxLeftInWindow(pet: IPetType): number {
    return Math.max(0, window.innerWidth - pet.width);
}

export function maxBottomInWindow(pet: IPetType): number {
    return Math.max(pet.floor, window.innerHeight - pet.width);
}

export function resolveState(state: string, pet: IPetType): IState {
    switch (state) {
        case States.sitIdle:
            return new SitIdleState(pet);
        case States.walkRight:
            return new WalkRightState(pet);
        case States.walkLeft:
            return new WalkLeftState(pet);
        case States.runRight:
            return new RunRightState(pet);
        case States.runLeft:
            return new RunLeftState(pet);
        case States.lie:
            return new LieState(pet);
        case States.wallHangLeft:
            return new WallHangLeftState(pet);
        case States.wallDigLeft:
            return new WallDigLeftState(pet);
        case States.wallNap:
            return new WallNapState(pet);
        case States.climbWallLeft:
            return new ClimbWallLeftState(pet);
        case States.jumpDownLeft:
            return new JumpDownLeftState(pet);
        case States.land:
            return new LandState(pet);
        case States.swipe:
            return new SwipeState(pet);
        case States.idleWithBall:
            return new IdleWithBallState(pet);
        case States.chaseFriend:
            return new ChaseFriendState(pet);
        case States.standRight:
            return new StandRightState(pet);
        case States.standLeft:
            return new StandLeftState(pet);
        case States.fly:
            return new FlyState(pet);
        case States.hover:
            return new HoverState(pet);
        case States.flyDown:
            return new FlyDownState(pet);
        case States.flyToLay:
            return new FlyToLayState(pet);
        case States.dragged:
            return new DraggedState(pet);
    }
    return new SitIdleState(pet);
}

export interface IState {
    label: string;
    spriteLabel: string;
    horizontalDirection: HorizontalDirection;
    pet: IPetType;
    nextFrame(): FrameResult;
}

class AbstractStaticState implements IState {
    label = States.sitIdle;
    idleCounter: number;
    spriteLabel = 'idle';
    holdTime = 50;
    pet: IPetType;

    horizontalDirection = HorizontalDirection.left;

    constructor(pet: IPetType) {
        this.idleCounter = 0;
        this.pet = pet;
    }

    nextFrame(): FrameResult {
        this.idleCounter++;
        if (this.idleCounter > this.holdTime) {
            return FrameResult.stateComplete;
        }
        return FrameResult.stateContinue;
    }
}

export class SitIdleState extends AbstractStaticState {
    label = States.sitIdle;
    spriteLabel = 'idle';
    horizontalDirection = HorizontalDirection.right;
    holdTime = 50;
}

export class LieState extends AbstractStaticState {
    label = States.lie;
    spriteLabel = 'lie';
    horizontalDirection = HorizontalDirection.right;
    holdTime = 50;
}

export class WallHangLeftState extends AbstractStaticState {
    label = States.wallHangLeft;
    spriteLabel = 'wallgrab';
    horizontalDirection = HorizontalDirection.left;
    holdTime = 50;
}

export class WallDigLeftState extends AbstractStaticState {
    label = States.wallDigLeft;
    spriteLabel = 'walldig';
    horizontalDirection = HorizontalDirection.left;
    holdTime = 60;
}

export class WallNapState extends AbstractStaticState {
    label = States.wallNap;
    spriteLabel = 'wallnap';
    horizontalDirection = HorizontalDirection.right;
    holdTime = 50;
}

export class LandState extends AbstractStaticState {
    label = States.land;
    spriteLabel = 'land';
    horizontalDirection = HorizontalDirection.left;
    holdTime = 10;
}

export class SwipeState extends AbstractStaticState {
    label = States.swipe;
    spriteLabel = 'swipe';
    horizontalDirection = HorizontalDirection.natural;
    holdTime = 15;
}

export class IdleWithBallState extends AbstractStaticState {
    label = States.idleWithBall;
    spriteLabel = 'with_ball';
    horizontalDirection = HorizontalDirection.left;
    holdTime = 30;
}

export class WalkRightState implements IState {
    label = States.walkRight;
    pet: IPetType;
    spriteLabel = 'walk';
    horizontalDirection = HorizontalDirection.right;
    leftBoundary: number;
    speedMultiplier = 1;
    idleCounter: number;
    holdTime = 60;

    constructor(pet: IPetType) {
        this.leftBoundary = Math.floor(window.innerWidth * 0.95);
        this.pet = pet;
        this.idleCounter = 0;
    }

    nextFrame(): FrameResult {
        this.idleCounter++;
        const newLeft = this.pet.left + this.pet.speed * this.speedMultiplier;
        const maxRight = this.leftBoundary - this.pet.width;
        this.pet.positionLeft(newLeft > maxRight ? maxRight : newLeft);
        if (this.pet.isMoving && this.pet.left >= maxRight) {
            return FrameResult.stateComplete;
        } else if (!this.pet.isMoving && this.idleCounter > this.holdTime) {
            return FrameResult.stateComplete;
        }
        return FrameResult.stateContinue;
    }
}

export class WalkLeftState implements IState {
    label = States.walkLeft;
    spriteLabel = 'walk';
    horizontalDirection = HorizontalDirection.left;
    pet: IPetType;
    speedMultiplier = 1;
    idleCounter: number;
    holdTime = 60;

    constructor(pet: IPetType) {
        this.pet = pet;
        this.idleCounter = 0;
    }

    nextFrame(): FrameResult {
        this.idleCounter++;
        const newLeft = this.pet.left - this.pet.speed * this.speedMultiplier;
        this.pet.positionLeft(newLeft < 0 ? 0 : newLeft);
        if (this.pet.isMoving && this.pet.left <= 0) {
            return FrameResult.stateComplete;
        } else if (!this.pet.isMoving && this.idleCounter > this.holdTime) {
            return FrameResult.stateComplete;
        }
        return FrameResult.stateContinue;
    }
}

export class RunRightState extends WalkRightState {
    label = States.runRight;
    spriteLabel = 'walk_fast';
    speedMultiplier = 1.6;
    holdTime = 130;
}

export class RunLeftState extends WalkLeftState {
    label = States.runLeft;
    spriteLabel = 'walk_fast';
    speedMultiplier = 1.6;
    holdTime = 130;
}

export class ChaseState implements IState {
    label = States.chase;
    spriteLabel = 'run';
    horizontalDirection = HorizontalDirection.left;
    ballState: BallState;
    canvas: HTMLCanvasElement;
    pet: IPetType;

    constructor(
        pet: IPetType,
        ballState: BallState,
        canvas: HTMLCanvasElement,
    ) {
        this.pet = pet;
        this.ballState = ballState;
        this.canvas = canvas;
    }

    nextFrame(): FrameResult {
        if (this.ballState.paused) {
            return FrameResult.stateCancel; // Ball is already caught
        }
        if (this.pet.left > this.ballState.cx) {
            this.horizontalDirection = HorizontalDirection.left;
            this.pet.positionLeft(this.pet.left - this.pet.speed);
        } else {
            this.horizontalDirection = HorizontalDirection.right;
            this.pet.positionLeft(this.pet.left + this.pet.speed);
        }

        if (
            this.canvas.height - this.ballState.cy <
                this.pet.width + this.pet.floor &&
            this.ballState.cx < this.pet.left &&
            this.pet.left < this.ballState.cx + 15
        ) {
            // hide ball
            this.canvas.style.display = 'none';
            this.ballState.paused = true;
            return FrameResult.stateComplete;
        }
        return FrameResult.stateContinue;
    }
}

export class ChaseFriendState implements IState {
    label = States.chaseFriend;
    spriteLabel = 'run';
    horizontalDirection = HorizontalDirection.left;
    pet: IPetType;

    constructor(pet: IPetType) {
        this.pet = pet;
    }

    nextFrame(): FrameResult {
        if (!this.pet.hasFriend || !this.pet.friend?.isPlaying) {
            return FrameResult.stateCancel; // Friend is no longer playing.
        }
        // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
        if (this.pet.left > this.pet.friend!.left) {
            this.horizontalDirection = HorizontalDirection.left;
            this.pet.positionLeft(this.pet.left - this.pet.speed);
        } else {
            this.horizontalDirection = HorizontalDirection.right;
            this.pet.positionLeft(this.pet.left + this.pet.speed);
        }

        return FrameResult.stateContinue;
    }
}

export class ClimbWallLeftState implements IState {
    label = States.climbWallLeft;
    spriteLabel = 'wallclimb';
    horizontalDirection = HorizontalDirection.left;
    pet: IPetType;

    constructor(pet: IPetType) {
        this.pet = pet;
    }

    nextFrame(): FrameResult {
        // パネルが低いときは上端で止める
        const limit = Math.min(
            this.pet.climbHeight,
            maxBottomInWindow(this.pet),
        );
        this.pet.positionBottom(
            Math.min(limit, this.pet.bottom + this.pet.climbSpeed),
        );
        if (this.pet.bottom >= limit) {
            return FrameResult.stateComplete;
        }
        return FrameResult.stateContinue;
    }
}

export class JumpDownLeftState implements IState {
    label = States.jumpDownLeft;
    spriteLabel = 'fall_from_grab';
    horizontalDirection = HorizontalDirection.right;
    pet: IPetType;

    constructor(pet: IPetType) {
        this.pet = pet;
    }

    nextFrame(): FrameResult {
        this.pet.positionBottom(this.pet.bottom - this.pet.fallSpeed);
        if (this.pet.bottom <= this.pet.floor) {
            this.pet.positionBottom(this.pet.floor);
            return FrameResult.stateComplete;
        }
        return FrameResult.stateContinue;
    }
}

export class StandRightState extends AbstractStaticState {
    label = States.standRight;
    spriteLabel = 'stand';
    horizontalDirection = HorizontalDirection.right;
    holdTime = 60;
}

export class StandLeftState extends AbstractStaticState {
    label = States.standLeft;
    spriteLabel = 'stand';
    horizontalDirection = HorizontalDirection.left;
    holdTime = 60;
}

// ---------------------------------------------------------------- 飛行改造(Flying)

// パネル内のランダムな点へ直線で飛ぶ。到達で完了
export class FlyState implements IState {
    label: States = States.fly;
    spriteLabel = 'run';
    horizontalDirection = HorizontalDirection.right;
    pet: IPetType;
    targetLeft = 0;
    targetBottom = 0;

    constructor(pet: IPetType) {
        this.pet = pet;
        this.pickTarget();
    }

    // 生成時と回避時に呼ぶ
    pickTarget(): void {
        const maxL = Math.max(
            0,
            Math.floor(window.innerWidth * 0.95) - this.pet.width,
        );
        const maxB = maxBottomInWindow(this.pet);
        const minB = this.pet.floor + (maxB - this.pet.floor) * 0.2;
        this.targetLeft = Math.random() * maxL;
        this.targetBottom = this.fixedBottom(
            minB + Math.random() * (maxB - minB),
        );
        this.horizontalDirection =
            this.targetLeft < this.pet.left
                ? HorizontalDirection.left
                : HorizontalDirection.right;
    }

    protected fixedBottom(bottom: number): number {
        return bottom;
    }

    // 目的地が相手の方向(内積 > 0)にあるか
    isHeadingTo(other: IPetType): boolean {
        const dx = this.targetLeft - this.pet.left;
        const dy = this.targetBottom - this.pet.bottom;
        const ox = other.left - this.pet.left;
        const oy = other.bottom - this.pet.bottom;
        return dx * ox + dy * oy > 0;
    }

    nextFrame(): FrameResult {
        if (!this.pet.isMoving) {
            return FrameResult.stateComplete;
        }
        const step = this.pet.speed * 1.5;
        const dx = this.targetLeft - this.pet.left;
        const dy = this.targetBottom - this.pet.bottom;
        const d = Math.hypot(dx, dy);
        if (d <= step) {
            this.pet.positionLeft(this.targetLeft);
            this.pet.positionBottom(this.targetBottom);
            return FrameResult.stateComplete;
        }
        this.pet.positionLeft(Math.max(0, this.pet.left + (dx / d) * step));
        this.pet.positionBottom(
            Math.max(this.pet.floor, this.pet.bottom + (dy / d) * step),
        );
        return FrameResult.stateContinue;
    }
}

// fly と同じだが、目的地の高さをパネル上端に固定する。到達で産卵
export class FlyToLayState extends FlyState {
    label: States = States.flyToLay;

    protected fixedBottom(): number {
        return maxBottomInWindow(this.pet);
    }
}

// 空中で静止する
export class HoverState extends AbstractStaticState {
    label = States.hover;
    spriteLabel = 'idle';
    horizontalDirection = HorizontalDirection.natural;
    holdTime = 40;
}

// 真下へ降りる。床に着いたら完了
export class FlyDownState implements IState {
    label = States.flyDown;
    spriteLabel = 'walk';
    horizontalDirection = HorizontalDirection.natural;
    pet: IPetType;

    constructor(pet: IPetType) {
        this.pet = pet;
    }

    nextFrame(): FrameResult {
        const next = this.pet.bottom - Math.max(this.pet.speed, 1);
        if (next <= this.pet.floor) {
            this.pet.positionBottom(this.pet.floor);
            return FrameResult.stateComplete;
        }
        this.pet.positionBottom(next);
        return FrameResult.stateContinue;
    }
}

// マウスで掴まれている間。自分では完了しない
export class DraggedState implements IState {
    label = States.dragged;
    spriteLabel = 'run';
    horizontalDirection = HorizontalDirection.natural;
    pet: IPetType;

    constructor(pet: IPetType) {
        this.pet = pet;
    }

    nextFrame(): FrameResult {
        return FrameResult.stateContinue;
    }
}
