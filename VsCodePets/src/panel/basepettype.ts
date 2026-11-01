import { PetColor, PetSize, PetSpeed } from '../common/types';
import { IPetType } from './states';
import { ISequenceTree } from './sequences';
import {
    States,
    IState,
    resolveState,
    PetInstanceState,
    isStateAboveGround,
    BallState,
    ChaseState,
    HorizontalDirection,
    FrameResult,
    FlyState,
    isStateFlying,
    maxLeftInWindow,
    maxBottomInWindow,
} from './states';

// 地上から飛び立つ確率と、ホバリング後にもう一度飛ぶ確率
export const TAKE_OFF_CHANCE = 0.8;
export const FLY_AGAIN_CHANCE = 0.7;
// 進路を変えたら、しばらくは変えない(向きを変え続けてガタガタしないため)
const STEER_COOLDOWN_TICKS = 30;
const MAX_RETARGET = 10;

export class InvalidStateError extends Error {
    fromState: States;
    petType: string;

    constructor(fromState: States, petType: string) {
        super(`Invalid state ${fromState} for pet type ${petType}`);
        this.fromState = fromState;
        this.petType = petType;
    }
}

export abstract class BasePetType implements IPetType {
    label: string = 'base';
    static count: number = 0;
    sequence: ISequenceTree = {
        startingState: States.sitIdle,
        sequenceStates: [],
    };
    static possibleColors: PetColor[];
    currentState: IState;
    currentStateEnum: States;
    holdState: IState | undefined;
    holdStateEnum: States | undefined;
    private el: HTMLImageElement;
    private collision: HTMLDivElement;
    private speech: HTMLDivElement;
    private _left: number;
    private _bottom: number;
    petRoot: string;
    _floor: number;
    _friend: IPetType | undefined;
    private _name: string;
    private _speed: number;
    private _size: PetSize;
    protected _climbSpeed: number = 1;
    protected _climbHeight: number = 100;
    protected _fallSpeed: number = 5;
    private _layingEgg = false;
    private _eggReady = false;
    private _steerCooldown = 0;
    private _speechTimer: ReturnType<typeof setTimeout> | undefined;

    constructor(
        spriteElement: HTMLImageElement,
        collisionElement: HTMLDivElement,
        speechElement: HTMLDivElement,
        size: PetSize,
        left: number,
        bottom: number,
        petRoot: string,
        floor: number,
        name: string,
        speed: number,
    ) {
        this.el = spriteElement;
        this.collision = collisionElement;
        this.speech = speechElement;
        this.petRoot = petRoot;
        this._floor = floor;
        this._left = left;
        this._bottom = bottom;
        this.initSprite(size, left, bottom);
        this.currentStateEnum = this.sequence.startingState;
        this.currentState = resolveState(this.currentStateEnum, this);

        this._name = name;
        this._size = size;
        this._speed = this.randomizeSpeed(speed);
        if (this._name.toLowerCase() === 'debug') {
            console.log(
                `Creating pet ${this._name} of size ${this._size} at position (${this._left}, ${this._bottom}) with speed ${this._speed}`,
            );
        }

        // Increment the static count of the Pet class that the constructor belongs to
        (this.constructor as any).count += 1;
    }

    initSprite(petSize: PetSize, left: number, bottom: number) {
        this.el.style.left = `${left}px`;
        this.el.style.bottom = `${bottom}px`;
        this.el.style.width = 'auto';
        this.el.style.height = 'auto';
        this.el.style.maxWidth = `${this.calculateSpriteWidth(petSize)}px`;
        this.el.style.maxHeight = `${this.calculateSpriteWidth(petSize)}px`;
        this.collision.style.left = `${left}px`;
        this.collision.style.bottom = `${bottom}px`;
        this.collision.style.width = `${this.calculateSpriteWidth(petSize)}px`;
        this.collision.style.height = `${this.calculateSpriteWidth(petSize)}px`;
        this.speech.style.left = `${left}px`;
        this.speech.style.bottom = `${
            bottom + this.calculateSpriteWidth(petSize)
        }px`;
        this.hideSpeechBubble();
    }

    get left(): number {
        return this._left;
    }

    get bottom(): number {
        return this._bottom;
    }

    private repositionAccompanyingElements() {
        this.collision.style.left = `${this._left}px`;
        this.collision.style.bottom = `${this._bottom}px`;
        this.speech.style.left = `${this._left}px`;
        this.speech.style.bottom = `${
            this._bottom + this.calculateSpriteWidth(this._size)
        }px`;
    }

    calculateSpriteWidth(size: PetSize): number {
        if (size === PetSize.nano) {
            return 30;
        } else if (size === PetSize.small) {
            return 40;
        } else if (size === PetSize.medium) {
            return 55;
        } else if (size === PetSize.large) {
            return 110;
        } else {
            return 30; // Shrug
        }
    }

    positionBottom(bottom: number): void {
        this._bottom = bottom;
        this.el.style.bottom = `${this._bottom}px`;
        this.repositionAccompanyingElements();
    }

    positionLeft(left: number): void {
        this._left = left;
        this.el.style.left = `${this._left}px`;
        this.repositionAccompanyingElements();
    }

    get width(): number {
        return this.el.width;
    }

    get floor(): number {
        return this._floor;
    }

    get hello(): string {
        // return the sound of the name of the animal
        return ` says hello 👋!`;
    }

    getState(): PetInstanceState {
        return { currentStateEnum: this.currentStateEnum };
    }

    get speed(): number {
        return this._speed;
    }

    randomizeSpeed(speed: number): number {
        const min = speed * 0.7;
        const max = speed * 1.3;
        const newSpeed = Math.random() * (max - min) + min;
        return newSpeed;
    }

    /**
     * The speed at which the pet can climb.
     * Default is 1.
     */
    get climbSpeed(): number {
        return this._climbSpeed;
    }

    /**
     * The height to which a pet can climb.
     * Default is 100.
     */
    get climbHeight(): number {
        return this._climbHeight;
    }

    /**
     * The speed at which the pet falls when it is in the air.
     * Default is 5.
     */
    get fallSpeed(): number {
        return this._fallSpeed;
    }

    get isMoving(): boolean {
        return this._speed !== PetSpeed.still;
    }

    recoverFriend(friend: IPetType) {
        // Recover friends..
        this._friend = friend;
    }

    recoverState(state: PetInstanceState) {
        // TODO : Resolve a bug where if it was swiping before, it would fail
        // because holdState is no longer valid.
        this.currentStateEnum = state.currentStateEnum ?? States.sitIdle;
        // 掴まれたまま保存されていたら、その場から降りる
        if (this.currentStateEnum === States.dragged) {
            this.currentStateEnum = States.flyDown;
        }
        // 卵の状態は保存しないので、産卵の途中だったものは普通の飛行として続ける
        if (this.currentStateEnum === States.flyToLay) {
            this.currentStateEnum = States.fly;
        }
        this.currentState = resolveState(this.currentStateEnum, this);

        if (!isStateAboveGround(this.currentStateEnum)) {
            // Reset the bottom of the sprite to the floor as the theme
            // has likely changed.
            this.positionBottom(this.floor);
        }
    }

    get canSwipe() {
        return !isStateAboveGround(this.currentStateEnum);
    }

    get canChase() {
        return !isStateAboveGround(this.currentStateEnum) && this.isMoving;
    }

    showSpeechBubble(message: string, duration: number = 3000) {
        // 前の吹き出しのタイマーが新しい吹き出しを消さないようにする
        if (this._speechTimer !== undefined) {
            clearTimeout(this._speechTimer);
        }
        this.speech.innerHTML = message;
        this.speech.style.display = 'block';
        this.fitSpeechBubble();
        this._speechTimer = setTimeout(() => {
            this._speechTimer = undefined;
            this.hideSpeechBubble();
        }, duration);
    }

    // 吹き出しは表示後の実サイズでパネル内に収める
    private fitSpeechBubble() {
        if (this.speech.style.display !== 'block') {
            return;
        }
        const w = this.speech.offsetWidth;
        const h = this.speech.offsetHeight;
        const left = Math.max(0, Math.min(this._left, window.innerWidth - w));
        const bottom = Math.min(
            this._bottom + this.calculateSpriteWidth(this._size),
            Math.max(0, window.innerHeight - h),
        );
        this.speech.style.left = `${left}px`;
        this.speech.style.bottom = `${bottom}px`;
    }

    hideSpeechBubble() {
        this.speech.style.display = 'none';
    }

    swipe() {
        if (this.currentStateEnum === States.swipe) {
            return;
        }
        this.holdState = this.currentState;
        this.holdStateEnum = this.currentStateEnum;
        this.currentStateEnum = States.swipe;
        this.currentState = resolveState(this.currentStateEnum, this);
        this.showSpeechBubble('👋');
    }

    chase(ballState: BallState, canvas: HTMLCanvasElement) {
        this.currentStateEnum = States.chase;
        this.currentState = new ChaseState(this, ballState, canvas);
    }

    faceLeft() {
        this.el.style.transform = 'scaleX(-1)';
    }

    faceRight() {
        this.el.style.transform = 'scaleX(1)';
    }

    setAnimation(face: string) {
        if (this.el.src.endsWith(`_${face}_8fps.gif`)) {
            return;
        }
        this.el.src = `${this.petRoot}_${face}_8fps.gif`;
    }

    // 飛行の遷移。undefined なら上流の sequence 抽選へ進む
    chooseFlyingState(fromState: States): States | undefined {
        switch (fromState) {
            case States.fly:
                return States.hover;
            case States.flyToLay:
                this._eggReady = true;
                return States.hover;
            case States.hover:
                return Math.random() < FLY_AGAIN_CHANCE
                    ? States.fly
                    : States.flyDown;
            case States.flyDown:
                return this.sequence.startingState;
        }
        // 壁登り・落下中からは離陸しない
        if (
            !isStateAboveGround(fromState) &&
            this.isMoving &&
            Math.random() < TAKE_OFF_CHANCE
        ) {
            return States.fly;
        }
        return undefined;
    }

    chooseNextState(fromState: States): States {
        const flying = this.chooseFlyingState(fromState);
        if (flying !== undefined) {
            return flying;
        }
        // Work out next state
        var possibleNextStates: States[] | undefined = undefined;
        for (var i = 0; i < this.sequence.sequenceStates.length; i++) {
            if (this.sequence.sequenceStates[i].state === fromState) {
                possibleNextStates =
                    this.sequence.sequenceStates[i].possibleNextStates;
                break;
            }
        }
        if (!possibleNextStates) {
            throw new InvalidStateError(fromState, this.label);
        }
        // randomly choose the next state
        const idx = Math.floor(Math.random() * possibleNextStates.length);
        return possibleNextStates[idx];
    }

    nextFrame() {
        this.stepFrame();
        this.keepInsideWindow();
        this.fitSpeechBubble();
    }

    // パネルのリサイズ対策: 掴まれている間以外は、毎フレーム最後にパネル内へ戻す
    keepInsideWindow() {
        if (this.currentStateEnum === States.dragged) {
            return;
        }
        const maxLeft = maxLeftInWindow(this);
        if (this._left < 0 || this._left > maxLeft) {
            this.positionLeft(Math.max(0, Math.min(this._left, maxLeft)));
        }
        const maxBottom = maxBottomInWindow(this);
        if (this._bottom > maxBottom) {
            this.positionBottom(maxBottom);
        }
    }

    private stepFrame() {
        if (
            this.currentState.horizontalDirection === HorizontalDirection.left
        ) {
            this.faceLeft();
        } else if (
            this.currentState.horizontalDirection === HorizontalDirection.right
        ) {
            this.faceRight();
        }
        this.setAnimation(this.currentState.spriteLabel);

        // What's my buddy doing?
        if (
            this.hasFriend &&
            this.currentStateEnum !== States.chaseFriend &&
            this.isMoving
        ) {
            if (
                this.friend?.isPlaying &&
                !isStateAboveGround(this.currentStateEnum)
            ) {
                this.currentState = resolveState(States.chaseFriend, this);
                this.currentStateEnum = States.chaseFriend;
                return;
            }
        }

        var frameResult = this.currentState.nextFrame();
        if (this._steerCooldown > 0) {
            this._steerCooldown--;
        }
        if (frameResult === FrameResult.stateComplete) {
            // If recovering from swipe..
            if (this.holdState && this.holdStateEnum) {
                this.currentState = this.holdState;
                this.currentStateEnum = this.holdStateEnum;
                this.holdState = undefined;
                this.holdStateEnum = undefined;
                return;
            }

            var nextState = this.chooseNextState(this.currentStateEnum);
            this.currentState = resolveState(nextState, this);
            this.currentStateEnum = nextState;
        } else if (frameResult === FrameResult.stateCancel) {
            if (this.currentStateEnum === States.chase) {
                var nextState = this.chooseNextState(States.idleWithBall);
                this.currentState = resolveState(nextState, this);
                this.currentStateEnum = nextState;
            } else if (this.currentStateEnum === States.chaseFriend) {
                var nextState = this.chooseNextState(States.idleWithBall);
                this.currentState = resolveState(nextState, this);
                this.currentStateEnum = nextState;
            }
        }
    }

    get hasFriend(): boolean {
        return this._friend !== undefined;
    }

    get friend(): IPetType | undefined {
        return this._friend;
    }

    get name(): string {
        return this._name;
    }

    makeFriendsWith(friend: IPetType): boolean {
        this._friend = friend;
        console.log(this.name, ": I'm now friends ❤️ with ", friend.name);
        return true;
    }

    // ---------------------------------------------------------------- マウスで掴む

    get isDragged(): boolean {
        return this.currentStateEnum === States.dragged;
    }

    startDrag() {
        // スワイプ等の保留ステートを捨て、吹き出しを消してから掴まれた状態へ
        this.holdState = undefined;
        this.holdStateEnum = undefined;
        this._layingEgg = false;
        this._eggReady = false;
        this.hideSpeechBubble();
        this.currentStateEnum = States.dragged;
        this.currentState = resolveState(States.dragged, this);
    }

    endDrag() {
        const next =
            this._bottom > this.floor
                ? States.flyDown
                : this.sequence.startingState;
        this.currentStateEnum = next;
        this.currentState = resolveState(next, this);
    }

    // ---------------------------------------------------------------- 産卵

    get isLayingEgg(): boolean {
        return this._layingEgg;
    }

    startLayingEgg() {
        if (!this.isMoving || this._layingEgg || this.isDragged) {
            return;
        }
        this._layingEgg = true;
        this._eggReady = false;
        this.holdState = undefined;
        this.holdStateEnum = undefined;
        this.currentStateEnum = States.flyToLay;
        this.currentState = resolveState(States.flyToLay, this);
    }

    // 上端に着いたあと1回だけ true を返す
    takeLaidEgg(): boolean {
        if (!this._eggReady) {
            return false;
        }
        this._eggReady = false;
        this._layingEgg = false;
        return true;
    }

    // ---------------------------------------------------------------- ペット同士の回避

    steerAwayFrom(other: IPetType) {
        if (this._steerCooldown > 0) {
            return;
        }
        const state = this.currentStateEnum;
        const toRight = other.left > this._left;
        let changed = false;
        if (
            (state === States.walkRight || state === States.runRight) &&
            toRight
        ) {
            changed = this.switchTo(
                state === States.walkRight ? States.walkLeft : States.runLeft,
            );
        } else if (
            (state === States.walkLeft || state === States.runLeft) &&
            !toRight
        ) {
            changed = this.switchTo(
                state === States.walkLeft ? States.walkRight : States.runRight,
            );
        } else if (state === States.fly || state === States.flyToLay) {
            const fly = this.currentState as FlyState;
            if (fly.isHeadingTo(other)) {
                for (
                    let i = 0;
                    i < MAX_RETARGET && fly.isHeadingTo(other);
                    i++
                ) {
                    fly.pickTarget();
                }
                changed = true;
            }
        }
        if (changed) {
            this._steerCooldown = STEER_COOLDOWN_TICKS;
        }
    }

    // そのペットの sequence にある状態にだけ切り替える
    private switchTo(state: States): boolean {
        const known = this.sequence.sequenceStates.some(
            (s) => s.state === state,
        );
        if (!known) {
            return false;
        }
        this.currentStateEnum = state;
        this.currentState = resolveState(state, this);
        return true;
    }

    get isFlying(): boolean {
        return isStateFlying(this.currentStateEnum);
    }

    get isPlaying(): boolean {
        return (
            this.isMoving &&
            (this.currentStateEnum === States.runRight ||
                this.currentStateEnum === States.runLeft)
        );
    }

    get emoji(): string {
        return '🐶';
    }

    get size(): PetSize {
        return this._size;
    }

    remove(): void {}
}
