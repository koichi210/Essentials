import { PetColor } from '../../common/types';
import { BasePetType } from '../basepettype';
import { States } from '../states';

export class Secretary extends BasePetType {
    label = 'secretary';
    static possibleColors = [
        PetColor.enji,
        PetColor.blue,
        PetColor.red,
        PetColor.brown,
        PetColor.green,
    ];
    sequence = {
        startingState: States.sitIdle,
        sequenceStates: [
            {
                state: States.sitIdle,
                possibleNextStates: [States.walkRight, States.runRight],
            },
            {
                state: States.walkRight,
                possibleNextStates: [States.walkLeft, States.runLeft],
            },
            {
                state: States.runRight,
                possibleNextStates: [States.walkLeft, States.runLeft],
            },
            {
                state: States.walkLeft,
                possibleNextStates: [States.sitIdle],
            },
            {
                state: States.runLeft,
                possibleNextStates: [States.sitIdle],
            },
            {
                state: States.chase,
                possibleNextStates: [States.idleWithBall],
            },
            {
                state: States.idleWithBall,
                possibleNextStates: [
                    States.walkRight,
                    States.walkLeft,
                    States.runLeft,
                    States.runRight,
                ],
            },
        ],
    };
    get emoji(): string {
        return '🗂️';
    }
    get hello(): string {
        return ` こんにちは、秘書さんだよ 👋!`;
    }
}

export const SECRETARY_NAMES: ReadonlyArray<string> = [
    'ミライ',
    'ひなた',
    'さくら',
    'あおい',
    'あかり',
    'みどり',
    'ことは',
    'ゆず',
    'ほのか',
    'つむぎ',
];
