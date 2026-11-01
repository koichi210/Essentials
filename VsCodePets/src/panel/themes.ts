import { ColorThemeKind, PetSize, Theme } from '../common/types';
import { Effect } from './effects/effect';
import { SnowEffect } from './effects/snow';
import { LeafEffect } from './effects/leaves';

interface AbstractThemeInfo {
    name: string;
    description: string;
    effect?: Effect;

    floor(size: PetSize): number;
    backgroundImageUrl(
        basePetUri: string,
        themeKind: ColorThemeKind,
        petSize: PetSize,
    ): string;
    foregroundImageUrl(
        basePetUri: string,
        themeKind: ColorThemeKind,
        petSize: PetSize,
    ): string;
}

export class ThemeInfo implements AbstractThemeInfo {
    name: string = '';
    description: string = '';
    effect?: Effect = undefined;

    // eslint-disable-next-line no-unused-vars
    floor(size: PetSize): number {
        return 0;
    }
    /* eslint-disable no-unused-vars */
    // 背景はライト/ダークで共通の1枚(暗くしない)
    backgroundImageUrl(
        basePetUri: string,
        themeKind: ColorThemeKind,
        petSize: PetSize,
    ): string {
        return `url('${basePetUri}/backgrounds/${this.name}/background.jpg')`;
    }
    foregroundImageUrl(
        basePetUri: string,
        themeKind: ColorThemeKind,
        petSize: PetSize,
    ): string {
        return '';
    }
    /* eslint-enable no-unused-vars */
}

// 季節の背景は1枚絵(横に繋げて並べる)。ペットの手前に重ねる前景は持たない
class SeasonThemeInfo extends ThemeInfo {
    floorBySize = { nano: 25, small: 35, medium: 45, large: 70 };

    floor(size: PetSize): number {
        return this.floorBySize[size] ?? this.floorBySize.nano;
    }
}

class SpringThemeInfo extends SeasonThemeInfo {
    name = 'spring';
    description = 'A spring theme';
}

class SummerThemeInfo extends SeasonThemeInfo {
    name = 'summer';
    description = 'A summer theme';
}

class AutumnThemeInfo extends SeasonThemeInfo {
    name = 'autumn';
    description = 'An autumn theme';
    effect = new LeafEffect();
}

class WinterThemeInfo extends SeasonThemeInfo {
    name = 'winter';
    description = 'A winter theme';
    effect = new SnowEffect();
}

// Map of theme name to theme info
export const THEMES: Record<Theme, ThemeInfo> = {
    none: {
        name: 'none',
        description: 'No theme',
        /* eslint-disable no-unused-vars */
        floor: (size: PetSize) => 0,
        backgroundImageUrl: (
            basePetUri: string,
            themeKind: ColorThemeKind,
            petSize: PetSize,
        ) => '',
        foregroundImageUrl: (
            basePetUri: string,
            themeKind: ColorThemeKind,
            petSize: PetSize,
        ) => '',
        /* eslint-enable no-unused-vars */
    },
    spring: new SpringThemeInfo(),
    summer: new SummerThemeInfo(),
    autumn: new AutumnThemeInfo(),
    winter: new WinterThemeInfo(),
};
