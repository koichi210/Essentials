export const enum PetColor {
    brown = 'brown',
    lightbrown = 'lightbrown',
    black = 'black',
    green = 'green',
    blue = 'blue',
    yellow = 'yellow',
    gray = 'gray',
    grayJimothy = 'gray_jimothy',
    purple = 'purple',
    red = 'red',
    white = 'white',
    orange = 'orange',
    akita = 'akita',
    socksblack = 'socks black',
    socksbeige = 'socks beige',
    socksbrown = 'socks brown',
    paintbeige = 'paint beige',
    paintblack = 'paint black',
    paintbrown = 'paint brown',
    magical = 'magical',
    warrior = 'warrior',
    pink = 'pink',
    enji = 'enji',
    null = 'null',
}

export const enum PetType {
    bunny = 'bunny',
    cat = 'cat',
    chicken = 'chicken',
    clippy = 'clippy',
    cockatiel = 'cockatiel',
    crab = 'crab',
    dog = 'dog',
    deno = 'deno',
    fox = 'fox',
    frog = 'frog',
    horse = 'horse',
    mod = 'mod',
    monkey = 'monkey',
    morph = 'morph',
    panda = 'panda',
    raccoon = 'raccoon',
    rat = 'rat',
    rocky = 'rocky',
    rubberduck = 'rubber-duck',
    secretary = 'secretary',
    skeleton = 'skeleton',
    snail = 'snail',
    snake = 'snake',
    squirrel = 'squirrel',
    totoro = 'totoro',
    turtle = 'turtle',
    zappy = 'zappy',
    null = 'null',
}

export const enum PetSpeed {
    still = 0,
    verySlow = 1,
    slow = 2,
    normal = 3,
    fast = 4,
    veryFast = 5,
}

export const enum PetSize {
    nano = 'nano',
    small = 'small',
    medium = 'medium',
    large = 'large',
}

export const enum ExtPosition {
    panel = 'panel',
    explorer = 'explorer',
}

export const enum Theme {
    none = 'none',
    spring = 'spring',
    summer = 'summer',
    autumn = 'autumn',
    winter = 'winter',
}

export const enum ColorThemeKind {
    light = 1,
    dark = 2,
    highContrast = 3,
    highContrastLight = 4,
}

export class WebviewMessage {
    text: string;
    command: string;

    constructor(text: string, command: string) {
        this.text = text;
        this.command = command;
    }
}

// スポーン時の選択肢。画像はすべてオリジナルに描き直したため、描いていない種類は外す:
// - cat / bunny / frog / squirrel: 上流でも画像がパスワード付き zip にしか無く、展開できない
// - clippy / deno / totoro / mod / morph / zappy / rocky: 他社・他作品のキャラクターなので描かない
// PetType enum と各ペットのクラスは上流との互換のため残している
export const ALL_PETS = [
    PetType.chicken,
    PetType.cockatiel,
    PetType.crab,
    PetType.dog,
    PetType.fox,
    PetType.horse,
    PetType.monkey,
    PetType.panda,
    PetType.raccoon,
    PetType.rat,
    PetType.rubberduck,
    PetType.secretary,
    PetType.skeleton,
    PetType.snail,
    PetType.snake,
    PetType.turtle,
];
export const ALL_COLORS = [
    PetColor.black,
    PetColor.brown,
    PetColor.lightbrown,
    PetColor.green,
    PetColor.yellow,
    PetColor.gray,
    PetColor.grayJimothy,
    PetColor.purple,
    PetColor.red,
    PetColor.white,
    PetColor.orange,
    PetColor.akita,
    PetColor.socksblack,
    PetColor.socksbeige,
    PetColor.socksbrown,
    PetColor.paintbeige,
    PetColor.paintblack,
    PetColor.paintbrown,
    PetColor.magical,
    PetColor.warrior,
    PetColor.enji,
    PetColor.null,
];
export const ALL_SCALES = [
    PetSize.nano,
    PetSize.small,
    PetSize.medium,
    PetSize.large,
];
export const ALL_THEMES = [
    Theme.none,
    Theme.spring,
    Theme.summer,
    Theme.autumn,
    Theme.winter,
];
